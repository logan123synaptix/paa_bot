namespace SimplePLC.Application.Tests;

using SimplePLC.Application.Logic.Compilation;
using SimplePLC.Application.Logic.Compilation.MacroLowering;
using SimplePLC.Application.Logic.Graph;
using SimplePLC.Domain.Enums;
using SimplePLC.Domain.Models;
using Xunit;

/// <summary>
/// Bộ kiểm thử xác thực cấu trúc phân rã module hóa của RuleCompiler (Pipeline Passes & Macro Lowering).
/// </summary>
public sealed class RuleCompilerModularizationTests
{
    private readonly ProductDefinition _product = ProductDefinition.CreateRemoteIo8Di8Do4Ai();

    [Fact]
    public void ScaleMacroLowering_WithIntegerGainOffset_GeneratesAuxParameterCorrectly()
    {
        // Arrange
        var ai0 = _product.FindTagByName("AI0")!;
        var vreg0 = _product.FindTagByName("VREG0")!;

        var graph = new LogicGraph();
        var scaleNode = new LogicNode
        {
            Id = "scale_1",
            Kind = LogicNodeKind.Scale,
            ScaleData = new ScaleNodeData
            {
                InTagIndex = ai0.TagIndex,
                OutTagIndex = vreg0.TagIndex,
                Gain = 2.5,
                Offset = 50.0
            }
        };
        graph.Nodes.Add(scaleNode);

        var candidateRules = new List<CandidateRule>();
        var diagnostics = new List<CompileDiagnostic>();
        var nodeMap = graph.Nodes.ToDictionary(n => n.Id);

        // Act
        ScaleMacroLowering.Lower(graph, _product, nodeMap, new[] { scaleNode }, candidateRules, diagnostics);

        // Assert
        Assert.Empty(diagnostics);
        Assert.Single(candidateRules);

        var rule = candidateRules[0];
        Assert.Equal(ActionKind.ScaleTag, rule.Action.Type);
        Assert.Equal(50.0, rule.Action.AuxParameter);
        Assert.Equal(2500, rule.Action.Parameter); // Gain 2.5 * 1000 = 2500
        Assert.Equal(50, rule.Trigger.ThresholdHi); // Backward compatibility
    }

    [Fact]
    public void ScaleMacroLowering_WithFloatTags_PreservesFloatAuxParameter()
    {
        // Arrange
        ushort nextIdx = (ushort)(_product.Tags.Where(t => t.TagIndex < 1000).Max(t => t.TagIndex) + 1);
        ushort inIdx = nextIdx++;
        ushort outIdx = nextIdx++;

        var floatTagIn = new TagDefinition(inIdx, "FLOAT_IN", TagKind.AnalogInput, TagDataType.Float32, isReadOnly: true);
        var floatTagOut = new TagDefinition(outIdx, "FLOAT_OUT", TagKind.VirtualRegister, TagDataType.Float32, isReadOnly: false);
        var customTags = _product.Tags.Concat(new[] { floatTagIn, floatTagOut }).ToList();
        var floatProduct = new ProductDefinition(
            "TestPLC",
            deviceClass: 1,
            productVariant: 1,
            maxRules: 100,
            resources: _product.Resources,
            tags: customTags,
            wireProfile: 2);

        var graph = new LogicGraph();
        var scaleNode = new LogicNode
        {
            Id = "scale_float",
            Kind = LogicNodeKind.Scale,
            ScaleData = new ScaleNodeData
            {
                InTagIndex = inIdx,
                OutTagIndex = outIdx,
                Gain = 1.25,
                Offset = 10.75
            }
        };
        graph.Nodes.Add(scaleNode);

        var candidateRules = new List<CandidateRule>();
        var diagnostics = new List<CompileDiagnostic>();
        var nodeMap = graph.Nodes.ToDictionary(n => n.Id);

        // Act
        ScaleMacroLowering.Lower(graph, floatProduct, nodeMap, new[] { scaleNode }, candidateRules, diagnostics);

        // Assert
        Assert.Empty(diagnostics);
        Assert.Single(candidateRules);

        var rule = candidateRules[0];
        Assert.True(rule.Action.IsFloat);
        Assert.Equal(10.75, rule.Action.AuxParameter, precision: 4);
        Assert.Equal(1.25, rule.Action.Parameter, precision: 4);
    }

    [Fact]
    public void TimerMacroLowering_TonMacro_ExpandsToOnAndOffCandidateRules()
    {
        // Arrange
        var di0 = _product.FindTagByName("DI0")!;
        var do0 = _product.FindTagByName("DO0")!;

        var graph = new LogicGraph();
        var timerNode = new LogicNode
        {
            Id = "timer_1",
            Kind = LogicNodeKind.Timer,
            TimerData = new TimerNodeData
            {
                Type = TimerMacroType.Ton,
                InTagIndex = di0.TagIndex,
                QTagIndex = do0.TagIndex,
                PresetMs = 5000
            }
        };
        graph.Nodes.Add(timerNode);

        var candidateRules = new List<CandidateRule>();
        var diagnostics = new List<CompileDiagnostic>();
        var nodeMap = graph.Nodes.ToDictionary(n => n.Id);

        // Act
        TimerMacroLowering.Lower(graph, _product, nodeMap, new[] { timerNode }, candidateRules, diagnostics);

        // Assert
        Assert.Empty(diagnostics);
        Assert.Equal(2, candidateRules.Count); // TON_ON & TON_OFF
        Assert.Contains(candidateRules, r => r.RuleName.EndsWith("_TON_ON") && r.Trigger.ForMs == 5000);
        Assert.Contains(candidateRules, r => r.RuleName.EndsWith("_TON_OFF") && r.Trigger.ForMs == 0);
    }

    [Fact]
    public void TimerMacroLowering_WithMultipleDownstreamActions_GeneratesRulesForAllActions()
    {
        // Arrange
        var di0 = _product.FindTagByName("DI0")!;
        var do0 = _product.FindTagByName("DO0")!;
        var vreg0 = _product.FindTagByName("VREG0")!;

        var graph = new LogicGraph();
        var timerNode = new LogicNode
        {
            Id = "timer_tp",
            Label = "TP Timer",
            Kind = LogicNodeKind.Timer,
            TimerData = new TimerNodeData
            {
                Type = TimerMacroType.Tp,
                InTagIndex = di0.TagIndex,
                QTagIndex = do0.TagIndex,
                PresetMs = 3000
            }
        };
        var act1 = new LogicNode
        {
            Id = "act_1",
            Kind = LogicNodeKind.Action,
            ActionData = new ActionNodeData
            {
                TargetTagIndex = do0.TagIndex,
                Type = ActionKind.SetTag,
                Parameter = 1
            }
        };
        var act2 = new LogicNode
        {
            Id = "act_2",
            Kind = LogicNodeKind.Action,
            ActionData = new ActionNodeData
            {
                TargetTagIndex = vreg0.TagIndex,
                Type = ActionKind.SetTag,
                Parameter = 1
            }
        };

        graph.Nodes.Add(timerNode);
        graph.Nodes.Add(act1);
        graph.Nodes.Add(act2);

        // Fan-out: Timer Q connects to both Action 1 and Action 2
        graph.Edges.Add(new LogicEdge("timer_tp", "act_1", "Q", "In"));
        graph.Edges.Add(new LogicEdge("timer_tp", "act_2", "Q", "In"));

        var candidateRules = new List<CandidateRule>();
        var diagnostics = new List<CompileDiagnostic>();
        var nodeMap = graph.Nodes.ToDictionary(n => n.Id);

        // Act
        TimerMacroLowering.Lower(graph, _product, nodeMap, new[] { timerNode }, candidateRules, diagnostics);

        // Assert
        Assert.Empty(diagnostics);
        // Each action produces 2 rules (START & EXPIRE), total 4 rules
        Assert.Equal(4, candidateRules.Count);
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == do0.TagIndex && r.RuleName.Contains("TP_START"));
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == do0.TagIndex && r.RuleName.Contains("TP_EXPIRE"));
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == vreg0.TagIndex && r.RuleName.Contains("TP_START"));
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == vreg0.TagIndex && r.RuleName.Contains("TP_EXPIRE"));
    }

    [Fact]
    public void CounterMacroLowering_WithMultipleDownstreamActions_GeneratesTripAndClearForAllActions()
    {
        // Arrange
        var di0 = _product.FindTagByName("DI0")!;
        var do0 = _product.FindTagByName("DO0")!;
        var vreg0 = _product.FindTagByName("VREG0")!;
        var counterTag = _product.FindTagByName("COUNTER0") ?? _product.FindTagByIndex(116)!;

        var graph = new LogicGraph();
        var counterNode = new LogicNode
        {
            Id = "cnt_1",
            Kind = LogicNodeKind.Counter,
            CounterData = new CounterNodeData
            {
                Type = CounterMacroType.Ctu,
                CuTagIndex = di0.TagIndex,
                CvTagIndex = counterTag.TagIndex,
                QTagIndex = do0.TagIndex,
                PresetValue = 10
            }
        };
        var act1 = new LogicNode
        {
            Id = "act_1",
            Kind = LogicNodeKind.Action,
            ActionData = new ActionNodeData
            {
                TargetTagIndex = do0.TagIndex,
                Type = ActionKind.SetTag,
                Parameter = 1
            }
        };
        var act2 = new LogicNode
        {
            Id = "act_2",
            Kind = LogicNodeKind.Action,
            ActionData = new ActionNodeData
            {
                TargetTagIndex = vreg0.TagIndex,
                Type = ActionKind.SetTag,
                Parameter = 1
            }
        };

        graph.Nodes.Add(counterNode);
        graph.Nodes.Add(act1);
        graph.Nodes.Add(act2);

        graph.Edges.Add(new LogicEdge("cnt_1", "act_1", "Q", "In"));
        graph.Edges.Add(new LogicEdge("cnt_1", "act_2", "Q", "In"));

        var candidateRules = new List<CandidateRule>();
        var diagnostics = new List<CompileDiagnostic>();
        var nodeMap = graph.Nodes.ToDictionary(n => n.Id);

        // Act
        CounterMacroLowering.Lower(graph, _product, nodeMap, new[] { counterNode }, candidateRules, diagnostics);

        // Assert
        Assert.Empty(diagnostics);
        // 1 COUNT pulse rule + 2 TRIP rules + 2 CLEAR rules = 5 rules
        Assert.Equal(5, candidateRules.Count);
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == do0.TagIndex && r.RuleName.Contains("CTU_TRIP"));
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == do0.TagIndex && r.RuleName.Contains("CTU_CLEAR"));
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == vreg0.TagIndex && r.RuleName.Contains("CTU_TRIP"));
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == vreg0.TagIndex && r.RuleName.Contains("CTU_CLEAR"));
    }

    [Fact]
    public void TimerMacroLowering_TonWithMultipleDownstreamActions_GeneratesRulesForAllActions()
    {
        // Arrange
        var di0 = _product.FindTagByName("DI0")!;
        var do0 = _product.FindTagByName("DO0")!;
        var do1 = _product.FindTagByName("DO1")!;
        var vreg0 = _product.FindTagByName("VREG0")!;

        var graph = new LogicGraph();
        var timerNode = new LogicNode
        {
            Id = "timer_ton",
            Label = "TON FanOut",
            Kind = LogicNodeKind.Timer,
            TimerData = new TimerNodeData
            {
                Type = TimerMacroType.Ton,
                InTagIndex = di0.TagIndex,
                QTagIndex = do0.TagIndex,
                PresetMs = 2500
            }
        };

        var act1 = new LogicNode { Id = "act_1", Kind = LogicNodeKind.Action, ActionData = new ActionNodeData { TargetTagIndex = do0.TagIndex, Type = ActionKind.SetTag, Parameter = 1 } };
        var act2 = new LogicNode { Id = "act_2", Kind = LogicNodeKind.Action, ActionData = new ActionNodeData { TargetTagIndex = do1.TagIndex, Type = ActionKind.SetTag, Parameter = 1 } };
        var act3 = new LogicNode { Id = "act_3", Kind = LogicNodeKind.Action, ActionData = new ActionNodeData { TargetTagIndex = vreg0.TagIndex, Type = ActionKind.SetTag, Parameter = 1 } };

        graph.Nodes.Add(timerNode);
        graph.Nodes.Add(act1);
        graph.Nodes.Add(act2);
        graph.Nodes.Add(act3);

        graph.Edges.Add(new LogicEdge("timer_ton", "act_1", "Q", "In"));
        graph.Edges.Add(new LogicEdge("timer_ton", "act_2", "Q", "In"));
        graph.Edges.Add(new LogicEdge("timer_ton", "act_3", "Q", "In"));

        var candidateRules = new List<CandidateRule>();
        var diagnostics = new List<CompileDiagnostic>();
        var nodeMap = graph.Nodes.ToDictionary(n => n.Id);

        // Act
        TimerMacroLowering.Lower(graph, _product, nodeMap, new[] { timerNode }, candidateRules, diagnostics);

        // Assert
        Assert.Empty(diagnostics);
        // 3 actions * 2 rules (TON_ON and TON_OFF) = 6 rules
        Assert.Equal(6, candidateRules.Count);
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == do0.TagIndex && r.RuleName.Contains("TON_ON") && r.Origin != null && r.Origin.ExpansionIndex == 0);
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == do0.TagIndex && r.RuleName.Contains("TON_OFF") && r.Origin != null && r.Origin.ExpansionIndex == 1);
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == do1.TagIndex && r.RuleName.Contains("TON_ON") && r.Origin != null && r.Origin.ExpansionIndex == 2);
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == do1.TagIndex && r.RuleName.Contains("TON_OFF") && r.Origin != null && r.Origin.ExpansionIndex == 3);
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == vreg0.TagIndex && r.RuleName.Contains("TON_ON") && r.Origin != null && r.Origin.ExpansionIndex == 4);
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == vreg0.TagIndex && r.RuleName.Contains("TON_OFF") && r.Origin != null && r.Origin.ExpansionIndex == 5);
    }

    [Fact]
    public void TimerMacroLowering_TofWithMultipleDownstreamActions_GeneratesRulesForAllActions()
    {
        // Arrange
        var di0 = _product.FindTagByName("DI0")!;
        var do0 = _product.FindTagByName("DO0")!;
        var do1 = _product.FindTagByName("DO1")!;

        var graph = new LogicGraph();
        var timerNode = new LogicNode
        {
            Id = "timer_tof",
            Label = "TOF FanOut",
            Kind = LogicNodeKind.Timer,
            TimerData = new TimerNodeData
            {
                Type = TimerMacroType.Tof,
                InTagIndex = di0.TagIndex,
                QTagIndex = do0.TagIndex,
                PresetMs = 4000
            }
        };

        var act1 = new LogicNode { Id = "act_1", Kind = LogicNodeKind.Action, ActionData = new ActionNodeData { TargetTagIndex = do0.TagIndex, Type = ActionKind.SetTag, Parameter = 1 } };
        var act2 = new LogicNode { Id = "act_2", Kind = LogicNodeKind.Action, ActionData = new ActionNodeData { TargetTagIndex = do1.TagIndex, Type = ActionKind.SetTag, Parameter = 1 } };

        graph.Nodes.Add(timerNode);
        graph.Nodes.Add(act1);
        graph.Nodes.Add(act2);

        graph.Edges.Add(new LogicEdge("timer_tof", "act_1", "Q", "In"));
        graph.Edges.Add(new LogicEdge("timer_tof", "act_2", "Q", "In"));

        var candidateRules = new List<CandidateRule>();
        var diagnostics = new List<CompileDiagnostic>();
        var nodeMap = graph.Nodes.ToDictionary(n => n.Id);

        // Act
        TimerMacroLowering.Lower(graph, _product, nodeMap, new[] { timerNode }, candidateRules, diagnostics);

        // Assert
        Assert.Empty(diagnostics);
        Assert.Equal(4, candidateRules.Count);
        // Both TOF_OFF rules must have guard checking the primaryQTag (DO0)
        var tofOff1 = candidateRules.First(r => r.Action.TargetTag.TagIndex == do0.TagIndex && r.RuleName.Contains("TOF_OFF"));
        var tofOff2 = candidateRules.First(r => r.Action.TargetTag.TagIndex == do1.TagIndex && r.RuleName.Contains("TOF_OFF"));
        Assert.NotNull(tofOff1.Guard.Tag);
        Assert.NotNull(tofOff2.Guard.Tag);
        Assert.Equal(do0.TagIndex, tofOff1.Guard.Tag.TagIndex);
        Assert.Equal(do0.TagIndex, tofOff2.Guard.Tag.TagIndex);
        Assert.False(tofOff1.Guard.Negated);
        Assert.False(tofOff2.Guard.Negated);
    }

    [Fact]
    public void TimerMacroLowering_CycleWithMultipleDownstreamActions_GeneratesRulesForAllActions()
    {
        // Arrange
        var di0 = _product.FindTagByName("DI0")!;
        var do0 = _product.FindTagByName("DO0")!;
        var do1 = _product.FindTagByName("DO1")!;

        var graph = new LogicGraph();
        var timerNode = new LogicNode
        {
            Id = "timer_cycle",
            Label = "Cycle FanOut",
            Kind = LogicNodeKind.Timer,
            TimerData = new TimerNodeData
            {
                Type = TimerMacroType.Cycle,
                InTagIndex = di0.TagIndex,
                QTagIndex = do0.TagIndex,
                OnTimeMs = 1500,
                OffTimeMs = 1000,
                StartWithOn = true
            }
        };

        var act1 = new LogicNode { Id = "act_1", Kind = LogicNodeKind.Action, ActionData = new ActionNodeData { TargetTagIndex = do0.TagIndex, Type = ActionKind.SetTag, Parameter = 1 } };
        var act2 = new LogicNode { Id = "act_2", Kind = LogicNodeKind.Action, ActionData = new ActionNodeData { TargetTagIndex = do1.TagIndex, Type = ActionKind.SetTag, Parameter = 1 } };

        graph.Nodes.Add(timerNode);
        graph.Nodes.Add(act1);
        graph.Nodes.Add(act2);

        graph.Edges.Add(new LogicEdge("timer_cycle", "act_1", "Q", "In"));
        graph.Edges.Add(new LogicEdge("timer_cycle", "act_2", "Q", "In"));

        var candidateRules = new List<CandidateRule>();
        var diagnostics = new List<CompileDiagnostic>();
        var nodeMap = graph.Nodes.ToDictionary(n => n.Id);

        // Act
        TimerMacroLowering.Lower(graph, _product, nodeMap, new[] { timerNode }, candidateRules, diagnostics);

        // Assert
        Assert.Empty(diagnostics);
        // 2 actions * 4 cycle stages = 8 rules
        Assert.Equal(8, candidateRules.Count);
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == do0.TagIndex && r.RuleName.Contains("CYCLE_START"));
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == do0.TagIndex && r.RuleName.Contains("CYCLE_ON_EXPIRE"));
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == do0.TagIndex && r.RuleName.Contains("CYCLE_OFF_EXPIRE"));
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == do0.TagIndex && r.RuleName.Contains("CYCLE_STOP"));
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == do1.TagIndex && r.RuleName.Contains("CYCLE_START"));
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == do1.TagIndex && r.RuleName.Contains("CYCLE_ON_EXPIRE"));
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == do1.TagIndex && r.RuleName.Contains("CYCLE_OFF_EXPIRE"));
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == do1.TagIndex && r.RuleName.Contains("CYCLE_STOP"));
    }

    [Fact]
    public void TimerMacroLowering_WithCustomTrigComparator_GeneratesInvertedOpForAllDownstreamActions()
    {
        // Arrange
        var ai0 = _product.FindTagByName("AI0") ?? _product.FindTagByIndex(16)!;
        var do0 = _product.FindTagByName("DO0")!;
        var do1 = _product.FindTagByName("DO1")!;

        var graph = new LogicGraph();
        var trigNode = new LogicNode
        {
            Id = "trig_cmp",
            Kind = LogicNodeKind.Trigger,
            TriggerData = new TriggerNodeData
            {
                Type = TriggerKind.OnChange,
                CompareOp = CompareOperator.GreaterThan,
                ThresholdLo = 50.0f
            }
        };
        var inpNode = new LogicNode
        {
            Id = "inp_ai0",
            Kind = LogicNodeKind.Input,
            InputData = new InputNodeData { TagIndex = ai0.TagIndex }
        };
        var timerNode = new LogicNode
        {
            Id = "timer_ton_analog",
            Label = "Analog TON",
            Kind = LogicNodeKind.Timer,
            TimerData = new TimerNodeData
            {
                Type = TimerMacroType.Ton,
                InTagIndex = ai0.TagIndex,
                QTagIndex = do0.TagIndex,
                PresetMs = 3000
            }
        };

        var act1 = new LogicNode { Id = "act_1", Kind = LogicNodeKind.Action, ActionData = new ActionNodeData { TargetTagIndex = do0.TagIndex, Type = ActionKind.SetTag, Parameter = 1 } };
        var act2 = new LogicNode { Id = "act_2", Kind = LogicNodeKind.Action, ActionData = new ActionNodeData { TargetTagIndex = do1.TagIndex, Type = ActionKind.SetTag, Parameter = 1 } };

        graph.Nodes.Add(inpNode);
        graph.Nodes.Add(trigNode);
        graph.Nodes.Add(timerNode);
        graph.Nodes.Add(act1);
        graph.Nodes.Add(act2);

        graph.Edges.Add(new LogicEdge("inp_ai0", "trig_cmp", "Out", "In"));
        graph.Edges.Add(new LogicEdge("trig_cmp", "timer_ton_analog", "Out", "In"));
        graph.Edges.Add(new LogicEdge("timer_ton_analog", "act_1", "Q", "In"));
        graph.Edges.Add(new LogicEdge("timer_ton_analog", "act_2", "Q", "In"));

        var candidateRules = new List<CandidateRule>();
        var diagnostics = new List<CompileDiagnostic>();
        var nodeMap = graph.Nodes.ToDictionary(n => n.Id);

        // Act
        TimerMacroLowering.Lower(graph, _product, nodeMap, new[] { timerNode }, candidateRules, diagnostics);

        // Assert
        Assert.Empty(diagnostics);
        Assert.Equal(4, candidateRules.Count);
        // TON_ON rules must test GreaterThan with ForMs=3000
        var on1 = candidateRules.First(r => r.Action.TargetTag.TagIndex == do0.TagIndex && r.RuleName.Contains("TON_ON"));
        var on2 = candidateRules.First(r => r.Action.TargetTag.TagIndex == do1.TagIndex && r.RuleName.Contains("TON_ON"));
        Assert.Equal(CompareOperator.GreaterThan, on1.Trigger.CompareOp);
        Assert.Equal(3000u, on1.Trigger.ForMs);
        Assert.Equal(CompareOperator.GreaterThan, on2.Trigger.CompareOp);

        // TON_OFF rules must invert to LessThanOrEqual with ForMs=0
        var off1 = candidateRules.First(r => r.Action.TargetTag.TagIndex == do0.TagIndex && r.RuleName.Contains("TON_OFF"));
        var off2 = candidateRules.First(r => r.Action.TargetTag.TagIndex == do1.TagIndex && r.RuleName.Contains("TON_OFF"));
        Assert.Equal(CompareOperator.LessThanOrEqual, off1.Trigger.CompareOp);
        Assert.Equal(0u, off1.Trigger.ForMs);
        Assert.Equal(CompareOperator.LessThanOrEqual, off2.Trigger.CompareOp);
    }

    [Fact]
    public void CounterMacroLowering_CtdWithMultipleDownstreamActions_GeneratesTripAndClearForAllActions()
    {
        // Arrange
        var di0 = _product.FindTagByName("DI0")!;
        var do0 = _product.FindTagByName("DO0")!;
        var vreg0 = _product.FindTagByName("VREG0")!;
        var counterTag = _product.FindTagByName("COUNTER0") ?? _product.FindTagByIndex(116)!;

        var graph = new LogicGraph();
        var counterNode = new LogicNode
        {
            Id = "cnt_ctd",
            Kind = LogicNodeKind.Counter,
            CounterData = new CounterNodeData
            {
                Type = CounterMacroType.Ctd,
                CuTagIndex = di0.TagIndex,
                CvTagIndex = counterTag.TagIndex,
                QTagIndex = do0.TagIndex,
                PresetValue = 5
            }
        };
        var act1 = new LogicNode { Id = "act_1", Kind = LogicNodeKind.Action, ActionData = new ActionNodeData { TargetTagIndex = do0.TagIndex, Type = ActionKind.SetTag, Parameter = 1 } };
        var act2 = new LogicNode { Id = "act_2", Kind = LogicNodeKind.Action, ActionData = new ActionNodeData { TargetTagIndex = vreg0.TagIndex, Type = ActionKind.SetTag, Parameter = 1 } };

        graph.Nodes.Add(counterNode);
        graph.Nodes.Add(act1);
        graph.Nodes.Add(act2);

        graph.Edges.Add(new LogicEdge("cnt_ctd", "act_1", "Q", "In"));
        graph.Edges.Add(new LogicEdge("cnt_ctd", "act_2", "Q", "In"));

        var candidateRules = new List<CandidateRule>();
        var diagnostics = new List<CompileDiagnostic>();
        var nodeMap = graph.Nodes.ToDictionary(n => n.Id);

        // Act
        CounterMacroLowering.Lower(graph, _product, nodeMap, new[] { counterNode }, candidateRules, diagnostics);

        // Assert
        Assert.Empty(diagnostics);
        Assert.Equal(5, candidateRules.Count);
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == do0.TagIndex && r.RuleName.Contains("CTD_TRIP"));
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == do0.TagIndex && r.RuleName.Contains("CTD_CLEAR"));
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == vreg0.TagIndex && r.RuleName.Contains("CTD_TRIP"));
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == vreg0.TagIndex && r.RuleName.Contains("CTD_CLEAR"));
    }

    [Fact]
    public void CounterMacroLowering_WithResetTagAndMultipleActions_GeneratesResetAndPerActionTripClear()
    {
        // Arrange
        var di0 = _product.FindTagByName("DI0")!;
        var di1 = _product.FindTagByName("DI1")!;
        var do0 = _product.FindTagByName("DO0")!;
        var do1 = _product.FindTagByName("DO1")!;
        var counterTag = _product.FindTagByName("COUNTER0") ?? _product.FindTagByIndex(116)!;

        var graph = new LogicGraph();
        var counterNode = new LogicNode
        {
            Id = "cnt_ctu_reset",
            Kind = LogicNodeKind.Counter,
            CounterData = new CounterNodeData
            {
                Type = CounterMacroType.Ctu,
                CuTagIndex = di0.TagIndex,
                ResetTagIndex = di1.TagIndex,
                CvTagIndex = counterTag.TagIndex,
                QTagIndex = do0.TagIndex,
                PresetValue = 100
            }
        };
        var act1 = new LogicNode { Id = "act_1", Kind = LogicNodeKind.Action, ActionData = new ActionNodeData { TargetTagIndex = do0.TagIndex, Type = ActionKind.SetTag, Parameter = 1 } };
        var act2 = new LogicNode { Id = "act_2", Kind = LogicNodeKind.Action, ActionData = new ActionNodeData { TargetTagIndex = do1.TagIndex, Type = ActionKind.SetTag, Parameter = 1 } };

        graph.Nodes.Add(counterNode);
        graph.Nodes.Add(act1);
        graph.Nodes.Add(act2);

        graph.Edges.Add(new LogicEdge("cnt_ctu_reset", "act_1", "Q", "In"));
        graph.Edges.Add(new LogicEdge("cnt_ctu_reset", "act_2", "Q", "In"));

        var candidateRules = new List<CandidateRule>();
        var diagnostics = new List<CompileDiagnostic>();
        var nodeMap = graph.Nodes.ToDictionary(n => n.Id);

        // Act
        CounterMacroLowering.Lower(graph, _product, nodeMap, new[] { counterNode }, candidateRules, diagnostics);

        // Assert
        Assert.Empty(diagnostics);
        // 1 COUNT pulse rule + 1 RESET rule + 2 TRIP rules + 2 CLEAR rules = 6 rules
        Assert.Equal(6, candidateRules.Count);
        Assert.Contains(candidateRules, r => r.RuleName.Contains("CTU_RESET"));
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == do0.TagIndex && r.RuleName.Contains("CTU_TRIP"));
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == do0.TagIndex && r.RuleName.Contains("CTU_CLEAR"));
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == do1.TagIndex && r.RuleName.Contains("CTU_TRIP"));
        Assert.Contains(candidateRules, r => r.Action.TargetTag.TagIndex == do1.TagIndex && r.RuleName.Contains("CTU_CLEAR"));
    }

    [Fact]
    public void CounterMacroLowering_CtuMacro_ExpandsToPulseResetTripClearRules()
    {
        // Arrange
        var di0 = _product.FindTagByName("DI0")!;
        var di1 = _product.FindTagByName("DI1")!;
        var do0 = _product.FindTagByName("DO0")!;
        var vreg0 = _product.FindTagByName("VREG0")!;

        var graph = new LogicGraph();
        var counterNode = new LogicNode
        {
            Id = "counter_1",
            Kind = LogicNodeKind.Counter,
            CounterData = new CounterNodeData
            {
                Type = CounterMacroType.Ctu,
                CuTagIndex = di0.TagIndex,
                CvTagIndex = vreg0.TagIndex,
                ResetTagIndex = di1.TagIndex,
                QTagIndex = do0.TagIndex,
                PresetValue = 10
            }
        };
        graph.Nodes.Add(counterNode);

        var candidateRules = new List<CandidateRule>();
        var diagnostics = new List<CompileDiagnostic>();
        var nodeMap = graph.Nodes.ToDictionary(n => n.Id);

        // Act
        CounterMacroLowering.Lower(graph, _product, nodeMap, new[] { counterNode }, candidateRules, diagnostics);

        // Assert
        Assert.Empty(diagnostics);
        Assert.Equal(4, candidateRules.Count); // COUNT, RESET, TRIP, CLEAR
        Assert.Contains(candidateRules, r => r.RuleName.EndsWith("_CTU_COUNT") && r.Action.Type == ActionKind.IncrementCounter);
        Assert.Contains(candidateRules, r => r.RuleName.EndsWith("_CTU_RESET") && r.Action.Type == ActionKind.SetTag && r.Action.Parameter == 0);
        Assert.Contains(candidateRules, r => r.RuleName.EndsWith("_CTU_TRIP") && r.Trigger.CompareOp == CompareOperator.GreaterThanOrEqual);
        Assert.Contains(candidateRules, r => r.RuleName.EndsWith("_CTU_CLEAR") && r.Trigger.CompareOp == CompareOperator.LessThan);
    }

    [Fact]
    public void RuleCompiler_FullPipeline_CompilesModularizedGraphDeterministically()
    {
        // Arrange
        var compiler = new RuleCompiler();
        var graph = new LogicGraph();
        var di0 = _product.FindTagByName("DI0")!;
        var do0 = _product.FindTagByName("DO0")!;

        graph.Nodes.Add(LogicNode.CreateInput("in_1", di0.TagIndex));
        graph.Nodes.Add(LogicNode.CreateAction("act_1", do0.TagIndex, ActionKind.SetTag, 1));
        graph.Edges.Add(new LogicEdge("in_1", "act_1"));

        // Act
        var result = compiler.Compile(graph, _product);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Program);
        Assert.Single(result.Program.Rules);
        Assert.Equal(0, result.Program.Rules[0].RuleIndex);
        Assert.Equal(di0.TagIndex, result.Program.Rules[0].Trigger.Tag?.TagIndex);
        Assert.Equal(do0.TagIndex, result.Program.Rules[0].Action.TargetTag?.TagIndex);
    }

    [Fact]
    public void CompilationHelpers_AllUtilityMethods_ExecuteCorrectly()
    {
        // CompareOp Invert
        Assert.Equal(CompareOperator.LessThanOrEqual, CompilationHelpers.InvertCompareOp(CompareOperator.GreaterThan));
        Assert.Equal(CompareOperator.GreaterThanOrEqual, CompilationHelpers.InvertCompareOp(CompareOperator.LessThan));
        Assert.Equal(CompareOperator.LessThan, CompilationHelpers.InvertCompareOp(CompareOperator.GreaterThanOrEqual));
        Assert.Equal(CompareOperator.GreaterThan, CompilationHelpers.InvertCompareOp(CompareOperator.LessThanOrEqual));
        Assert.Equal(CompareOperator.NotEqual, CompilationHelpers.InvertCompareOp(CompareOperator.Equal));
        Assert.Equal(CompareOperator.Equal, CompilationHelpers.InvertCompareOp(CompareOperator.NotEqual));

        // IsFloatThreshold
        var intTag = _product.FindTagByName("DI0")!;
        Assert.False(CompilationHelpers.IsFloatThreshold(10.0, 20.0, intTag));
        Assert.True(CompilationHelpers.IsFloatThreshold(10.5, 20.0, intTag));

        // GetNodeLabel
        var node = new LogicNode { Id = "test_node", Label = "CustomLabel" };
        Assert.Equal("CustomLabel", CompilationHelpers.GetNodeLabel(node));
        Assert.Equal("Fallback", CompilationHelpers.GetNodeLabel(new LogicNode { Id = "unnamed" }, "Fallback"));

        // ResolveSourcePortTagIndex
        var inNode = LogicNode.CreateInput("in_test", 5);
        Assert.Equal((ushort)5, CompilationHelpers.ResolveSourcePortTagIndex(inNode, "Val"));
    }

    [Fact]
    public void ActionPathTracer_DisconnectedAction_ProducesDiagnosticError()
    {
        // Arrange
        var graph = new LogicGraph();
        var actNode = LogicNode.CreateAction("act_orphan", 8, ActionKind.SetTag, 1);
        graph.Nodes.Add(actNode);

        var candidateRules = new List<CandidateRule>();
        var diagnostics = new List<CompileDiagnostic>();
        var nodeMap = graph.Nodes.ToDictionary(n => n.Id);

        // Act
        ActionPathTracer.Trace(graph, _product, nodeMap, new[] { actNode }, candidateRules, diagnostics);

        // Assert
        Assert.Single(diagnostics);
        Assert.Equal(RuleCompiler.ErrActionNoIncoming, diagnostics[0].Code);
        Assert.Empty(candidateRules);
    }

    [Fact]
    public void PeripheralConfigExtractor_ModbusMasterOnSlavePort_ProducesDiagnosticError()
    {
        // Arrange
        var graph = new LogicGraph();
        var mbReadNode = new LogicNode
        {
            Id = "mb_read_1",
            Kind = LogicNodeKind.ModbusRead,
            ModbusReadData = new ModbusReadNodeData { PortIndex = 0 }
        };
        graph.Nodes.Add(mbReadNode);
        var nodeMap = graph.Nodes.ToDictionary(n => n.Id);

        var slavePorts = new[]
        {
            new SimplePLC.Protocol.Dto.ModbusPortConfigDto
            {
                PortIndex = 0,
                Role = SimplePLC.Protocol.Enums.ModbusPortRole.Slave
            }
        };

        // Act
        var result = PeripheralConfigExtractor.Extract(graph, _product, nodeMap, slavePorts);

        // Assert
        Assert.Single(result.Diagnostics);
        Assert.Equal(RuleCompiler.ErrModbusPortSlaveConflict, result.Diagnostics[0].Code);
    }
}
