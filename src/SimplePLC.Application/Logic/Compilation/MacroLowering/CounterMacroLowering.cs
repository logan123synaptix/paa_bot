namespace SimplePLC.Application.Logic.Compilation.MacroLowering;

using SimplePLC.Application.Logic.Graph;
using SimplePLC.Domain.Enums;
using SimplePLC.Domain.Models;

/// <summary>
/// Hạ cấp (Macro Lowering) các khối Counter (CTU, CTD) thành tập luật logic cơ sở.
/// </summary>
public static class CounterMacroLowering
{
    /// <summary>
    /// Thực hiện mở rộng các Counter nodes thành các CandidateRule.
    /// </summary>
    public static void Lower(
        LogicGraph graph,
        ProductDefinition product,
        IReadOnlyDictionary<string, LogicNode> nodeMap,
        IReadOnlyList<LogicNode> counterNodes,
        List<CandidateRule> candidateRules,
        List<CompileDiagnostic> diagnostics)
    {
        for (int counterIdx = 0; counterIdx < counterNodes.Count; counterIdx++)
        {
            var counterNode = counterNodes[counterIdx];
            var cData = counterNode.CounterData ?? new CounterNodeData();
            ushort resolvedCuTagIndex = cData.CuTagIndex;
            ushort? resolvedResetTagIndex = cData.ResetTagIndex;
            ushort resolvedQTagIndex = cData.QTagIndex;
            TriggerNodeData? customCuTrigData = null;

            // Phân giải kết nối dây từ graph:
            // 1. Incoming edges (CU/CD hoặc R)
            foreach (var inEdge in graph.GetIncomingEdges(counterNode.Id))
            {
                if (nodeMap.TryGetValue(inEdge.SourceNodeId, out var srcNode))
                {
                    bool isResetPort = string.Equals(inEdge.TargetPort, "R", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(inEdge.TargetPort, "Reset", StringComparison.OrdinalIgnoreCase);

                    if (srcNode.Kind == LogicNodeKind.Input && srcNode.InputData != null)
                    {
                        if (isResetPort) resolvedResetTagIndex = srcNode.InputData.TagIndex;
                        else resolvedCuTagIndex = srcNode.InputData.TagIndex;
                    }
                    else if (srcNode.Kind == LogicNodeKind.Trigger && srcNode.TriggerData != null)
                    {
                        var trigInEdge = graph.GetIncomingEdges(srcNode.Id).FirstOrDefault();
                        if (trigInEdge != null && nodeMap.TryGetValue(trigInEdge.SourceNodeId, out var inpNode) && inpNode.Kind == LogicNodeKind.Input && inpNode.InputData != null)
                        {
                            if (isResetPort)
                            {
                                resolvedResetTagIndex = inpNode.InputData.TagIndex;
                            }
                            else
                            {
                                resolvedCuTagIndex = inpNode.InputData.TagIndex;
                                customCuTrigData = srcNode.TriggerData;
                            }
                        }
                    }
                }
            }

            // 2. Outgoing edges: Hỗ trợ Fan-out ra nhiều Action nodes
            var downstreamActions = new List<(LogicNode ActionNode, ActionNodeData ActionData, TagDefinition TargetTag)>();
            foreach (var outEdge in graph.GetOutgoingEdges(counterNode.Id))
            {
                if (nodeMap.TryGetValue(outEdge.TargetNodeId, out var tgtNode) && tgtNode.Kind == LogicNodeKind.Action && tgtNode.ActionData != null)
                {
                    var targetTag = product.FindTagByIndex(tgtNode.ActionData.TargetTagIndex);
                    if (targetTag != null)
                    {
                        downstreamActions.Add((tgtNode, tgtNode.ActionData, targetTag));
                    }
                }
            }

            // Fallback: Nếu không nối Action nào nhưng Counter có cấu hình OutputTag (Standalone mode)
            if (downstreamActions.Count == 0 && resolvedQTagIndex != 0)
            {
                var standaloneTag = product.FindTagByIndex(resolvedQTagIndex);
                if (standaloneTag != null)
                {
                    downstreamActions.Add((counterNode, new ActionNodeData { TargetTagIndex = standaloneTag.TagIndex, Type = ActionKind.SetTag, Parameter = 1 }, standaloneTag));
                }
            }

            var cuTag = product.FindTagByIndex(resolvedCuTagIndex);
            var cvTag = product.FindTagByIndex(cData.CvTagIndex);
            if (cvTag == null)
            {
                // Quy ước: Counter thứ i dùng tag COUNTER[i] làm CV nếu cData.CvTagIndex chưa được cấu hình
                if (counterIdx >= 0)
                {
                    var defaultCounterTag = product.FindTagByName($"COUNTER{counterIdx}");
                    if (defaultCounterTag != null)
                    {
                        cvTag = defaultCounterTag;
                        cData.CvTagIndex = defaultCounterTag.TagIndex;
                    }
                }
            }

            TagDefinition? resetTag = resolvedResetTagIndex.HasValue ? product.FindTagByIndex(resolvedResetTagIndex.Value) : null;

            var counterLabel = CompilationHelpers.GetNodeLabel(counterNode, "Counter");
            if (cuTag == null)
            {
                diagnostics.Add(new CompileDiagnostic(
                    RuleCompiler.ErrTagNotFound,
                    DiagnosticSeverity.Error,
                    $"Counter node '{counterLabel}' references unknown Count Input Tag {resolvedCuTagIndex}.",
                    NodeId: counterNode.Id));
                continue;
            }

            if (cvTag == null)
            {
                diagnostics.Add(new CompileDiagnostic(
                    RuleCompiler.ErrTagNotFound,
                    DiagnosticSeverity.Error,
                    $"Counter node '{counterLabel}' references unknown CV Register Tag {cData.CvTagIndex}.",
                    NodeId: counterNode.Id));
                continue;
            }

            if (downstreamActions.Count == 0)
            {
                diagnostics.Add(new CompileDiagnostic(
                    RuleCompiler.ErrTagNotFound,
                    DiagnosticSeverity.Error,
                    $"Counter node '{counterLabel}' references unknown Output Tag {cData.QTagIndex}.",
                    NodeId: counterNode.Id));
                continue;
            }

            string macroId = counterNode.Id;
            string labelPrefix = !string.IsNullOrWhiteSpace(counterNode.Label) ? counterNode.Label : counterNode.Id;
            string macroType = cData.Type == CounterMacroType.Ctd ? "CTD" : "CTU";

            int ruleExpIndex = 0;

            // R0: Count Pulse
            // CTU: CU ON_RISE -> CV += 1
            // CTD: CD ON_RISE -> CV += -1 (decrement)
            int incStep = cData.Type == CounterMacroType.Ctd ? -1 : 1;
            TriggerModel trigCount;
            if (customCuTrigData != null && customCuTrigData.CompareOp != CompareOperator.None)
            {
                bool cIsFloat = CompilationHelpers.IsFloatThreshold(customCuTrigData.ThresholdLo, customCuTrigData.ThresholdHi, cuTag);
                trigCount = new TriggerModel(cuTag, customCuTrigData.Type)
                {
                    CompareOp = customCuTrigData.CompareOp,
                    ThresholdLo = customCuTrigData.ThresholdLo,
                    ThresholdHi = customCuTrigData.ThresholdHi,
                    IsFloat = cIsFloat,
                    ForMs = 0
                };
            }
            else
            {
                trigCount = new TriggerModel(cuTag, customCuTrigData?.Type ?? TriggerKind.OnRise) { ForMs = 0 };
            }
            var actCount = new ActionModel(cvTag, ActionKind.IncrementCounter, incStep);
            var guardCount = resetTag != null ? new GuardModel(resetTag, negated: true) : GuardModel.Empty;
            var originCount = new GeneratedRuleOrigin(counterNode.Id, macroId, macroType, ruleExpIndex++, counterNode.Label);
            candidateRules.Add(new CandidateRule(counterNode, trigCount, actCount, guardCount, $"{labelPrefix}_{macroType}_COUNT", originCount));

            // R1: Reset (nếu có resetTag)
            // CTU: Reset ON_RISE -> SET CV = 0
            // CTD: Load/Reset ON_RISE -> SET CV = PresetValue
            if (resetTag != null)
            {
                var trigReset = new TriggerModel(resetTag, TriggerKind.OnRise) { ForMs = 0 };
                int resetTargetVal = cData.Type == CounterMacroType.Ctd ? cData.PresetValue : 0;
                var actReset = new ActionModel(cvTag, ActionKind.SetTag, resetTargetVal);
                var originReset = new GeneratedRuleOrigin(counterNode.Id, macroId, macroType, ruleExpIndex++, counterNode.Label);
                candidateRules.Add(new CandidateRule(counterNode, trigReset, actReset, GuardModel.Empty, $"{labelPrefix}_{macroType}_RESET", originReset));
            }

            // R2 & R3: Threshold Trip & Clear cho toàn bộ các Action hạ nguồn
            var tripOp = cData.Type == CounterMacroType.Ctd ? CompareOperator.LessThanOrEqual : CompareOperator.GreaterThanOrEqual;
            int tripThreshold = cData.Type == CounterMacroType.Ctd ? 0 : cData.PresetValue;

            var clearOp = cData.Type == CounterMacroType.Ctd ? CompareOperator.GreaterThan : CompareOperator.LessThan;
            int clearThreshold = cData.Type == CounterMacroType.Ctd ? 0 : cData.PresetValue;

            for (int actIdx = 0; actIdx < downstreamActions.Count; actIdx++)
            {
                var (actionNode, actionData, qTag) = downstreamActions[actIdx];
                int actionParamVal = actionData.Parameter != 0 ? actionData.Parameter : 1;
                var actTrip = new ActionModel(qTag, actionData.Type, actionParamVal);
                var actClear = new ActionModel(qTag, ActionKind.SetTag, 0);

                string actionSuffix = downstreamActions.Count > 1 ? $"_{qTag.Name}" : "";
                string displayLabel = downstreamActions.Count > 1 ? $"{counterNode.Label ?? macroType} -> {qTag.Name}" : counterNode.Label ?? macroType;

                // Threshold Trip (Khi đạt ngưỡng -> kích hoạt Action)
                var trigTrip = new TriggerModel(cvTag, TriggerKind.OnChange) { CompareOp = tripOp, ThresholdLo = tripThreshold, ForMs = 0 };
                var originTrip = new GeneratedRuleOrigin(counterNode.Id, macroId, macroType, ruleExpIndex++, displayLabel);
                candidateRules.Add(new CandidateRule(counterNode, trigTrip, actTrip, GuardModel.Empty, $"{labelPrefix}_{macroType}_TRIP{actionSuffix}", originTrip));

                // Threshold Clear (Khi chưa đạt / sau reset -> xóa Action về 0)
                var trigClear = new TriggerModel(cvTag, TriggerKind.OnChange) { CompareOp = clearOp, ThresholdLo = clearThreshold, ForMs = 0 };
                var originClear = new GeneratedRuleOrigin(counterNode.Id, macroId, macroType, ruleExpIndex++, displayLabel);
                candidateRules.Add(new CandidateRule(counterNode, trigClear, actClear, GuardModel.Empty, $"{labelPrefix}_{macroType}_CLEAR{actionSuffix}", originClear));
            }
        }
    }
}
