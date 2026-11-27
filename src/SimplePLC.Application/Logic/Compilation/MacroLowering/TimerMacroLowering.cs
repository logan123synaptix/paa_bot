namespace SimplePLC.Application.Logic.Compilation.MacroLowering;

using SimplePLC.Application.Logic.Graph;
using SimplePLC.Domain.Enums;
using SimplePLC.Domain.Models;

/// <summary>
/// Hạ cấp (Macro Lowering) các khối Timer (TON, TOF, TP, CYCLE) thành tập luật logic cơ sở.
/// </summary>
public static class TimerMacroLowering
{
    /// <summary>
    /// Thực hiện mở rộng các Timer nodes thành các CandidateRule.
    /// </summary>
    public static void Lower(
        LogicGraph graph,
        ProductDefinition product,
        IReadOnlyDictionary<string, LogicNode> nodeMap,
        IReadOnlyList<LogicNode> timerNodes,
        List<CandidateRule> candidateRules,
        List<CompileDiagnostic> diagnostics)
    {
        foreach (var timerNode in timerNodes)
        {
            var tData = timerNode.TimerData ?? new TimerNodeData();
            ushort resolvedInTagIndex = tData.InTagIndex;
            ushort resolvedQTagIndex = tData.QTagIndex;
            TriggerNodeData? customTrigData = null;
            GuardModel interlockGuard = GuardModel.Empty;

            // Phân giải kết nối ngõ vào của Timer:
            // Hỗ trợ nhận từ Input (Boolean), Trigger (Comparator/Edge), hoặc Guard (Interlock)
            var inEdge = graph.GetIncomingEdges(timerNode.Id).FirstOrDefault();
            if (inEdge != null && nodeMap.TryGetValue(inEdge.SourceNodeId, out var srcNode))
            {
                if (srcNode.Kind == LogicNodeKind.Input && srcNode.InputData != null)
                {
                    resolvedInTagIndex = srcNode.InputData.TagIndex;
                }
                else if (srcNode.Kind == LogicNodeKind.Trigger && srcNode.TriggerData != null)
                {
                    customTrigData = srcNode.TriggerData;
                    var trigInEdge = graph.GetIncomingEdges(srcNode.Id).FirstOrDefault();
                    if (trigInEdge != null && nodeMap.TryGetValue(trigInEdge.SourceNodeId, out var inpNode) && inpNode.Kind == LogicNodeKind.Input && inpNode.InputData != null)
                    {
                        resolvedInTagIndex = inpNode.InputData.TagIndex;
                    }
                }
                else if (srcNode.Kind == LogicNodeKind.Guard && srcNode.GuardData != null)
                {
                    var gTag = product.FindTagByIndex(srcNode.GuardData.TagIndex);
                    if (gTag != null)
                    {
                        interlockGuard = new GuardModel(gTag, srcNode.GuardData.Negated);
                    }

                    // Upstream của Guard là Trigger
                    var guardInEdge = graph.GetIncomingEdges(srcNode.Id).FirstOrDefault();
                    if (guardInEdge != null && nodeMap.TryGetValue(guardInEdge.SourceNodeId, out var trigNode) && trigNode.Kind == LogicNodeKind.Trigger && trigNode.TriggerData != null)
                    {
                        customTrigData = trigNode.TriggerData;
                        var trigInEdge = graph.GetIncomingEdges(trigNode.Id).FirstOrDefault();
                        if (trigInEdge != null && nodeMap.TryGetValue(trigInEdge.SourceNodeId, out var inpNode) && inpNode.Kind == LogicNodeKind.Input && inpNode.InputData != null)
                        {
                            resolvedInTagIndex = inpNode.InputData.TagIndex;
                        }
                    }
                }
            }

            // Phân giải các kết nối ngõ ra: hỗ trợ Fan-out ra nhiều Action nodes
            var downstreamActions = new List<(LogicNode ActionNode, ActionNodeData ActionData, TagDefinition TargetTag)>();
            foreach (var outEdge in graph.GetOutgoingEdges(timerNode.Id))
            {
                if (nodeMap.TryGetValue(outEdge.TargetNodeId, out var tgtNode) &&
                    tgtNode.Kind == LogicNodeKind.Action &&
                    tgtNode.ActionData != null)
                {
                    var aTag = product.FindTagByIndex(tgtNode.ActionData.TargetTagIndex);
                    if (aTag != null)
                    {
                        downstreamActions.Add((tgtNode, tgtNode.ActionData, aTag));
                    }
                }
            }

            // Fallback: Nếu không nối dây Action nào mà Timer có chọn QTagIndex trong Inspector (Standalone mode)
            if (downstreamActions.Count == 0 && resolvedQTagIndex != 0)
            {
                var standaloneTag = product.FindTagByIndex(resolvedQTagIndex);
                if (standaloneTag != null)
                {
                    downstreamActions.Add((timerNode, new ActionNodeData { TargetTagIndex = standaloneTag.TagIndex, Type = ActionKind.SetTag, Parameter = 1 }, standaloneTag));
                }
            }

            var inTag = product.FindTagByIndex(resolvedInTagIndex);
            var timerLabel = CompilationHelpers.GetNodeLabel(timerNode, "Timer");
            if (inTag == null)
            {
                diagnostics.Add(new CompileDiagnostic(
                    RuleCompiler.ErrTagNotFound,
                    DiagnosticSeverity.Error,
                    $"Timer node '{timerLabel}' references unknown Input Tag {resolvedInTagIndex}.",
                    NodeId: timerNode.Id));
                continue;
            }

            if (downstreamActions.Count == 0)
            {
                diagnostics.Add(new CompileDiagnostic(
                    RuleCompiler.ErrTagNotFound,
                    DiagnosticSeverity.Error,
                    $"Timer node '{timerLabel}' references unknown Output Tag {tData.QTagIndex}.",
                    NodeId: timerNode.Id));
                continue;
            }

            // Master Q tag dùng cho trạng thái định thời & khóa liên động (luôn lấy Action đầu tiên làm Master)
            var primaryQTag = downstreamActions[0].TargetTag;
            string macroId = timerNode.Id;
            string labelPrefix = !string.IsNullOrWhiteSpace(timerNode.Label) ? timerNode.Label : timerNode.Id;

            for (int actIdx = 0; actIdx < downstreamActions.Count; actIdx++)
            {
                var (actionNode, actionData, qTag) = downstreamActions[actIdx];
                int actionParamVal = actionData.Parameter != 0 ? actionData.Parameter : 1;
                var actOn = new ActionModel(qTag, actionData.Type, actionParamVal);
                var actOff = new ActionModel(qTag, ActionKind.SetTag, 0);

                string actionSuffix = downstreamActions.Count > 1 ? $"_{qTag.Name}" : "";
                string displayLabel = downstreamActions.Count > 1 ? $"{timerNode.Label ?? "Timer"} -> {qTag.Name}" : timerNode.Label ?? "Timer";

                switch (tData.Type)
                {
                    case TimerMacroType.Ton:
                    {
                        int expIndexBase = actIdx * 2;
                        if (customTrigData != null && customTrigData.CompareOp != CompareOperator.None)
                        {
                            bool cIsFloat = CompilationHelpers.IsFloatThreshold(customTrigData.ThresholdLo, customTrigData.ThresholdHi, inTag);
                            // R0: Điều kiện đúng duy trì đủ PresetMs -> SET Q = 1
                            var trig0 = new TriggerModel(inTag, customTrigData.Type)
                            {
                                CompareOp = customTrigData.CompareOp,
                                ThresholdLo = customTrigData.ThresholdLo,
                                ThresholdHi = customTrigData.ThresholdHi,
                                IsFloat = cIsFloat,
                                ForMs = tData.PresetMs
                            };
                            var origin0 = new GeneratedRuleOrigin(timerNode.Id, macroId, "TON", expIndexBase, displayLabel);
                            candidateRules.Add(new CandidateRule(timerNode, trig0, actOn, interlockGuard, $"{labelPrefix}_TON_ON{actionSuffix}", origin0));

                            // R1: Điều kiện đảo ngược rớt ngưỡng -> tức thời Reset Q = 0
                            var invertedOp = CompilationHelpers.InvertCompareOp(customTrigData.CompareOp);
                            var trig1 = new TriggerModel(inTag, TriggerKind.OnChange)
                            {
                                CompareOp = invertedOp,
                                ThresholdLo = customTrigData.ThresholdLo,
                                ThresholdHi = customTrigData.ThresholdHi,
                                IsFloat = cIsFloat,
                                ForMs = 0
                            };
                            var origin1 = new GeneratedRuleOrigin(timerNode.Id, macroId, "TON", expIndexBase + 1, displayLabel);
                            candidateRules.Add(new CandidateRule(timerNode, trig1, actOff, GuardModel.Empty, $"{labelPrefix}_TON_OFF{actionSuffix}", origin1));
                        }
                        else
                        {
                            // R0: IN ON_RISE for PT -> SET Q = 1
                            var trigKind0 = customTrigData?.Type ?? TriggerKind.OnRise;
                            var trig0 = new TriggerModel(inTag, trigKind0) { ForMs = tData.PresetMs };
                            var origin0 = new GeneratedRuleOrigin(timerNode.Id, macroId, "TON", expIndexBase, displayLabel);
                            candidateRules.Add(new CandidateRule(timerNode, trig0, actOn, interlockGuard, $"{labelPrefix}_TON_ON{actionSuffix}", origin0));

                            // R1: IN ON_FALL for 0 -> SET Q = 0
                            var trig1 = new TriggerModel(inTag, TriggerKind.OnFall) { ForMs = 0 };
                            var origin1 = new GeneratedRuleOrigin(timerNode.Id, macroId, "TON", expIndexBase + 1, displayLabel);
                            candidateRules.Add(new CandidateRule(timerNode, trig1, actOff, GuardModel.Empty, $"{labelPrefix}_TON_OFF{actionSuffix}", origin1));
                        }
                        break;
                    }

                    case TimerMacroType.Tof:
                    {
                        int expIndexBase = actIdx * 2;
                        if (customTrigData != null && customTrigData.CompareOp != CompareOperator.None)
                        {
                            bool cIsFloat = CompilationHelpers.IsFloatThreshold(customTrigData.ThresholdLo, customTrigData.ThresholdHi, inTag);
                            // R0: Điều kiện đúng -> SET Q = 1
                            var trig0 = new TriggerModel(inTag, customTrigData.Type)
                            {
                                CompareOp = customTrigData.CompareOp,
                                ThresholdLo = customTrigData.ThresholdLo,
                                ThresholdHi = customTrigData.ThresholdHi,
                                IsFloat = cIsFloat,
                                ForMs = 0
                            };
                            var origin0 = new GeneratedRuleOrigin(timerNode.Id, macroId, "TOF", expIndexBase, displayLabel);
                            candidateRules.Add(new CandidateRule(timerNode, trig0, actOn, interlockGuard, $"{labelPrefix}_TOF_ON{actionSuffix}", origin0));

                            // R1: Điều kiện đảo ngược duy trì đủ PresetMs, Guard primaryQTag == 1 -> SET Q = 0
                            var invertedOp = CompilationHelpers.InvertCompareOp(customTrigData.CompareOp);
                            var trig1 = new TriggerModel(inTag, TriggerKind.OnChange)
                            {
                                CompareOp = invertedOp,
                                ThresholdLo = customTrigData.ThresholdLo,
                                ThresholdHi = customTrigData.ThresholdHi,
                                IsFloat = cIsFloat,
                                ForMs = tData.PresetMs
                            };
                            var guard1 = new GuardModel(primaryQTag, negated: false);
                            var origin1 = new GeneratedRuleOrigin(timerNode.Id, macroId, "TOF", expIndexBase + 1, displayLabel);
                            candidateRules.Add(new CandidateRule(timerNode, trig1, actOff, guard1, $"{labelPrefix}_TOF_OFF{actionSuffix}", origin1));
                        }
                        else
                        {
                            // R0: IN ON_RISE for 0 -> SET Q = 1
                            var trig0 = new TriggerModel(inTag, TriggerKind.OnRise) { ForMs = 0 };
                            var origin0 = new GeneratedRuleOrigin(timerNode.Id, macroId, "TOF", expIndexBase, displayLabel);
                            candidateRules.Add(new CandidateRule(timerNode, trig0, actOn, interlockGuard, $"{labelPrefix}_TOF_ON{actionSuffix}", origin0));

                            // R1: IN ON_FALL for PT, Guard primaryQTag == 1 -> SET Q = 0
                            var trig1 = new TriggerModel(inTag, TriggerKind.OnFall) { ForMs = tData.PresetMs };
                            var guard1 = new GuardModel(primaryQTag, negated: false);
                            var origin1 = new GeneratedRuleOrigin(timerNode.Id, macroId, "TOF", expIndexBase + 1, displayLabel);
                            candidateRules.Add(new CandidateRule(timerNode, trig1, actOff, guard1, $"{labelPrefix}_TOF_OFF{actionSuffix}", origin1));
                        }
                        break;
                    }

                    case TimerMacroType.Tp:
                    {
                        int expIndexBase = actIdx * 2;
                        if (customTrigData != null && customTrigData.CompareOp != CompareOperator.None)
                        {
                            bool cIsFloat = CompilationHelpers.IsFloatThreshold(customTrigData.ThresholdLo, customTrigData.ThresholdHi, inTag);
                            // R0: Điều kiện đúng, Guard primaryQTag == 0 -> SET Q = 1
                            var trig0 = new TriggerModel(inTag, customTrigData.Type)
                            {
                                CompareOp = customTrigData.CompareOp,
                                ThresholdLo = customTrigData.ThresholdLo,
                                ThresholdHi = customTrigData.ThresholdHi,
                                IsFloat = cIsFloat,
                                ForMs = 0
                            };
                            var guard0 = new GuardModel(primaryQTag, negated: true);
                            var origin0 = new GeneratedRuleOrigin(timerNode.Id, macroId, "TP", expIndexBase, displayLabel);
                            candidateRules.Add(new CandidateRule(timerNode, trig0, actOn, guard0, $"{labelPrefix}_TP_START{actionSuffix}", origin0));

                            // R1: primaryQTag ON_RISE for PT -> SET Q = 0
                            var trig1 = new TriggerModel(primaryQTag, TriggerKind.OnRise) { ForMs = tData.PresetMs };
                            var origin1 = new GeneratedRuleOrigin(timerNode.Id, macroId, "TP", expIndexBase + 1, displayLabel);
                            candidateRules.Add(new CandidateRule(timerNode, trig1, actOff, GuardModel.Empty, $"{labelPrefix}_TP_EXPIRE{actionSuffix}", origin1));
                        }
                        else
                        {
                            // R0: IN ON_RISE for 0, Guard primaryQTag == 0 -> SET Q = 1
                            var trig0 = new TriggerModel(inTag, TriggerKind.OnRise) { ForMs = 0 };
                            var guard0 = new GuardModel(primaryQTag, negated: true);
                            var origin0 = new GeneratedRuleOrigin(timerNode.Id, macroId, "TP", expIndexBase, displayLabel);
                            candidateRules.Add(new CandidateRule(timerNode, trig0, actOn, guard0, $"{labelPrefix}_TP_START{actionSuffix}", origin0));

                            // R1: primaryQTag ON_RISE for PT -> SET Q = 0
                            var trig1 = new TriggerModel(primaryQTag, TriggerKind.OnRise) { ForMs = tData.PresetMs };
                            var origin1 = new GeneratedRuleOrigin(timerNode.Id, macroId, "TP", expIndexBase + 1, displayLabel);
                            candidateRules.Add(new CandidateRule(timerNode, trig1, actOff, GuardModel.Empty, $"{labelPrefix}_TP_EXPIRE{actionSuffix}", origin1));
                        }
                        break;
                    }

                    case TimerMacroType.Cycle:
                    {
                        int expIndexBase = actIdx * 4;
                        uint onTime = tData.OnTimeMs > 0 ? tData.OnTimeMs : (tData.PresetMs > 0 ? tData.PresetMs : 1000);
                        uint offTime = tData.OffTimeMs > 0 ? tData.OffTimeMs : 1000;
                        bool startWithOn = tData.StartWithOn;

                        // R0: Bắt đầu chu kỳ khi In lên 1
                        var trig0 = new TriggerModel(inTag, TriggerKind.OnRise) { ForMs = startWithOn ? 0 : offTime };
                        var guard0 = new GuardModel(primaryQTag, negated: true);
                        var origin0 = new GeneratedRuleOrigin(timerNode.Id, macroId, "CYCLE", expIndexBase, displayLabel);
                        candidateRules.Add(new CandidateRule(timerNode, trig0, actOn, guard0, $"{labelPrefix}_CYCLE_START{actionSuffix}", origin0));

                        // R1: Pha BẬT hết hạn: primaryQTag ON_RISE for OnTime -> SET Q = 0
                        var trig1 = new TriggerModel(primaryQTag, TriggerKind.OnRise) { ForMs = onTime };
                        var origin1 = new GeneratedRuleOrigin(timerNode.Id, macroId, "CYCLE", expIndexBase + 1, displayLabel);
                        candidateRules.Add(new CandidateRule(timerNode, trig1, actOff, GuardModel.Empty, $"{labelPrefix}_CYCLE_ON_EXPIRE{actionSuffix}", origin1));

                        // R2: Pha NGHỈ hết hạn: primaryQTag ON_FALL for OffTime, Guard In == 1 -> SET Q = 1
                        var trig2 = new TriggerModel(primaryQTag, TriggerKind.OnFall) { ForMs = offTime };
                        var guard2 = new GuardModel(inTag, negated: false);
                        var origin2 = new GeneratedRuleOrigin(timerNode.Id, macroId, "CYCLE", expIndexBase + 2, displayLabel);
                        candidateRules.Add(new CandidateRule(timerNode, trig2, actOn, guard2, $"{labelPrefix}_CYCLE_OFF_EXPIRE{actionSuffix}", origin2));

                        // R3: Ngắt khẩn cấp / Dừng an toàn khi In về 0: In ON_FALL for 0 -> SET Q = 0
                        var trig3 = new TriggerModel(inTag, TriggerKind.OnFall) { ForMs = 0 };
                        var origin3 = new GeneratedRuleOrigin(timerNode.Id, macroId, "CYCLE", expIndexBase + 3, displayLabel);
                        candidateRules.Add(new CandidateRule(timerNode, trig3, actOff, GuardModel.Empty, $"{labelPrefix}_CYCLE_STOP{actionSuffix}", origin3));
                        break;
                    }
                }
            }
        }
    }
}
