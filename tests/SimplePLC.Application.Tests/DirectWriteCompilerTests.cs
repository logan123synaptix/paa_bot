namespace SimplePLC.Application.Tests;

using System.Linq;
using SimplePLC.Application.Logic.Compilation;
using SimplePLC.Application.Logic.Graph;
using SimplePLC.Domain.Enums;
using SimplePLC.Domain.Models;
using SimplePLC.Protocol.Enums;
using Xunit;

public class DirectWriteCompilerTests
{
    private readonly ProductDefinition _product = ProductDefinition.CreateRemoteIo8Di8Do4Ai();
    private readonly RuleCompiler _compiler = new();

    [Fact]
    public void DirectWriteNode_CompilesSuccessfully_WithTargetPinAndChannelMode()
    {
        var di0 = _product.FindTagByName("DI0")!;
        var do4 = _product.FindTagByName("DO4")!;
        var vreg0 = _product.FindTagByName("VREG0")!;

        var graph = new LogicGraph();
        graph.Nodes.Add(LogicNode.CreateInput("IN_TRIG", di0.TagIndex));
        graph.Nodes.Add(LogicNode.CreateInput("IN_CHAN", vreg0.TagIndex));
        graph.Nodes.Add(LogicNode.CreateDirectWrite("DW0",
            trigTag: di0.TagIndex,
            targetPin: DirectWriteTargetPin.Do3,
            mode: DirectWriteMode.AddressableBus,
            channelCount: 18,
            isExclusiveChannel: true,
            channelTag: vreg0.TagIndex,
            defaultColorRgb: 0x00FF00));
        graph.Nodes.Add(LogicNode.CreateAction("ACT_CHIME", do4.TagIndex, ActionKind.SetTag, 1));

        graph.Edges.Add(new LogicEdge("IN_TRIG", "DW0", "Out", "Trig"));
        graph.Edges.Add(new LogicEdge("IN_CHAN", "DW0", "Out", "Chan"));
        graph.Edges.Add(new LogicEdge("DW0", "ACT_CHIME", "Done", "In"));

        var result = _compiler.Compile(graph, _product);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Program);
        Assert.Single(result.Program.DirectWriteConfigs);

        var config = result.Program.DirectWriteConfigs[0];
        Assert.Equal("DW0", config.Id);
        Assert.Equal(DirectWriteTargetPin.Do3, config.TargetPin);
        Assert.Equal(DirectWriteMode.AddressableBus, config.Mode);
        Assert.Equal((ushort)18, config.ChannelCount);
        Assert.True(config.IsExclusiveChannel);
        Assert.Equal(0x00FF00u, config.DefaultColorRgb);
        Assert.Equal(vreg0.TagIndex, config.ChannelTagIndex);
    }

    [Fact]
    public void DirectWriteNode_CompilesPtoMode_ExtractsLifecycleTags()
    {
        var di0 = _product.FindTagByName("DI0")!;
        var do0 = _product.FindTagByName("DO0")!;
        var vreg0 = _product.FindTagByName("VREG0")!;
        var vreg1 = _product.FindTagByName("VREG1")!;
        var vreg2 = _product.FindTagByName("VREG2")!;
        var vreg3 = _product.FindTagByName("VREG3")!;

        var graph = new LogicGraph();
        graph.Nodes.Add(LogicNode.CreateDirectWrite(
            "DW_PTO",
            trigTag: di0.TagIndex,
            targetPin: DirectWriteTargetPin.Do0,
            mode: DirectWriteMode.PulseTrain,
            channelCount: 500,
            isExclusiveChannel: true,
            pulseWidthUs: 50,
            pwmFrequencyHz: 2500,
            triggerMode: DirectWriteTriggerMode.OnTriggerRisingEdge,
            channelTag: vreg0.TagIndex,
            paramTag: vreg1.TagIndex,
            doneTag: vreg2.TagIndex,
            errorTag: vreg3.TagIndex));

        var result = _compiler.Compile(graph, _product);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Program);
        Assert.Single(result.Program.DirectWriteConfigs);

        var config = result.Program.DirectWriteConfigs[0];
        Assert.Equal("DW_PTO", config.Id);
        Assert.Equal(DirectWriteTargetPin.Do0, config.TargetPin);
        Assert.Equal(DirectWriteMode.PulseTrain, config.Mode);
        Assert.Equal((ushort)500, config.ChannelCount);
        Assert.Equal(2500u, config.PwmFrequencyHz);
        Assert.Equal(di0.TagIndex, config.TrigTagIndex);
        Assert.Equal(vreg0.TagIndex, config.ChannelTagIndex);
        Assert.Equal(vreg1.TagIndex, config.ParamTagIndex);
        Assert.Equal(vreg2.TagIndex, config.DoneTagIndex);
        Assert.Equal(vreg3.TagIndex, config.ErrorTagIndex);
    }
}
