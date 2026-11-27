using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SimplePLC.Studio.Models;

/// <summary>
/// Mô hình đặc tả luật logic 32-byte do AI sinh ra hoặc nạp vào Bảng Rule (Wire Contract V1.7).
/// </summary>
public class AiRuleSpecModel
{
    [JsonPropertyName("narrative")]
    public string Narrative { get; set; } = string.Empty;

    [JsonPropertyName("input_tag")]
    public string InputTag { get; set; } = string.Empty;

    [JsonPropertyName("trigger")]
    public string Trigger { get; set; } = "ON_RISE";

    [JsonPropertyName("for_ms")]
    public uint ForMs { get; set; } = 50;

    [JsonPropertyName("compare_op")]
    public string CompareOp { get; set; } = "NONE";

    [JsonPropertyName("threshold_lo")]
    public int ThresholdLo { get; set; } = 0;

    [JsonPropertyName("threshold_hi")]
    public int ThresholdHi { get; set; } = 0;

    [JsonPropertyName("guard_tag")]
    public string GuardTag { get; set; } = "NONE";

    [JsonPropertyName("guard_op")]
    public string GuardOp { get; set; } = "NONE";

    [JsonPropertyName("guard_val")]
    public int GuardVal { get; set; } = 0;

    [JsonPropertyName("guard_negated")]
    public bool GuardNegated { get; set; } = false;

    [JsonPropertyName("action_tag")]
    public string ActionTag { get; set; } = string.Empty;

    [JsonPropertyName("action_type")]
    public string ActionType { get; set; } = "SET_TAG";

    [JsonPropertyName("param")]
    public int Param { get; set; } = 1;

    /// <summary>
    /// Tham số phụ cho Action (Reg 14..15 của chuẩn 32-byte V1.7):
    /// Lưu hệ số lệch b của SCALE_TAG hoặc toán hạng thứ 2 của MATH_OP.
    /// </summary>
    [JsonPropertyName("aux_param")]
    public int AuxParam { get; set; } = 0;

    /// <summary>
    /// Cờ chỉ định dữ liệu số thực dấu chấm động Float32 IEEE-754 (Flags Bit 0 hoặc Bit 1).
    /// </summary>
    [JsonPropertyName("is_float")]
    public bool IsFloat { get; set; } = false;

    /// <summary>
    /// Phân loại bản thể đồ thị FBD: PIPE, MQTT_PUB, MQTT_SUB, MB_R, MB_W, TIMER, COUNTER, SCALE, MATH.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    // Các trường viết tắt (Ultra-compact JSON shorthand) nhằm tối ưu triệt để dung lượng token cho Groq & Gemini:
    [JsonPropertyName("in")]
    public string InShort { set => InputTag = string.IsNullOrWhiteSpace(InputTag) ? value : InputTag; }

    [JsonPropertyName("tag")]
    public string TagShort { set => InputTag = string.IsNullOrWhiteSpace(InputTag) ? value : InputTag; }

    [JsonPropertyName("trg")]
    public string TrgShort { set => Trigger = value; }

    [JsonPropertyName("cmp")]
    public string CmpShort { set => CompareOp = value; }

    [JsonPropertyName("lo")]
    public int LoShort { set => ThresholdLo = value; }

    [JsonPropertyName("hi")]
    public int HiShort { set => ThresholdHi = value; }

    [JsonPropertyName("ms")]
    public uint MsShort { set => ForMs = value; }

    [JsonPropertyName("grd")]
    public string GrdShort { set => GuardTag = value; }

    [JsonPropertyName("act")]
    public string ActShort { set => ActionTag = value; }

    [JsonPropertyName("op")]
    public string OpShort { set => ActionType = value; }

    [JsonPropertyName("val")]
    public int ValShort { set => Param = value; }

    // MQTT Fields:
    [JsonPropertyName("topic")]
    public string MqttTopic { get; set; } = string.Empty;

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = string.Empty;

    [JsonPropertyName("qos")]
    public int MqttQos { get; set; } = 0;

    [JsonPropertyName("retain")]
    public bool MqttRetain { get; set; } = false;

    [JsonPropertyName("target_tag")]
    public string TargetTag { get; set; } = string.Empty;

    [JsonPropertyName("source_tag")]
    public string SourceTag { get; set; } = string.Empty;

    // Modbus Fields:
    [JsonPropertyName("port")]
    public int ModbusPort { get; set; } = 0;

    [JsonPropertyName("slave")]
    public int ModbusSlave { get; set; } = 1;

    [JsonPropertyName("fc")]
    public int ModbusFc { get; set; } = 3;

    [JsonPropertyName("addr")]
    public int ModbusAddr { get; set; } = 0;

    [JsonPropertyName("count")]
    public int ModbusCount { get; set; } = 1;

    /// <summary>
    /// Danh sách chân đo (Register Pins) đa kênh cho khối MB_R / MB_W.
    /// Giúp gộp nhiều thanh ghi của cùng một Slave ID vào 1 khối polling duy nhất.
    /// </summary>
    [JsonPropertyName("pins")]
    public List<AiModbusPinSpecModel>? Pins { get; set; }

    // Macro/Math Fields:
    [JsonPropertyName("q")]
    public string QTag { get; set; } = string.Empty;

    [JsonPropertyName("cu")]
    public string CuTag { get; set; } = string.Empty;

    [JsonPropertyName("r")]
    public string RTag { get; set; } = string.Empty;

    [JsonPropertyName("cv")]
    public string CvTag { get; set; } = string.Empty;

    [JsonPropertyName("pv")]
    public int Pv { get; set; } = 0;

    [JsonPropertyName("out")]
    public string OutTag { get; set; } = string.Empty;

    [JsonPropertyName("gain")]
    public double Gain { get; set; } = 0.01;

    [JsonPropertyName("offset")]
    public double Offset { get; set; } = 0.0;

    [JsonPropertyName("unit")]
    public string Unit { get; set; } = "bar";

    [JsonPropertyName("clamp")]
    public bool Clamp { get; set; } = true;

    [JsonPropertyName("min")]
    public double ClampMin { get; set; } = 0.0;

    [JsonPropertyName("max")]
    public double ClampMax { get; set; } = 100.0;

    [JsonPropertyName("in1")]
    public string In1 { get; set; } = string.Empty;

    [JsonPropertyName("in2")]
    public string In2 { get; set; } = string.Empty;

    [JsonPropertyName("in2_const")]
    public bool In2Const { get; set; } = false;
}

/// <summary>
/// Bộ phân tích và trích xuất danh sách luật JSON từ phản hồi văn bản của mô hình AI.
/// </summary>
public static class AiRuleParser
{
    private static readonly Regex RuleJsonBlockRegex = new(
        @"```(?:json:rules|json|rules)?\s*(\[[\s\S]*?\])\s*```",
        RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex FallbackArrayRegex = new(
        @"```(?:json:rules|json|rules)?\s*(\[[\s\S]*\])",
        RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex BareArrayRegex = new(
        @"(\[[\s\S]*?\{(?:[\s\S]*?)(?:""(?:type|in|tag|input_tag|topic|narrative|port)""[\s\S]*?)\}[\s\S]*\])",
        RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex OrphanedBlockRegex = new(
        @"```(?:json:rules|json|rules)?\s*$",
        RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex LineCommentRegex = new(
        @"^\s*//.*$",
        RegexOptions.Multiline);

    public static List<AiRuleSpecModel> ExtractRules(string rawContent, out string cleanContent)
    {
        cleanContent = rawContent;
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return new List<AiRuleSpecModel>();
        }

        var match = RuleJsonBlockRegex.Match(rawContent);
        if (!match.Success)
        {
            match = FallbackArrayRegex.Match(rawContent);
        }
        if (!match.Success)
        {
            match = BareArrayRegex.Match(rawContent);
        }

        if (match.Success)
        {
            try
            {
                string jsonText = match.Groups[1].Value.Trim();
                jsonText = LineCommentRegex.Replace(jsonText, "");
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };

                var rules = JsonSerializer.Deserialize<List<AiRuleSpecModel>>(jsonText, options);
                if (rules != null && rules.Count > 0)
                {
                    // Remove the raw JSON block from displayed content for clean UX
                    cleanContent = rawContent.Remove(match.Index, match.Length).TrimEnd();
                    // Dọn dẹp cả thẻ mở code block mồ côi nếu còn sót lại
                    cleanContent = OrphanedBlockRegex.Replace(cleanContent, "").TrimEnd();
                    return rules;
                }
            }
            catch
            {
                // If JSON is malformed, fail gracefully without breaking chat
            }
        }

        // Dọn dẹp thẻ mồ côi ngay cả khi không parse được
        cleanContent = OrphanedBlockRegex.Replace(cleanContent, "").TrimEnd();
        return new List<AiRuleSpecModel>();
    }
}

/// <summary>
/// Đại diện cho cấu hình một chân đo Modbus trong đặc tả rule do AI sinh ra.
/// </summary>
public class AiModbusPinSpecModel
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("offset")]
    public ushort Offset { get; set; } = 0;

    [JsonPropertyName("type")]
    public string DataType { get; set; } = "Float32";

    [JsonPropertyName("endian")]
    public string Endianness { get; set; } = "BigEndian_ABCD";

    [JsonPropertyName("target_tag")]
    public string TargetTag { get; set; } = string.Empty;

    [JsonPropertyName("scale")]
    public float Multiplier { get; set; } = 1.0f;
}
