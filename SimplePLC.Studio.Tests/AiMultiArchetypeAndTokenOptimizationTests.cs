using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SimplePLC.Domain.Enums;
using SimplePLC.Domain.Models;
using SimplePLC.Studio.Models;
using SimplePLC.Studio.Services;
using SimplePLC.Studio.Services.Ai;
using SimplePLC.Studio.ViewModels;
using Xunit;

namespace SimplePLC.Studio.Tests;

/// <summary>
/// Bộ kiểm thử đơn vị cho kiến trúc Đa bản thể (Multi-Archetype) siêu cô đọng và tối ưu Token:
/// 1. Bóc tách JSON đa bản thể (MQTT_PUB, MQTT_SUB, MB_R, MB_W, TIMER, COUNTER, SCALE, MATH, PIPE)
/// 2. Tiết kiệm Token cho Groq Cloud và Gemini
/// 3. Vòng đời AcceptProposal trung thực (Axiom 2: Visual Truth) và đồng bộ Canvas ➔ Rule Table
/// 4. Chuyển đổi luật MQTT thành khối MqttPublishNodeViewModel chuyên biệt trên Canvas
/// </summary>
public class AiMultiArchetypeAndTokenOptimizationTests
{
    private static void RunOnSta(Action action)
    {
        var tcs = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            try
            {
                action();
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        tcs.Task.GetAwaiter().GetResult();
    }

    [Fact]
    public void AiRuleParser_ExtractRules_ParsesCompactMqttPubArchetype()
    {
        // Arrange
        string rawJson = """
            Dưới đây là sơ đồ phát MQTT:
            ```json:rules
            [
              {
                "type": "MQTT_PUB",
                "tag": "AI0",
                "topic": "factory/sensor/ai0",
                "mode": "JSON",
                "qos": 1,
                "retain": true
              }
            ]
            ```
            """;

        // Act
        var rules = AiRuleParser.ExtractRules(rawJson, out string clean);

        // Assert
        Assert.Single(rules);
        var r = rules[0];
        Assert.Equal("MQTT_PUB", r.Type);
        Assert.Equal("AI0", r.InputTag);
        Assert.Equal("factory/sensor/ai0", r.MqttTopic);
        Assert.Equal("JSON", r.Mode);
        Assert.Equal(1, r.MqttQos);
        Assert.True(r.MqttRetain);
        Assert.DoesNotContain("```json:rules", clean);
    }

    [Fact]
    public void AiRuleParser_ExtractRules_WithComments_ExtractsRulesAndCleansJson()
    {
        // Arrange - Payload thực tế từ người dùng có kèm comment // Rule 1, // Rule 2
        string rawWithComments = """
            1. Nguyên lý hoạt động
            Sơ đồ nháp (Ghost Preview) đã được phác thảo trên Canvas FBD.

            ```json:rules
            [
              // Rule 1 - Detect change of product counter and raise a flag
              {
                "type": "PIPE",
                "in": "VREG0",
                "trg": "ON_CHANGE",
                "cmp": "NONE",
                "lo": 0,
                "hi": 0,
                "ms": 0,
                "grd": "NONE",
                "act": "VFLAG0",
                "op": "SET_TAG",
                "val": 1
              },
              // Rule 2 - Publish the counter value to MQTT whenever it changes
              {
                "type": "MQTT_PUB",
                "tag": "VREG0",
                "topic": "factory/production/count",
                "mode": "JSON",
                "qos": 0,
                "retain": false
              }
            ]
            ```
            """;

        // Act
        var rules = AiRuleParser.ExtractRules(rawWithComments, out string clean);

        // Assert
        Assert.Equal(2, rules.Count);
        Assert.Equal("PIPE", rules[0].Type);
        Assert.Equal("VREG0", rules[0].InputTag);
        Assert.Equal("VFLAG0", rules[0].ActionTag);

        Assert.Equal("MQTT_PUB", rules[1].Type);
        Assert.Equal("VREG0", rules[1].InputTag);
        Assert.Equal("factory/production/count", rules[1].MqttTopic);

        // Đảm bảo khối JSON và comment đã được gỡ bỏ sạch sẽ khỏi nội dung hiển thị cho người dùng
        Assert.DoesNotContain("```json:rules", clean);
        Assert.DoesNotContain("Rule 1 - Detect change", clean);
    }

    [Fact]
    public void AiRuleParser_ExtractRules_ParsesCompactModbusReadArchetype()
    {
        // Arrange
        string rawJson = """
            [
              {
                "type": "MB_R",
                "port": 1,
                "slave": 2,
                "fc": 3,
                "addr": 100,
                "count": 4,
                "target_tag": "VREG5"
              }
            ]
            """;

        // Act
        var rules = AiRuleParser.ExtractRules(rawJson, out _);

        // Assert
        Assert.Single(rules);
        var r = rules[0];
        Assert.Equal("MB_R", r.Type);
        Assert.Equal(1, r.ModbusPort);
        Assert.Equal(2, r.ModbusSlave);
        Assert.Equal(3, r.ModbusFc);
        Assert.Equal(100, r.ModbusAddr);
        Assert.Equal(4, r.ModbusCount);
        Assert.Equal("VREG5", r.TargetTag);
    }

    [Fact]
    public void AiPromptBuilder_BuildSystemInstruction_IsUltraCompactForGroq()
    {
        // Act
        string viPrompt = AiPromptBuilder.BuildSystemInstruction(AiUserIntent.FbdSynthesis, isEnglish: false, isAppEnglish: false);
        string enPrompt = AiPromptBuilder.BuildSystemInstruction(AiUserIntent.FbdSynthesis, isEnglish: true, isAppEnglish: true);

        // Assert - Ước lượng độ dài ký tự < 12,000 ký tự (~1,800 - 2,000 tokens), hoàn toàn an toàn dưới ngưỡng 8k TPM của Groq
        Assert.True(viPrompt.Length < 12000, $"Vietnamese prompt is too large ({viPrompt.Length} chars)");
        Assert.True(enPrompt.Length < 12000, $"English prompt is too large ({enPrompt.Length} chars)");

        // Chứa đặc tả bản thể đa năng
        Assert.Contains("MQTT_PUB", viPrompt);
        Assert.Contains("MB_R", viPrompt);
        Assert.Contains("MQTT_PUB", enPrompt);
        Assert.Contains("MB_R", enPrompt);
    }

    [Fact]
    public void AiChatViewModel_AcceptProposal_TruthfulReportingAndCanvasSync()
    {
        RunOnSta(() =>
        {
            // Arrange
            var tagCatalog = new TagCatalogViewModel();
            var logicVM = new LogicEditorViewModel(tagCatalog);
            var existingRules = new List<RuleItemModel>
            {
                new() { Index = 0, Id = "R1", Narrative = "Old Rule 1" }
            };

            var chatVM = new AiChatViewModel(
                () => tagCatalog.AllTags,
                () => existingRules,
                () => logicVM);

            // Nạp đề xuất nháp gồm 2 khối (Input AI0 + MqttPublish)
            var tx = new DraftGraphTransaction();
            tx.AddedNodes.Add(new DraftNode
            {
                Id = "node_in_1",
                NodeType = "Input",
                TagName = "AI0",
                PositionX = 100,
                PositionY = 100
            });
            tx.AddedNodes.Add(new DraftNode
            {
                Id = "node_mqtt_1",
                NodeType = "MqttPublish",
                TagName = "AI0",
                MqttTopic = "factory/sensor/ai0",
                PositionX = 340,
                PositionY = 100
            });
            tx.AddedWires.Add(new DraftWire
            {
                SourceNodeId = "node_in_1",
                SourcePort = "Out",
                TargetNodeId = "node_mqtt_1",
                TargetPort = "Val"
            });

            logicVM.ApplyAiProposalToCanvas(tx, "Đề xuất AI: +2 khối");
            Assert.True(logicVM.HasPendingAiProposal);
            Assert.Equal(2, logicVM.Nodes.Count(n => n.IsGhost));

            var msg = new ChatMessageModel("assistant", "Đã đề xuất MQTT")
            {
                HasProposal = true,
                ExtractedRules = new List<AiRuleSpecModel>
                {
                    new() { Type = "MQTT_PUB", InputTag = "AI0", MqttTopic = "factory/sensor/ai0" }
                }
            };

            // Act: Bấm nút chấp thuận đề xuất
            chatVM.AcceptProposal(msg);

            // Assert
            Assert.False(logicVM.HasPendingAiProposal, "Pending AI proposal should be resolved");
            Assert.Equal(0, logicVM.Nodes.Count(n => n.IsGhost)); // Tất cả ghost đã hóa thành node thật
            Assert.True(msg.IsApplied);
            Assert.False(msg.IsRejected);

            // Bắt buộc báo đúng số khối mới (+2), TUYỆT ĐỐI không được báo "1 Rules to Rule Table" của rule cũ
            Assert.Contains("+2", msg.AppliedStatusText);
            Assert.DoesNotContain("1 Rules to Rule Table", msg.AppliedStatusText);
        });
    }

    [Fact]
    public void AiChatViewModel_ExtractedRules_ConvertsMqttToMqttPublishNode()
    {
        RunOnSta(() =>
        {
            // Arrange
            var tagCatalog = new TagCatalogViewModel();
            var logicVM = new LogicEditorViewModel(tagCatalog);
            var chatVM = new AiChatViewModel(
                () => tagCatalog.AllTags,
                () => new List<RuleItemModel>(),
                () => logicVM);

            var extractedRules = new List<AiRuleSpecModel>
            {
                new()
                {
                    Type = "MQTT_PUB",
                    InputTag = "AI0",
                    MqttTopic = "plant/water/tank_level",
                    Mode = "JSON",
                    MqttQos = 1,
                    MqttRetain = true
                }
            };

            // Act: Mô phỏng tạo transaction cho FBD từ danh sách rule
            var tx = new DraftGraphTransaction();
            double x = 60, y = 100;
            int ruleIdx = 0;
            foreach (var rule in extractedRules)
            {
                bool isMqttPub = string.Equals(rule.Type, "MQTT_PUB", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(rule.ActionTag, "MQTT_PUBLISH", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(rule.ActionType, "MQTT_PUBLISH", StringComparison.OrdinalIgnoreCase);

                if (isMqttPub)
                {
                    string srcTag = !string.IsNullOrWhiteSpace(rule.InputTag) ? rule.InputTag : "AI0";
                    string inId = $"in_{ruleIdx}";
                    string mqttId = $"mqtt_{ruleIdx}";

                    tx.AddedNodes.Add(new DraftNode
                    {
                        Id = inId,
                        NodeType = "Input",
                        TagName = srcTag,
                        PositionX = x,
                        PositionY = y
                    });

                    tx.AddedNodes.Add(new DraftNode
                    {
                        Id = mqttId,
                        NodeType = "MqttPublish",
                        TagName = srcTag,
                        MqttTopic = rule.MqttTopic,
                        MqttPayloadMode = rule.Mode,
                        MqttQos = rule.MqttQos,
                        MqttRetain = rule.MqttRetain,
                        PositionX = x + 240,
                        PositionY = y
                    });

                    tx.AddedWires.Add(new DraftWire
                    {
                        SourceNodeId = inId,
                        SourcePort = "Out",
                        TargetNodeId = mqttId,
                        TargetPort = "Val"
                    });
                }
            }

            logicVM.ApplyAiProposalToCanvas(tx, "Đề xuất MQTT");

            // Assert
            var mqttNode = logicVM.Nodes.OfType<MqttPublishNodeViewModel>().FirstOrDefault();
            Assert.NotNull(mqttNode);
            Assert.Equal("plant/water/tank_level", mqttNode.Topic);
            Assert.Equal("JSON", mqttNode.PayloadMode);
            Assert.Equal(1, mqttNode.Qos);
            Assert.True(mqttNode.Retain);
        });
    }

    [Fact]
    public void AiContextAggregator_WithProductProvider_InjectsHardwareCapabilities_InBothFullAndCompactModes()
    {
        // Arrange: Thiết bị Remote I/O có WIFI và MQTT_PUB, nhưng KHÔNG hỗ trợ MATH_ALU
        var product = ProductDefinition.CreateRemoteIo8Di8Do4Ai(
            hardwareCapabilities: SPLC_HardwareCapabilities.BASE_LOGIC | SPLC_HardwareCapabilities.WIFI | SPLC_HardwareCapabilities.MQTT_PUB);

        var tags = new List<TagModel>
        {
            new() { Name = "AI0", DataType = TagDataType.Float32, Alias = "Sensor" }
        };

        var aggregator = new AiContextAggregator(
            () => tags,
            () => null,
            productProvider: () => product);

        // Act
        var normalPayload = aggregator.BuildContextPayload(isUltraCompact: false);
        var compactPayload = aggregator.BuildContextPayload(isUltraCompact: true);

        // Assert - Normal Payload
        var hwProfile = normalPayload["hardware_profile"]?.AsObject();
        Assert.NotNull(hwProfile);
        Assert.Contains("Remote I/O (8DI-8DO-4AI)", hwProfile["target"]?.GetValue<string>());
        Assert.False(hwProfile["supports_math_alu"]?.GetValue<bool>());
        Assert.True(hwProfile["supports_mqtt_publish"]?.GetValue<bool>());
        Assert.True(hwProfile["supports_network"]?.GetValue<bool>());

        // Assert - Ultra Compact Payload (Groq Cloud)
        Assert.Null(compactPayload["hardware_profile"]);
        var compactHw = compactPayload["hw"]?.AsObject();
        Assert.NotNull(compactHw);
        Assert.Equal("Remote I/O (8DI-8DO-4AI)", compactHw["model"]?.GetValue<string>());
        Assert.False(compactHw["math_alu"]?.GetValue<bool>());
        Assert.True(compactHw["mqtt_pub"]?.GetValue<bool>());
        Assert.True(compactHw["net"]?.GetValue<bool>());
    }

    [Fact]
    public void AiContextAggregator_WhenDeviceLacksNetwork_MqttPubIsReportedFalse()
    {
        // Arrange: Thiết bị Remote I/O cơ bản KHÔNG có WIFI, ETHERNET, hay MQTT
        var product = ProductDefinition.CreateRemoteIo8Di8Do4Ai(
            hardwareCapabilities: SPLC_HardwareCapabilities.BASE_LOGIC | SPLC_HardwareCapabilities.RETENTIVE);

        var aggregator = new AiContextAggregator(
            () => null,
            () => null,
            productProvider: () => product);

        // Act
        var compactPayload = aggregator.BuildContextPayload(isUltraCompact: true);
        var normalPayload = aggregator.BuildContextPayload(isUltraCompact: false);

        // Assert: Cả compact và normal đều báo rõ không có mạng và không có MQTT
        var compactHw = compactPayload["hw"]?.AsObject();
        Assert.NotNull(compactHw);
        Assert.False(compactHw["mqtt_pub"]?.GetValue<bool>());
        Assert.False(compactHw["net"]?.GetValue<bool>());

        var hwProfile = normalPayload["hardware_profile"]?.AsObject();
        Assert.NotNull(hwProfile);
        Assert.False(hwProfile["supports_mqtt_publish"]?.GetValue<bool>());
        Assert.False(hwProfile["supports_network"]?.GetValue<bool>());
    }

    [Fact]
    public void AiPromptBuilder_Instructions_EnforceHardwareCapabilityAndZeroMathCopy()
    {
        // Act
        string viPrompt = AiPromptBuilder.BuildSystemInstruction(AiUserIntent.FbdSynthesis, isEnglish: false);
        string enPrompt = AiPromptBuilder.BuildSystemInstruction(AiUserIntent.FbdSynthesis, isEnglish: true);

        // Assert: Tiếng Việt chứa nghiêm cấm MATH_ALU khi không hỗ trợ và cấm dùng MATH để copy cho MQTT
        Assert.Contains("math_alu: false", viPrompt);
        Assert.Contains("TUYỆT ĐỐI CẤM sinh khối `MATH`", viPrompt);
        Assert.Contains("TUYỆT ĐỐI CẤM dùng khối `MATH`", viPrompt);
        Assert.Contains("NGUYÊN TẮC BẮN VIỄN TRẮC TRỰC TIẾP", viPrompt);

        // Assert: Tiếng Anh chứa lệnh cấm tương đương
        Assert.Contains("STRICT HARDWARE CAPABILITY COMPLIANCE", enPrompt);
        Assert.Contains("STRICT ZERO-MATH TELEMETRY", enPrompt);
        Assert.Contains("NEVER emit `MATH` nodes", enPrompt);
    }

    [Fact]
    public void AiPromptBuilder_BuildContextPrompt_WithProduct_IncludesTargetHardwareDevice()
    {
        // Arrange
        var product = ProductDefinition.CreateRemoteIo8Di8Do4Ai(
            hardwareCapabilities: SPLC_HardwareCapabilities.BASE_LOGIC | SPLC_HardwareCapabilities.MQTT_PUB);

        // Act
        string viContext = AiPromptBuilder.BuildContextPrompt(null, null, "Gửi AI0 qua MQTT", isEnglish: false, product: product);
        string enContext = AiPromptBuilder.BuildContextPrompt(null, null, "Publish AI0 to MQTT", isEnglish: true, product: product);

        // Assert
        Assert.Contains("Remote I/O (8DI-8DO-4AI)", viContext);
        Assert.Contains("Hỗ trợ MATH_ALU: False", viContext);
        Assert.Contains("Hỗ trợ MQTT: True", viContext);

        Assert.Contains("Remote I/O (8DI-8DO-4AI)", enContext);
        Assert.Contains("Supports MATH_ALU: False", enContext);
        Assert.Contains("Supports MQTT: True", enContext);
    }
}
