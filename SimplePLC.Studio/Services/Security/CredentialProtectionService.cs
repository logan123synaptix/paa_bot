using System;
using System.Security.Cryptography;
using System.Text;

namespace SimplePLC.Studio.Services.Security;

/// <summary>
/// Dịch vụ mã hóa và bảo vệ bí mật cục bộ trên Windows bằng DPAPI kết hợp muối ứng dụng (Application Entropy).
/// </summary>
public sealed class CredentialProtectionService : ICredentialProtectionService
{
    private static readonly Lazy<CredentialProtectionService> _lazy = new(() => new CredentialProtectionService());

    /// <summary>
    /// Singleton instance toàn cục của dịch vụ bảo vệ thông tin xác thực.
    /// </summary>
    public static CredentialProtectionService Instance => _lazy.Value;

    /// <summary>
    /// Tiền tố nhận diện phong bì bảo vệ phiên bản 1 (DPAPI CurrentUser).
    /// </summary>
    public const string V1Prefix = "enc:dpapi:v1:";

    // Muối ứng dụng chuyên biệt của SynaptiX ngăn chặn các ứng dụng khác của cùng User giải mã tùy tiện
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SynaptiX::IndustrialSecurity::v1::Salt");

    /// <summary>
    /// Khởi tạo thể hiện mới của <see cref="CredentialProtectionService"/>.
    /// </summary>
    public CredentialProtectionService()
    {
    }

    /// <summary>
    /// Mã hóa chuỗi văn bản thuần thành phong bì mã hóa Base64 có tiền tố <see cref="V1Prefix"/>.
    /// </summary>
    /// <param name="plainText">Chuỗi văn bản thuần cần mã hóa.</param>
    /// <returns>Chuỗi phong bì mã hóa dạng 'enc:dpapi:v1:...', hoặc giữ nguyên nếu chuỗi null/rỗng.</returns>
    public string Protect(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return plainText ?? string.Empty;

        // Tính lũy đẳng (Idempotency): Nếu chuỗi đã được mã hóa trước đó, không mã hóa lồng lần 2
        if (IsProtected(plainText))
            return plainText;

        try
        {
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] cipherBytes = ProtectedData.Protect(
                plainBytes,
                Entropy,
                DataProtectionScope.CurrentUser);

            return V1Prefix + Convert.ToBase64String(cipherBytes);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SECURITY ERROR] DPAPI Protect failed: {ex.Message}");
            return plainText; // Fallback an toàn không làm crash ứng dụng
        }
    }

    /// <summary>
    /// Giải mã chuỗi phong bì trở lại văn bản thuần ban đầu.
    /// Tự động tương thích ngược: nếu chuỗi không có tiền tố phong bì, trả về nguyên trạng.
    /// </summary>
    /// <param name="cipherText">Chuỗi phong bì mã hóa hoặc chuỗi plaintext bản cũ.</param>
    /// <returns>Văn bản thuần gốc, hoặc chuỗi rỗng nếu giải mã thất bại do khóa không khớp.</returns>
    public string Unprotect(string? cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return cipherText ?? string.Empty;

        // Tương thích ngược: Nếu chuỗi không có tiền tố phong bì, đây là dữ liệu bản cũ (plaintext)
        if (!IsProtected(cipherText))
            return cipherText;

        try
        {
            string base64Payload = cipherText[V1Prefix.Length..];
            byte[] cipherBytes = Convert.FromBase64String(base64Payload);

            byte[] plainBytes = ProtectedData.Unprotect(
                cipherBytes,
                Entropy,
                DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SECURITY ERROR] DPAPI Unprotect failed (Key invalid or copied from another machine): {ex.Message}");
            return string.Empty; // Trả về rỗng an toàn khi không thể giải mã (chống can thiệp)
        }
    }

    /// <summary>
    /// Kiểm tra một chuỗi có mang định dạng phong bì mã hóa hợp lệ hay không.
    /// </summary>
    /// <param name="value">Chuỗi cần kiểm tra.</param>
    /// <returns>True nếu chuỗi bắt đầu bằng tiền tố phiên bản hợp lệ, ngược lại False.</returns>
    public bool IsProtected(string? value) =>
        !string.IsNullOrEmpty(value) && value.StartsWith(V1Prefix, StringComparison.Ordinal);
}
