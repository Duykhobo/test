using LegacyVault.Application.Features.Identity;
using LegacyVault.Infrastructure.Security;

namespace LegacyVault.Api.Common;

/// <summary>
/// Ghi / xóa cookie chứa token. Việc của tầng HTTP nên nằm ở Api, không ở service.
/// </summary>
/// <remarks>
/// Ý nghĩa từng cờ cookie:
/// - HttpOnly: JavaScript không đọc được cookie; XSS vẫn có thể gửi request bằng phiên hiện tại.
/// - Secure: chỉ gửi qua HTTPS. Tắt ở Development vì FE gọi http://localhost:5000.
/// - SameSite=Lax: hạn chế gửi cookie trong request cross-site; không thay thế toàn bộ CSRF policy.
///   localhost:5173 và localhost:5000 cùng scheme vẫn là cùng site, dù khác origin.
/// - Path: giới hạn đường dẫn gửi cookie; không phải cơ chế phân quyền.
/// </remarks>
/// <param name="environment">Môi trường chạy dùng để chọn cờ Secure.</param>
public sealed class AuthCookieWriter(IHostEnvironment environment)
{
    /// <summary>Ghi access cookie và refresh cookie từ kết quả use case đã commit thành công.</summary>
    /// <param name="response">HTTP response nhận các header Set-Cookie.</param>
    /// <param name="result">Kết quả nội bộ chứa token; không serialize trực tiếp ra body.</param>
    /// <remarks>
    /// Access cookie có hạn theo access token. Refresh cookie chỉ có Expires khi IsPersistent là true.
    /// Cookie phiên có thể được trình duyệt khôi phục; server vẫn phải kiểm tra hạn và revocation.
    /// </remarks>
    public void Append(HttpResponse response, AuthResult result)
    {
        response.Cookies.Append(
            AuthCookieNames.AccessToken,
            result.AccessToken,
            BuildOptions(AuthCookieNames.AccessTokenPath, result.AccessTokenExpiresAt));

        response.Cookies.Append(
            AuthCookieNames.RefreshToken,
            result.RefreshToken,
            // Không ghi nhớ đăng nhập: dùng cookie phiên, không đặt thời hạn lưu phía trình duyệt.
            BuildOptions(AuthCookieNames.RefreshTokenPath, result.IsPersistent ? result.RefreshTokenExpiresAt : null));
    }

    /// <summary>Gửi cookie xóa với cùng tên và path được dùng khi ghi cookie.</summary>
    /// <param name="response">HTTP response nhận các header xóa cookie.</param>
    /// <remarks>Chỉ xóa cookie phía client; việc thu hồi phiên ở server thuộc use case theo policy.</remarks>
    public void Clear(HttpResponse response)
    {
        // Xóa cookie phải dùng ĐÚNG Path lúc tạo, nếu không trình duyệt coi là cookie khác.
        response.Cookies.Delete(AuthCookieNames.AccessToken, BuildOptions(AuthCookieNames.AccessTokenPath, null));
        response.Cookies.Delete(AuthCookieNames.RefreshToken, BuildOptions(AuthCookieNames.RefreshTokenPath, null));
    }

    /// <summary>Tạo tùy chọn cookie thống nhất cho thao tác ghi và xóa.</summary>
    /// <param name="path">Đường dẫn được gửi cookie, lấy từ AuthCookieNames.</param>
    /// <param name="expires">Thời điểm hết hạn phía trình duyệt; null cho cookie phiên.</param>
    /// <returns>Tùy chọn HttpOnly, SameSite Lax và Secure ngoài Development.</returns>
    private CookieOptions BuildOptions(string path, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = !environment.IsDevelopment(),
        SameSite = SameSiteMode.Lax,
        Path = path,
        Expires = expires,
        IsEssential = true,
    };
}
