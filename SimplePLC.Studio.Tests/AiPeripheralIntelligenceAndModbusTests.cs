using System;
using System.Collections.Generic;
using System.Linq;
using SimplePLC.Protocol.Enums;
using SimplePLC.Studio.Models;
using SimplePLC.Studio.Services;
using SimplePLC.Studio.Services.Ai;
using SimplePLC.Studio.Services.Ai.Peripheral;
using SimplePLC.Studio.ViewModels;
using Xunit;

namespace SimplePLC.Studio.Tests;

/// <summary>
/// Bộ kiểm thử hợp đồng và TDD cho hệ thống Trí tuệ Thiết bị Ngoại vi (Peripheral Intelligence)
/// và cơ chế sinh khối Modbus đa chân (Multi-Pin MB_READ) cho SynaptiX AI Chatbox.
/// </summary>
public class AiPeripheralIntelligenceAndModbusTests
{
    [Theory]
    [InlineData("Tôi muốn tạo rule đọc thông tin đồng hồ MFM383A", "SELEC MFM383A")]
    [InlineData("Đọc điện áp từ đồng hồ SELEC mfm-383a", "SELEC MFM383A")]
    [InlineData("Đọc thông số từ Schneider PM2200 qua RS485", "Schneider PM2200")]
    [InlineData("Đọc cảm biến nhiệt ẩm SHT30 trạm 2", "SHT30 Modbus Sensor")]
    public void PeripheralTemplateRegistry_WhenPromptMentionsPeripheral_MatchesAndGeneratesCompactSnippet(string prompt, string expectedModel)
    {
        // Act
        bool matched = PeripheralTemplateRegistry.Instance.TryMatchPeripheral(prompt, out var template);

        // Assert
        Assert.True(matched, $"Prompt '{prompt}' should match a peripheral template.");
        Assert.NotNull(template);
        Assert.Equal(expectedModel, template.ModelName);

        string snippet = PeripheralTemplateRegistry.Instance.BuildCompactPromptSnippet(template);
        Assert.NotEmpty(snippet);
        Assert.Contains(template.ModelName, snippet);
        Assert.Contains("pins", snippet);
        // Đảm bảo dung lượng cực gọn dưới 500 ký tự (~100 tokens)
        Assert.True(snippet.Length < 500, $"Snippet length {snippet.Length} is too verbose for token economy.");
    }

    [Fact]
    public void PeripheralTemplateRegistry_WhenPromptHasNoPeripheral_ReturnsFalseAndEmptySnippet()
    {
        // Arrange
        string prompt = "Bật đèn DO0 khi nút nhấn DI0 được kích hoạt";

        // Act
        bool matched = PeripheralTemplateRegistry.Instance.TryMatchPeripheral(prompt, out var template);

        // Assert
        Assert.False(matched);
        Assert.Null(template);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PeripheralTemplateRegistry_WhenPromptNullOrWhitespace_ReturnsFalse(string? prompt)
    {
        bool matched = PeripheralTemplateRegistry.Instance.TryMatchPeripheral(prompt!, out var template);
        Assert.False(matched);
        Assert.Null(template);
    }

    [Fact]
    public void PeripheralTemplateRegistry_CompactSnippet_HasCorrectJsonSchemaAndTypes()
    {
        // Arrange
        bool matched = PeripheralTemplateRegistry.Instance.TryMatchPeripheral("đọc thông số selec mfm383a", out var template);
        Assert.True(matched);
        Assert.NotNull(template);

        // Act
        string snippet = PeripheralTemplateRegistry.Instance.BuildCompactPromptSnippet(template);

        // Assert
        using var doc = System.Text.Json.JsonDocument.Parse(snippet);
        var root = doc.RootElement;
        Assert.Equal("SELEC MFM383A", root.GetProperty("device").GetString());
        Assert.Equal("PowerMeter", root.GetProperty("cat").GetString());
        Assert.Equal(1, root.GetProperty("slave").GetInt32());
        Assert.Equal(4, root.GetProperty("fc").GetInt32());
        var pins = root.GetProperty("pins");
        Assert.Equal(System.Text.Json.JsonValueKind.Array, pins.ValueKind);
        Assert.True(pins.GetArrayLength() > 0);
        var firstPin = pins[0];
        Assert.NotNull(firstPin.GetProperty("name").GetString());
        Assert.True(firstPin.GetProperty("offset").GetInt32() >= 0);
        Assert.NotNull(firstPin.GetProperty("type").GetString());
        Assert.NotNull(firstPin.GetProperty("tag").GetString());
    }

    [Fact]
    public void AiRuleParser_WithMultiPinModbusJson_ParsesPinsCorrectly()
    {
        // Arrange
        string json = @"
```json:rules
[
  {
    ""narrative"": ""Poll SELEC MFM383A 3-phase meter"",
    ""type"": ""MB_READ"",
    ""port"": 0,
    ""slave"": 1,
    ""fc"": 4,
    ""addr"": 0,
    ""count"": 4,
    ""pins"": [
      { ""name"": ""V_Avg"", ""offset"": 0, ""type"": ""Float32"", ""endian"": ""BigEndian_ABCD"", ""target_tag"": ""VREG0"" },
      { ""name"": ""I_Avg"", ""offset"": 2, ""type"": ""Float32"", ""endian"": ""BigEndian_ABCD"", ""target_tag"": ""VREG2"" }
    ]
  }
]
```";

        // Act
        var rules = AiRuleParser.ExtractRules(json, out _);

        // Assert
        Assert.Single(rules);
        var r = rules[0];
        Assert.Equal("MB_READ", r.Type);
        Assert.Equal(1, r.ModbusSlave);
        Assert.Equal(4, r.ModbusFc);
        Assert.NotNull(r.Pins);
        Assert.Equal(2, r.Pins.Count);
        Assert.Equal("V_Avg", r.Pins[0].Name);
        Assert.Equal("VREG0", r.Pins[0].TargetTag);
        Assert.Equal("I_Avg", r.Pins[1].Name);
        Assert.Equal("VREG2", r.Pins[1].TargetTag);
    }

    [Fact]
    public void AiPromptBuilder_VietnamesePrompt_ExplicitlyForbidsClockInterpretationForProcessMeters()
    {
        // Arrange & Act
        string viPrompt = AiPromptBuilder.BuildSystemInstruction(isEnglish: false);

        // Assert
        Assert.Contains("MFM383", viPrompt);
        Assert.Contains("đồng hồ", viPrompt);
        Assert.Contains("MB_R", viPrompt);
    }

    [Fact]
    public void AiContextAggregator_WithUserPrompt_InjectsPeripheralTemplate()
    {
        // Arrange
        var aggregator = new AiContextAggregator(
            tagsProvider: () => new List<TagModel>(),
            logicEditorProvider: () => null);

        // Act
        var payload = aggregator.BuildContextPayload(isUltraCompact: false, userPrompt: "Tôi muốn tạo rule đọc thông tin đồng hồ MFM383A");

        // Assert
        Assert.NotNull(payload);
        Assert.True(payload.ContainsKey("external_peripheral"), "Payload should contain external_peripheral when prompt mentions MFM383A");
        var periph = payload["external_peripheral"];
        Assert.NotNull(periph);
        Assert.Equal("SELEC MFM383A", periph?["device"]?.ToString());
    }

    [Fact]
    public void DraftNode_SupportsModbusPinsCollection()
    {
        // Arrange & Act
        var node = new DraftNode
        {
            NodeType = "MB_READ",
            Label = "SELEC MFM383A",
            ModbusSlaveId = 1,
            ModbusFunctionCode = 4
        };

        node.ModbusPins.Add(new DraftModbusPin
        {
            Label = "Voltage",
            RegOffset = 0,
            DataType = "Float32",
            TargetTagName = "VREG0"
        });

        // Assert
        Assert.Single(node.ModbusPins);
        Assert.Equal("Voltage", node.ModbusPins[0].Label);
        Assert.Equal("Float32", node.ModbusPins[0].DataType);
    }

    [Fact]
    public void LogicEditorViewModel_ApplyAiProposal_PopulatesModbusReadPinsOnCanvasNode()
    {
        // Arrange
        var logicVm = new LogicEditorViewModel(new TagCatalogViewModel());
        var tx = new DraftGraphTransaction();

        var draftMb = new DraftNode
        {
            Id = "mb_node_1",
            NodeType = "MB_READ",
            Label = "Modbus Read (SELEC MFM383A)",
            ModbusPortIndex = 0,
            ModbusSlaveId = 1,
            ModbusFunctionCode = 4,
            ModbusAddress = 0,
            ModbusCount = 4
        };

        draftMb.ModbusPins.Add(new DraftModbusPin
        {
            Label = "V_Avg",
            RegOffset = 0,
            DataType = "Float32",
            TargetTagName = "VREG0"
        });

        draftMb.ModbusPins.Add(new DraftModbusPin
        {
            Label = "I_Avg",
            RegOffset = 2,
            DataType = "Float32",
            TargetTagName = "VREG2"
        });

        tx.AddedNodes.Add(draftMb);

        // Act
        logicVm.ApplyAiProposalToCanvas(tx);

        // Assert
        var createdMbNode = logicVm.Nodes.OfType<ModbusReadNodeViewModel>().FirstOrDefault();
        Assert.NotNull(createdMbNode);
        Assert.Equal(1, createdMbNode.SlaveId);
        Assert.Equal(ModbusMasterFunctionCode.ReadInputRegisters, createdMbNode.FunctionCode);
        Assert.Equal(2, createdMbNode.Pins.Count);
        Assert.Equal("V_Avg", createdMbNode.Pins[0].Label);
        Assert.Equal("I_Avg", createdMbNode.Pins[1].Label);
        Assert.Equal(ModbusDataType.Real, createdMbNode.Pins[0].DataType);
    }

    [Fact]
    public void AiChatViewModel_WhenMultipleModbusRulesForSameSlave_AggregatesIntoSingleDraftNodeWithPins()
    {
        // Arrange: 2 rules from AI targeting the same Slave ID 1 on Port 0
        var rules = new List<AiRuleSpecModel>
        {
            new()
            {
                Type = "MB_R",
                ModbusPort = 0,
                ModbusSlave = 1,
                ModbusFc = 4,
                ModbusAddr = 0,
                ModbusCount = 2,
                TargetTag = "VREG0"
            },
            new()
            {
                Type = "MB_R",
                ModbusPort = 0,
                ModbusSlave = 1,
                ModbusFc = 4,
                ModbusAddr = 16,
                ModbusCount = 2,
                TargetTag = "VREG2"
            }
        };

        // Act
        var tx = AiChatViewModel.ConvertRulesToDraftTransaction(rules);

        // Assert
        Assert.Single(tx.AddedNodes); // Phải gộp thành 1 node duy nhất, không được sinh 2 node
        var mbNode = tx.AddedNodes[0];
        Assert.Equal("MB_READ", mbNode.NodeType);
        Assert.Equal(1, mbNode.ModbusSlaveId);
        Assert.Equal(2, mbNode.ModbusPins.Count); // 2 chân pin tương ứng VREG0 và VREG2
        Assert.Equal("VREG0", mbNode.ModbusPins[0].TargetTagName);
        Assert.Equal("VREG2", mbNode.ModbusPins[1].TargetTagName);
    }
}
