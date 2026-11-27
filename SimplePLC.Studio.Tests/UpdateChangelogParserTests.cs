using SimplePLC.Studio.Services;
using Xunit;

namespace SimplePLC.Studio.Tests;

/// <summary>
/// Unit tests cho bộ phân tích Changelog đa ngữ (UpdateChangelogParser).
/// Kiểm thử theo chuẩn IEC 61508-3 Triad: Happy Path, Boundary/Fallback, Fault Injection.
/// </summary>
public class UpdateChangelogParserTests
{
    private const string SampleBilingualMarkdown = """
        # [EN]
        ## What's New in v2.15.7
        - Local DPAPI encryption for credentials and AI secrets.
        - Resolved toast notification layout shift.
        - Enhanced real-time Modbus RTU telemetry inspector.

        # [VI]
        ## Điểm mới trong v2.15.7
        - Mã hóa bảo mật DPAPI cục bộ cho cấu hình và AI key.
        - Sửa lỗi tràn bố cục thông báo Toast.
        - Nâng cấp bộ giám sát chẩn đoán truyền thông Modbus RTU.
        """;

    [Fact]
    public void ExtractLanguageSection_WhenBilingual_ExtractsEnglishCorrectly()
    {
        string result = UpdateChangelogParser.ExtractLanguageSection(SampleBilingualMarkdown, isVietnamese: false);

        Assert.Contains("What's New in v2.15.7", result);
        Assert.Contains("Local DPAPI encryption", result);
        Assert.DoesNotContain("Điểm mới trong v2.15.7", result);
        Assert.DoesNotContain("Mã hóa bảo mật DPAPI", result);
    }

    [Fact]
    public void ExtractLanguageSection_WhenBilingual_ExtractsVietnameseCorrectly()
    {
        string result = UpdateChangelogParser.ExtractLanguageSection(SampleBilingualMarkdown, isVietnamese: true);

        Assert.Contains("Điểm mới trong v2.15.7", result);
        Assert.Contains("Mã hóa bảo mật DPAPI", result);
        Assert.DoesNotContain("What's New in v2.15.7", result);
        Assert.DoesNotContain("Local DPAPI encryption", result);
    }

    [Fact]
    public void ExtractLanguageSection_WhenReversedOrder_ExtractsTargetLanguageCorrectly()
    {
        string reversedMarkdown = """
            # [VI]
            ## Cập nhật v2.16.0
            - Tính năng A

            # [EN]
            ## Update v2.16.0
            - Feature A
            """;

        string enResult = UpdateChangelogParser.ExtractLanguageSection(reversedMarkdown, isVietnamese: false);
        string viResult = UpdateChangelogParser.ExtractLanguageSection(reversedMarkdown, isVietnamese: true);

        Assert.Contains("Feature A", enResult);
        Assert.DoesNotContain("Tính năng A", enResult);

        Assert.Contains("Tính năng A", viResult);
        Assert.DoesNotContain("Feature A", viResult);
    }

    [Theory]
    [InlineData("## Single Language Release\n- Bug fixes and stability improvements.")]
    [InlineData("Phiên bản phát hành nội bộ không gắn thẻ tag đa ngữ.")]
    public void ExtractLanguageSection_WhenSingleLanguageWithoutTags_ReturnsFullMarkdownCleaned(string singleLangText)
    {
        string resultEn = UpdateChangelogParser.ExtractLanguageSection(singleLangText, isVietnamese: false);
        string resultVi = UpdateChangelogParser.ExtractLanguageSection(singleLangText, isVietnamese: true);

        Assert.Equal(singleLangText.Trim(), resultEn);
        Assert.Equal(singleLangText.Trim(), resultVi);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t\r\n  ")]
    public void ExtractLanguageSection_WhenEmptyOrNull_ReturnsLocalizedDefaultFallback(string? emptyInput)
    {
        string resultVi = UpdateChangelogParser.ExtractLanguageSection(emptyInput, isVietnamese: true);
        string resultEn = UpdateChangelogParser.ExtractLanguageSection(emptyInput, isVietnamese: false);

        Assert.False(string.IsNullOrWhiteSpace(resultVi));
        Assert.False(string.IsNullOrWhiteSpace(resultEn));
    }

    [Fact]
    public void ExtractLanguageSection_WhenEmpty_WithVersionTag_FormatsFallbackWithVersion()
    {
        string result = UpdateChangelogParser.ExtractLanguageSection("", isVietnamese: false, versionTag: "v2.15.7");

        Assert.Contains("v2.15.7", result);
    }

    [Fact]
    public void ExtractLanguageSection_WhenTargetLanguageMissing_FallsBackToAlternativeLanguage()
    {
        string englishOnlyWithTag = """
            # [EN]
            ## English Only Notes
            - Only English bullet points provided
            """;

        // Operator is using Vietnamese, but release only has [EN]
        string result = UpdateChangelogParser.ExtractLanguageSection(englishOnlyWithTag, isVietnamese: true);

        Assert.Contains("Only English bullet points provided", result);
    }

    [Fact]
    public void ExtractLanguageSection_CaseInsensitiveAndVariantMarkers_MatchesAppropriateSection()
    {
        string variantMarkdown = """
            [english]
            - Modernized telemetry

            ## [Tiếng Việt]
            - Hiện đại hóa đo lường
            """;

        string enResult = UpdateChangelogParser.ExtractLanguageSection(variantMarkdown, isVietnamese: false);
        string viResult = UpdateChangelogParser.ExtractLanguageSection(variantMarkdown, isVietnamese: true);

        Assert.Contains("Modernized telemetry", enResult);
        Assert.Contains("Hiện đại hóa đo lường", viResult);
    }
}
