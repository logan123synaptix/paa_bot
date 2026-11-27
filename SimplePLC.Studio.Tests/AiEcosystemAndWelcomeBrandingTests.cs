using System;
using SimplePLC.Studio.Services;
using SimplePLC.Studio.Services.Ai;
using SimplePLC.Studio.ViewModels;
using Xunit;

namespace SimplePLC.Studio.Tests;

/// <summary>
/// Bộ kiểm thử hợp đồng và TDD cho việc chuẩn hóa định vị Hệ sinh thái Thiết bị SynaptiX
/// và thông điệp chào mừng (Welcome Greeting) trong SynaptiX AI Chatbox.
/// </summary>
public class AiEcosystemAndWelcomeBrandingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WelcomeGreeting_DoesNotContainInternalCodenameOrNarrowPlcLabel(bool isEnglish)
    {
        // Act: Khởi tạo ChatViewModel để kiểm tra thông điệp chào mặc định
        var chatVm = new AiChatViewModel(
            tagsProvider: () => null,
            rulesProvider: () => null,
            logicEditorProvider: () => null);

        Assert.NotEmpty(chatVm.Messages);
        var welcomeMsg = chatVm.Messages[0].Content;

        // Assert: Tuyệt đối không chứa tên mã nội bộ cũ
        Assert.DoesNotContain("SimplePLCStudio", welcomeMsg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SimplePLC", welcomeMsg, StringComparison.OrdinalIgnoreCase);

        // Khẳng định chứa từ khóa hệ sinh thái thiết bị SynaptiX
        Assert.Contains("SynaptiX", welcomeMsg);
        if (isEnglish)
        {
            // Trong bản tiếng Anh hoặc có chứa từ khóa thiết bị
            Assert.True(
                welcomeMsg.Contains("Remote I/O") || welcomeMsg.Contains("Gateway DTU") || welcomeMsg.Contains("ecosystem"),
                $"Welcome message should mention the SynaptiX device ecosystem: {welcomeMsg}");
        }
        else
        {
            Assert.True(
                welcomeMsg.Contains("Remote I/O") || welcomeMsg.Contains("Gateway DTU") || welcomeMsg.Contains("Hệ sinh thái"),
                $"Welcome message should mention the SynaptiX device ecosystem in Vietnamese: {welcomeMsg}");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PromptKernel_DoesNotContainInternalCodename(bool isEnglish)
    {
        // Act
        string kernel = AiPromptBuilder.BuildCompactKernel(isEnglish);
        string consultative = AiPromptBuilder.BuildSystemInstruction(AiUserIntent.ConsultativeQnA, isEnglish);

        // Assert: Loại bỏ hoàn toàn tên mã nội bộ SimplePLC Studio
        Assert.DoesNotContain("SimplePLC Studio", kernel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SimplePLCStudio", kernel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SimplePLC Studio", consultative, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SimplePLCStudio", consultative, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConsultativeInstruction_WhenAskedAboutSynaptixIde_DefinesEcosystemAccurately(bool isEnglish)
    {
        // Act
        string prompt = AiPromptBuilder.BuildSystemInstruction(AiUserIntent.ConsultativeQnA, isEnglish);

        // Assert
        Assert.Contains("SynaptiX", prompt);
        Assert.Contains("Remote I/O", prompt);
        Assert.Contains("DTU", prompt);
    }

    [Theory]
    [InlineData(AiUserIntent.ConsultativeQnA, false)]
    [InlineData(AiUserIntent.ConsultativeQnA, true)]
    [InlineData(AiUserIntent.FbdSynthesis, false)]
    [InlineData(AiUserIntent.FbdSynthesis, true)]
    public void PromptLengthUnderSafeTokenBudget_RemainsCompliant(AiUserIntent intent, bool isEnglish)
    {
        // Act
        string prompt = AiPromptBuilder.BuildSystemInstruction(intent, isEnglish);

        // Assert: An toàn tuyệt đối dưới 10,000 ký tự (~2,500 tokens)
        Assert.True(prompt.Length < 10000, $"Prompt {intent} (EN={isEnglish}) quá dài: {prompt.Length} chars");
    }
}
