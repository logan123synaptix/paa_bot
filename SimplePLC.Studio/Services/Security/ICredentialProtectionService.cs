namespace SimplePLC.Studio.Services.Security;

/// <summary>
/// Giao diện dịch vụ bảo vệ và mã hóa thông tin xác thực nhạy cảm cục bộ trên máy trạm (IEC 62443).
/// </summary>
public interface ICredentialProtectionService
{
    /// <summary>
    /// Mã hóa chuỗi nhạy cảm thành phong bì bảo vệ Base64 có tiền tố phiên bản.
    /// </summary>
    /// <param name="plainText">Chuỗi văn bản thuần cần bảo vệ.</param>
    /// <returns>Chuỗi phong bì đã mã hóa an toàn.</returns>
    string Protect(string? plainText);

    /// <summary>
    /// Giải mã chuỗi phong bì trở lại văn bản gốc. Tự động tương thích với chuỗi chưa mã hóa bản cũ.
    /// </summary>
    /// <param name="cipherText">Chuỗi phong bì mã hóa hoặc chuỗi bản cũ.</param>
    /// <returns>Văn bản thuần ban đầu.</returns>
    string Unprotect(string? cipherText);

    /// <summary>
    /// Kiểm tra một chuỗi có mang định dạng phong bì mã hóa hợp lệ hay không.
    /// </summary>
    /// <param name="value">Chuỗi cần kiểm tra.</param>
    /// <returns>True nếu chuỗi là phong bì đã mã hóa, ngược lại False.</returns>
    bool IsProtected(string? value);
}
