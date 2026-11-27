using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SimplePLC.Studio.Services.Ai.Peripheral;

/// <summary>
/// Đại diện cho cấu hình chân đo (Register Pin) mẫu của một thiết bị ngoại vi Modbus.
/// </summary>
public record PeripheralPinTemplate(
    string Name,
    ushort RegOffset,
    string DataType,
    string Endianness,
    string DefaultTag);

/// <summary>
/// Đại diện cho bản mẫu thiết bị ngoại vi công nghiệp (Đồng hồ điện, cảm biến, biến tần...).
/// </summary>
public record PeripheralDeviceTemplate(
    string ModelName,
    string Category,
    byte DefaultSlaveId,
    byte FunctionCode,
    uint PollIntervalMs,
    IReadOnlyList<PeripheralPinTemplate> Pins,
    IReadOnlyList<string> Aliases);

/// <summary>
/// Quản lý từ điển và nhận diện thiết bị ngoại vi công nghiệp theo cơ chế Dynamic On-Demand RAG.
/// Giúp cung cấp siêu dữ liệu thanh ghi chính xác cho AI với mức tiêu hao token cực thấp (&lt; 80 tokens).
/// </summary>
public class PeripheralTemplateRegistry
{
    private static readonly Lazy<PeripheralTemplateRegistry> _lazy = new(() => new PeripheralTemplateRegistry());
    public static PeripheralTemplateRegistry Instance => _lazy.Value;

    private readonly List<PeripheralDeviceTemplate> _templates = new();

    public PeripheralTemplateRegistry()
    {
        RegisterDefaultTemplates();
    }

    /// <summary>
    /// Đăng ký các thiết bị công nghiệp tiêu chuẩn phổ biến.
    /// </summary>
    private void RegisterDefaultTemplates()
    {
        // 1. SELEC MFM383A / MFM384: Đồng hồ đo điện năng 3 pha đa chức năng
        _templates.Add(new PeripheralDeviceTemplate(
            ModelName: "SELEC MFM383A",
            Category: "PowerMeter",
            DefaultSlaveId: 1,
            FunctionCode: 4, // FC04 Read Input Registers
            PollIntervalMs: 1000,
            Pins: new List<PeripheralPinTemplate>
            {
                new("V_Avg", 0, "Float32", "BigEndian_ABCD", "VREG0"),
                new("I_Avg", 16, "Float32", "BigEndian_ABCD", "VREG2"),
                new("Total_Power", 32, "Float32", "BigEndian_ABCD", "VREG4")
            },
            Aliases: new[] { "mfm383a", "mfm-383a", "mfm383", "mfm 383", "selec mfm", "dong ho mfm" }));

        // 2. Schneider Electric EasyLogic PM2200 / PM2100
        _templates.Add(new PeripheralDeviceTemplate(
            ModelName: "Schneider PM2200",
            Category: "PowerMeter",
            DefaultSlaveId: 1,
            FunctionCode: 3, // FC03 Read Holding Registers
            PollIntervalMs: 1000,
            Pins: new List<PeripheralPinTemplate>
            {
                new("V_L1L2", 3019, "Float32", "BigEndian_ABCD", "VREG0"),
                new("I_Avg", 3009, "Float32", "BigEndian_ABCD", "VREG2"),
                new("Active_Power", 3059, "Float32", "BigEndian_ABCD", "VREG4")
            },
            Aliases: new[] { "pm2200", "pm-2200", "pm2100", "pm 2200", "schneider pm" }));

        // 3. SHT30 / Cảm biến nhiệt độ - độ ẩm công nghiệp RS-485 Modbus RTU
        _templates.Add(new PeripheralDeviceTemplate(
            ModelName: "SHT30 Modbus Sensor",
            Category: "Sensor",
            DefaultSlaveId: 1,
            FunctionCode: 4, // FC04
            PollIntervalMs: 500,
            Pins: new List<PeripheralPinTemplate>
            {
                new("Temperature", 0, "Int16", "BigEndian_ABCD", "VREG0"),
                new("Humidity", 1, "Int16", "BigEndian_ABCD", "VREG1")
            },
            Aliases: new[] { "sht30", "sht-30", "sht3x", "nhiet am modbus", "cam bien sht30" }));
    }

    /// <summary>
    /// Thử khớp yêu cầu văn bản của người dùng với một thiết bị ngoại vi trong từ điển.
    /// </summary>
    public bool TryMatchPeripheral(string userPrompt, out PeripheralDeviceTemplate? matchedTemplate)
    {
        matchedTemplate = null;
        if (string.IsNullOrWhiteSpace(userPrompt)) return false;

        string normalized = userPrompt.ToLowerInvariant();

        foreach (var t in _templates)
        {
            if (t.Aliases.Any(a => normalized.Contains(a)) || normalized.Contains(t.ModelName.ToLowerInvariant()))
            {
                matchedTemplate = t;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Sinh chuỗi JSON tóm tắt siêu cô đọng (&lt; 80 tokens) để tiêm vào ngữ cảnh Prompt của LLM.
    /// </summary>
    public string BuildCompactPromptSnippet(PeripheralDeviceTemplate template)
    {
        var pinList = template.Pins.Select(p => new
        {
            name = p.Name,
            offset = p.RegOffset,
            type = p.DataType,
            tag = p.DefaultTag
        });

        var payload = new
        {
            device = template.ModelName,
            cat = template.Category,
            slave = template.DefaultSlaveId,
            fc = template.FunctionCode,
            pins = pinList
        };

        return JsonSerializer.Serialize(payload);
    }
}
