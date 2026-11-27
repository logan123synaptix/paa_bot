using System.Collections.Generic;
using System.Linq;
using SimplePLC.Application.Logic.Compilation;
using SimplePLC.Application.Logic.Graph;
using SimplePLC.Domain.Enums;
using SimplePLC.Domain.Models;
using Xunit;

namespace SimplePLC.Application.Tests;

/// <summary>
/// Unit tests chuyên biệt kiểm chứng tính toàn vẹn của WriteConflictValidator đối với các nhóm quy tắc
/// Controlled Multi-Writer Group trong kịch bản Fan-out ngõ ra của Timer và Counter Macros.
/// </summary>
public class WriteConflictValidatorFanOutTests
{
    private readonly ProductDefinition _product = ProductDefinition.CreateRemoteIo8Di8Do4Ai();

    [Fact]
    public void Validate_TonMacroFanOutToMultipleTags_AllowsControlledMultiWriterGroup()
    {
        // Arrange: TON macro fan-out tới 2 tags: DO0 và DO1 (mỗi tag có 2 rules: ON!=0 và OFF==0)
        var do0 = _product.FindTagByName("DO0")!;
        var do1 = _product.FindTagByName("DO1")!;
        var di0 = _product.FindTagByName("DI0")!;
        var dummyNode = new LogicNode { Id = "timer_ton", Kind = LogicNodeKind.Timer };

        var r0_on = RuleAccessAnalyzer.Analyze(dummyNode,
            new TriggerModel(di0, TriggerKind.OnRise) { ForMs = 2000 },
            new ActionModel(do0, ActionKind.SetTag, 1),
            GuardModel.Empty, "TON_ON_DO0",
            new GeneratedRuleOrigin("timer_ton", "timer_ton", "TON", 0, "Timer -> DO0"));

        var r0_off = RuleAccessAnalyzer.Analyze(dummyNode,
            new TriggerModel(di0, TriggerKind.OnFall) { ForMs = 0 },
            new ActionModel(do0, ActionKind.SetTag, 0),
            GuardModel.Empty, "TON_OFF_DO0",
            new GeneratedRuleOrigin("timer_ton", "timer_ton", "TON", 1, "Timer -> DO0"));

        var r1_on = RuleAccessAnalyzer.Analyze(dummyNode,
            new TriggerModel(di0, TriggerKind.OnRise) { ForMs = 2000 },
            new ActionModel(do1, ActionKind.SetTag, 1),
            GuardModel.Empty, "TON_ON_DO1",
            new GeneratedRuleOrigin("timer_ton", "timer_ton", "TON", 2, "Timer -> DO1"));

        var r1_off = RuleAccessAnalyzer.Analyze(dummyNode,
            new TriggerModel(di0, TriggerKind.OnFall) { ForMs = 0 },
            new ActionModel(do1, ActionKind.SetTag, 0),
            GuardModel.Empty, "TON_OFF_DO1",
            new GeneratedRuleOrigin("timer_ton", "timer_ton", "TON", 3, "Timer -> DO1"));

        // Act
        var diagnostics = WriteConflictValidator.Validate(new[] { r0_on, r0_off, r1_on, r1_off }, _product);

        // Assert: Cả 2 nhóm ghi DO0 và DO1 đều là Controlled Multi-Writer hợp lệ
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Validate_CycleMacroFanOutToMultipleTags_AllowsControlledMultiWriterGroup()
    {
        // Arrange: CYCLE macro fan-out tới 2 tags: DO0 và DO1 (mỗi tag 4 rules)
        var do0 = _product.FindTagByName("DO0")!;
        var do1 = _product.FindTagByName("DO1")!;
        var di0 = _product.FindTagByName("DI0")!;
        var dummyNode = new LogicNode { Id = "timer_cycle", Kind = LogicNodeKind.Timer };

        var rules = new List<RuleAccessInfo>();
        foreach (var (tag, baseExp) in new[] { (do0, 0), (do1, 4) })
        {
            rules.Add(RuleAccessAnalyzer.Analyze(dummyNode, new TriggerModel(di0, TriggerKind.OnRise), new ActionModel(tag, ActionKind.SetTag, 1), GuardModel.Empty, "CYCLE_START", new GeneratedRuleOrigin("timer_cycle", "timer_cycle", "CYCLE", baseExp)));
            rules.Add(RuleAccessAnalyzer.Analyze(dummyNode, new TriggerModel(do0, TriggerKind.OnRise) { ForMs = 1000 }, new ActionModel(tag, ActionKind.SetTag, 0), GuardModel.Empty, "CYCLE_ON_EXP", new GeneratedRuleOrigin("timer_cycle", "timer_cycle", "CYCLE", baseExp + 1)));
            rules.Add(RuleAccessAnalyzer.Analyze(dummyNode, new TriggerModel(do0, TriggerKind.OnFall) { ForMs = 1000 }, new ActionModel(tag, ActionKind.SetTag, 1), GuardModel.Empty, "CYCLE_OFF_EXP", new GeneratedRuleOrigin("timer_cycle", "timer_cycle", "CYCLE", baseExp + 2)));
            rules.Add(RuleAccessAnalyzer.Analyze(dummyNode, new TriggerModel(di0, TriggerKind.OnFall), new ActionModel(tag, ActionKind.SetTag, 0), GuardModel.Empty, "CYCLE_STOP", new GeneratedRuleOrigin("timer_cycle", "timer_cycle", "CYCLE", baseExp + 3)));
        }

        // Act
        var diagnostics = WriteConflictValidator.Validate(rules, _product);

        // Assert: Hợp lệ 100%
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Validate_CounterMacroFanOutToMultipleTags_AllowsControlledMultiWriterGroup()
    {
        // Arrange: CTU macro fan-out tới DO0 và DO1 (mỗi tag 2 rules: TRIP=1, CLEAR=0)
        var do0 = _product.FindTagByName("DO0")!;
        var do1 = _product.FindTagByName("DO1")!;
        var cvTag = _product.FindTagByIndex(116)!;
        var di0 = _product.FindTagByName("DI0")!;
        var dummyNode = new LogicNode { Id = "cnt_1", Kind = LogicNodeKind.Counter };

        var rules = new List<RuleAccessInfo>
        {
            // CV increment rule
            RuleAccessAnalyzer.Analyze(dummyNode, new TriggerModel(di0, TriggerKind.OnRise), new ActionModel(cvTag, ActionKind.IncrementCounter, 1), GuardModel.Empty, "CTU_PULSE", new GeneratedRuleOrigin("cnt_1", "cnt_1", "CTU", 0)),
            // DO0 Trip & Clear
            RuleAccessAnalyzer.Analyze(dummyNode, new TriggerModel(cvTag, TriggerKind.OnChange) { CompareOp = CompareOperator.GreaterThanOrEqual, ThresholdLo = 10 }, new ActionModel(do0, ActionKind.SetTag, 1), GuardModel.Empty, "CTU_TRIP_DO0", new GeneratedRuleOrigin("cnt_1", "cnt_1", "CTU", 2)),
            RuleAccessAnalyzer.Analyze(dummyNode, new TriggerModel(cvTag, TriggerKind.OnChange) { CompareOp = CompareOperator.LessThan, ThresholdLo = 10 }, new ActionModel(do0, ActionKind.SetTag, 0), GuardModel.Empty, "CTU_CLEAR_DO0", new GeneratedRuleOrigin("cnt_1", "cnt_1", "CTU", 3)),
            // DO1 Trip & Clear
            RuleAccessAnalyzer.Analyze(dummyNode, new TriggerModel(cvTag, TriggerKind.OnChange) { CompareOp = CompareOperator.GreaterThanOrEqual, ThresholdLo = 10 }, new ActionModel(do1, ActionKind.SetTag, 1), GuardModel.Empty, "CTU_TRIP_DO1", new GeneratedRuleOrigin("cnt_1", "cnt_1", "CTU", 4)),
            RuleAccessAnalyzer.Analyze(dummyNode, new TriggerModel(cvTag, TriggerKind.OnChange) { CompareOp = CompareOperator.LessThan, ThresholdLo = 10 }, new ActionModel(do1, ActionKind.SetTag, 0), GuardModel.Empty, "CTU_CLEAR_DO1", new GeneratedRuleOrigin("cnt_1", "cnt_1", "CTU", 5))
        };

        // Act
        var diagnostics = WriteConflictValidator.Validate(rules, _product);

        // Assert
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Validate_WhenExternalRuleWritesToMacroControlledTag_RejectsWithConflictingWriters()
    {
        // Arrange: TON macro quản lý DO0, nhưng có thêm 1 rule bên ngoài cũng cố tình ghi DO0
        var do0 = _product.FindTagByName("DO0")!;
        var di0 = _product.FindTagByName("DI0")!;
        var di1 = _product.FindTagByName("DI1")!;
        var dummyNode = new LogicNode { Id = "timer_ton", Kind = LogicNodeKind.Timer };
        var externalNode = new LogicNode { Id = "act_ext", Kind = LogicNodeKind.Action };

        var r0_on = RuleAccessAnalyzer.Analyze(dummyNode, new TriggerModel(di0, TriggerKind.OnRise) { ForMs = 2000 }, new ActionModel(do0, ActionKind.SetTag, 1), GuardModel.Empty, "TON_ON", new GeneratedRuleOrigin("timer_ton", "timer_ton", "TON", 0));
        var r0_off = RuleAccessAnalyzer.Analyze(dummyNode, new TriggerModel(di0, TriggerKind.OnFall) { ForMs = 0 }, new ActionModel(do0, ActionKind.SetTag, 0), GuardModel.Empty, "TON_OFF", new GeneratedRuleOrigin("timer_ton", "timer_ton", "TON", 1));
        var r_ext = RuleAccessAnalyzer.Analyze(externalNode, new TriggerModel(di1, TriggerKind.OnRise), new ActionModel(do0, ActionKind.SetTag, 1), GuardModel.Empty, "USER_OVERWRITE", null);

        // Act
        var diagnostics = WriteConflictValidator.Validate(new[] { r0_on, r0_off, r_ext }, _product);

        // Assert
        Assert.NotEmpty(diagnostics);
        Assert.Contains(diagnostics, d => d.Code == WriteConflictValidator.ErrConflictingWriters);
    }

    [Fact]
    public void Validate_WhenDifferentMacrosWriteToSameTag_RejectsWithConflictingWriters()
    {
        // Arrange: 2 timers khác nhau cùng tranh chấp ghi vào DO0
        var do0 = _product.FindTagByName("DO0")!;
        var di0 = _product.FindTagByName("DI0")!;
        var timer1 = new LogicNode { Id = "timer_1", Kind = LogicNodeKind.Timer };
        var timer2 = new LogicNode { Id = "timer_2", Kind = LogicNodeKind.Timer };

        var r1_on = RuleAccessAnalyzer.Analyze(timer1, new TriggerModel(di0, TriggerKind.OnRise), new ActionModel(do0, ActionKind.SetTag, 1), GuardModel.Empty, "T1_ON", new GeneratedRuleOrigin("timer_1", "timer_1", "TON", 0));
        var r1_off = RuleAccessAnalyzer.Analyze(timer1, new TriggerModel(di0, TriggerKind.OnFall), new ActionModel(do0, ActionKind.SetTag, 0), GuardModel.Empty, "T1_OFF", new GeneratedRuleOrigin("timer_1", "timer_1", "TON", 1));
        var r2_on = RuleAccessAnalyzer.Analyze(timer2, new TriggerModel(di0, TriggerKind.OnRise), new ActionModel(do0, ActionKind.SetTag, 1), GuardModel.Empty, "T2_ON", new GeneratedRuleOrigin("timer_2", "timer_2", "TON", 0));
        var r2_off = RuleAccessAnalyzer.Analyze(timer2, new TriggerModel(di0, TriggerKind.OnFall), new ActionModel(do0, ActionKind.SetTag, 0), GuardModel.Empty, "T2_OFF", new GeneratedRuleOrigin("timer_2", "timer_2", "TON", 1));

        // Act
        var diagnostics = WriteConflictValidator.Validate(new[] { r1_on, r1_off, r2_on, r2_off }, _product);

        // Assert
        Assert.NotEmpty(diagnostics);
        Assert.Contains(diagnostics, d => d.Code == WriteConflictValidator.ErrConflictingWriters);
    }
}
