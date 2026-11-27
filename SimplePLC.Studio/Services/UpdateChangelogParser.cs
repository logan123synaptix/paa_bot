using System.Text;
using System.Text.RegularExpressions;

namespace SimplePLC.Studio.Services;

/// <summary>
/// Bộ phân tích và trích xuất nội dung Changelog / Release Notes đa ngữ cho SynaptiX IDE.
/// Hỗ trợ bóc tách linh hoạt các phân đoạn ngôn ngữ ([EN], [VI]), lọc bỏ rác kỹ thuật và tự động áp dụng ngôn ngữ người dùng.
/// </summary>
public static class UpdateChangelogParser
{
    private static readonly Regex EnHeaderRegex = new(@"^\s*#*\s*\[(?:EN|English)\]\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ViHeaderRegex = new(@"^\s*#*\s*\[(?:VI|Tiếng Việt|Vietnamese)\]\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Trích xuất nội dung Changelog phù hợp với ngôn ngữ hiển thị của ứng dụng.
    /// </summary>
    /// <param name="rawMarkdown">Nội dung markdown gốc từ GitHub Release hoặc Fallback.</param>
    /// <param name="isVietnamese">True nếu đang sử dụng giao diện Tiếng Việt; False nếu Tiếng Anh.</param>
    /// <param name="versionTag">Phiên bản phát hành (tùy chọn) dùng để định dạng nội dung mặc định khi thiếu mô tả.</param>
    /// <returns>Nội dung changelog đã được lọc sạch theo ngôn ngữ chỉ định.</returns>
    public static string ExtractLanguageSection(string? rawMarkdown, bool isVietnamese, string? versionTag = null)
    {
        if (string.IsNullOrWhiteSpace(rawMarkdown))
        {
            return GetDefaultFallback(isVietnamese, versionTag);
        }

        string normalized = rawMarkdown.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = normalized.Split('\n');

        bool hasEnTag = false;
        bool hasViTag = false;

        foreach (string line in lines)
        {
            if (EnHeaderRegex.IsMatch(line)) hasEnTag = true;
            if (ViHeaderRegex.IsMatch(line)) hasViTag = true;
        }

        // Nếu không có bất kỳ thẻ phân đoạn đa ngữ nào -> trả về toàn bộ markdown gốc đã cắt khoảng trắng
        if (!hasEnTag && !hasViTag)
        {
            return normalized.Trim();
        }

        var enBuilder = new StringBuilder();
        var viBuilder = new StringBuilder();
        int currentSection = 0; // 0 = preamble, 1 = EN, 2 = VI

        foreach (string line in lines)
        {
            if (EnHeaderRegex.IsMatch(line))
            {
                currentSection = 1;
                continue;
            }

            if (ViHeaderRegex.IsMatch(line))
            {
                currentSection = 2;
                continue;
            }

            if (currentSection == 1)
            {
                enBuilder.AppendLine(line);
            }
            else if (currentSection == 2)
            {
                viBuilder.AppendLine(line);
            }
        }

        string enText = enBuilder.ToString().Trim();
        string viText = viBuilder.ToString().Trim();

        if (isVietnamese)
        {
            if (!string.IsNullOrEmpty(viText)) return viText;
            if (!string.IsNullOrEmpty(enText)) return enText;
        }
        else
        {
            if (!string.IsNullOrEmpty(enText)) return enText;
            if (!string.IsNullOrEmpty(viText)) return viText;
        }

        return GetDefaultFallback(isVietnamese, versionTag);
    }

    /// <summary>
    /// Sinh nội dung tóm tắt mặc định theo ngôn ngữ khi không thể lấy nội dung phát hành.
    /// </summary>
    /// <param name="isVietnamese">Trạng thái ngôn ngữ tiếng Việt.</param>
    /// <param name="versionTag">Thẻ phiên bản.</param>
    /// <returns>Chuỗi tóm tắt mặc định đã định dạng.</returns>
    public static string GetDefaultFallback(bool isVietnamese, string? versionTag = null)
    {
        string key = string.IsNullOrEmpty(versionTag) ? "UpdateDialogDefaultChangelog" : "UpdateDialogFallbackSummaryFmt";
        string template = LocalizationService.Instance[key];

        if (!string.IsNullOrEmpty(versionTag) && template.Contains("{0}"))
        {
            return string.Format(template, versionTag);
        }

        return template;
    }
}
