using System;
using System.IO;
using System.Text.Json;
using SimplePLC.Studio.Models;
using SimplePLC.Studio.Services.Security;
using Xunit;

namespace SimplePLC.Studio.Tests;

/// <summary>
/// Unit tests for <see cref="CredentialProtectionService"/> and <see cref="ICredentialProtectionService"/>,
/// verifying compliance with IEC 62443 data protection, Windows DPAPI encryption, and backward compatibility.
/// </summary>
public class CredentialProtectionServiceTests
{
    private readonly ICredentialProtectionService _service = CredentialProtectionService.Instance;

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void Protect_NullOrEmpty_ReturnsEmpty(string? input, string expected)
    {
        string result = _service.Protect(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void Unprotect_NullOrEmpty_ReturnsEmpty(string? input, string expected)
    {
        string result = _service.Unprotect(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Protect_ValidPlainText_ReturnsVersionedEnvelope()
    {
        string plainKey = "AIzaSyD-TestGeminiKey123456789";

        string protectedKey = _service.Protect(plainKey);

        Assert.NotNull(protectedKey);
        Assert.StartsWith(CredentialProtectionService.V1Prefix, protectedKey);
        Assert.True(_service.IsProtected(protectedKey));
        Assert.NotEqual(plainKey, protectedKey);
    }

    [Theory]
    [InlineData("AIzaSyD_Standard_Key_12345")]
    [InlineData("gsk_Groq_Api_Key_Secure_987654321")]
    [InlineData("Complex!@#$%^&*()_+~|}{[]:;?><,./-=")]
    [InlineData("MậtKhẩuTiếngViệtCóDấu_KhóaBảoMật_12345")]
    public void Unprotect_ValidEnvelope_RestoresExactPlainText(string original)
    {
        string encrypted = _service.Protect(original);
        string restored = _service.Unprotect(encrypted);

        Assert.Equal(original, restored);
    }

    [Fact]
    public void Protect_Idempotent_DoesNotDoubleEncrypt()
    {
        string plainKey = "AIzaSyD-TestGeminiKey";
        string encryptedOnce = _service.Protect(plainKey);

        string encryptedTwice = _service.Protect(encryptedOnce);

        Assert.Equal(encryptedOnce, encryptedTwice);
        Assert.Equal(plainKey, _service.Unprotect(encryptedTwice));
    }

    [Theory]
    [InlineData("AIzaSyD-OldLegacyPlaintextKey")]
    [InlineData("gsk_OldLegacyGroqKey")]
    [InlineData("Regular_Password_123")]
    public void Unprotect_LegacyPlainText_ReturnsOriginalUnchanged(string legacyPlain)
    {
        // Legacy keys without 'enc:dpapi:v1:' prefix must pass through directly
        Assert.False(_service.IsProtected(legacyPlain));

        string result = _service.Unprotect(legacyPlain);

        Assert.Equal(legacyPlain, result);
    }

    [Theory]
    [InlineData("enc:dpapi:v1:NotAValidBase64String%%%")]
    [InlineData("enc:dpapi:v1:AAAA")]
    [InlineData("enc:dpapi:v1:AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA_CorruptedPayload")]
    public void Unprotect_CorruptedPayload_DoesNotThrow_ReturnsEmpty(string corrupted)
    {
        // Fault injection test: Corrupted or tampered payload must fail safely without throwing exceptions
        string result = _service.Unprotect(corrupted);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void AiSettingsModel_SaveAndLoad_RoundtripsEncryptedKeysCorrectly()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"synaptix_test_ai_{Guid.NewGuid():N}.json");
        AiSettingsModel.CustomSettingsFilePath = tempFile;

        try
        {
            var model = new AiSettingsModel
            {
                Provider = "Gemini",
                GeminiApiKey = "AIzaSyD-SecretGeminiKey-999",
                GroqApiKey = "gsk_SecretGroqKey-888",
                ModelName = "gemini-3.8-flash"
            };

            model.Save();

            Assert.True(File.Exists(tempFile));
            string rawJson = File.ReadAllText(tempFile);

            // Raw JSON on disk MUST contain encrypted versioned envelopes, NOT plaintext keys!
            Assert.DoesNotContain("AIzaSyD-SecretGeminiKey-999", rawJson);
            Assert.DoesNotContain("gsk_SecretGroqKey-888", rawJson);
            Assert.Contains(CredentialProtectionService.V1Prefix, rawJson);

            // Loading from disk MUST decrypt keys transparently into memory
            var loaded = AiSettingsModel.Load();
            Assert.NotNull(loaded);
            Assert.Equal("AIzaSyD-SecretGeminiKey-999", loaded.GeminiApiKey);
            Assert.Equal("gsk_SecretGroqKey-888", loaded.GroqApiKey);
            Assert.Equal("AIzaSyD-SecretGeminiKey-999", loaded.EffectiveGeminiApiKey);
            Assert.Equal("gsk_SecretGroqKey-888", loaded.EffectiveGroqApiKey);
        }
        finally
        {
            AiSettingsModel.CustomSettingsFilePath = null;
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void AiSettingsModel_LoadsLegacyPlaintextAndEncryptsOnSave()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"synaptix_test_legacy_ai_{Guid.NewGuid():N}.json");
        AiSettingsModel.CustomSettingsFilePath = tempFile;

        try
        {
            // Simulate legacy plaintext file on disk
            string legacyJson = """
            {
              "Provider": "Groq",
              "GeminiApiKey": "AIzaSyD-OldLegacyKey",
              "GroqApiKey": "gsk_OldLegacyGroqKey",
              "ModelName": "gemini-3.8-flash",
              "GroqModel": "openai/gpt-oss-120b"
            }
            """;
            File.WriteAllText(tempFile, legacyJson);

            // Load legacy file
            var loaded = AiSettingsModel.Load();
            Assert.Equal("AIzaSyD-OldLegacyKey", loaded.GeminiApiKey);
            Assert.Equal("gsk_OldLegacyGroqKey", loaded.GroqApiKey);

            // Save back to disk
            loaded.Save();

            // After save, the file on disk MUST now be upgraded to encrypted format
            string upgradedJson = File.ReadAllText(tempFile);
            Assert.DoesNotContain("AIzaSyD-OldLegacyKey", upgradedJson);
            Assert.DoesNotContain("gsk_OldLegacyGroqKey", upgradedJson);
            Assert.Contains(CredentialProtectionService.V1Prefix, upgradedJson);

            // Re-loading upgraded file works seamlessly
            var reloaded = AiSettingsModel.Load();
            Assert.Equal("AIzaSyD-OldLegacyKey", reloaded.GeminiApiKey);
            Assert.Equal("gsk_OldLegacyGroqKey", reloaded.GroqApiKey);
        }
        finally
        {
            AiSettingsModel.CustomSettingsFilePath = null;
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
