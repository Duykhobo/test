using LegacyVault.Application.Features.Identity;
using LegacyVault.Infrastructure.Security;

namespace LegacyVault.Api.Common;

/// <summary>
/// Ghi / xóa cookie chứa token. Việc của tầng HTTP nên nằm ở Api, không ở service.
/// </summary>
/// <remarks>
/// Ý nghĩa từng cờ cookie:
/// - HttpOnly: JavaScript (document.cookie) KHÔNG đọc được → chống XSS đánh cắp token.
/// - Secure: chỉ gửi qua HTTPS. Tắt ở Development vì FE gọi http://localhost:5000.
/// - SameSite=Lax: trình duyệt không gửi cookie khi trang web KHÁC tự POST tới API của ta
///   → chống CSRF. localhost:5173 và localhost:5000 vẫn được coi là cùng site (cổng không tính).
/// - Path: giới hạn đường dẫn mà cookie được gửi kèm.
/// </remarks>
public sealed class AuthCookieWriter(IHostEnvironment environment)
{
    public void Append(HttpResponse response, AuthResult result)
    {
        response.Cookies.Append(
            AuthCookieNames.AccessToken,
            result.AccessToken,
            BuildOptions(AuthCookieNames.AccessTokenPath, result.AccessTokenExpiresAt));

        response.Cookies.Append(
            AuthCookieNames.RefreshToken,
            result.RefreshToken,
            // Không "Ghi nhớ đăng nhập" → cookie phiên (Expires = null), mất khi đóng trình duyệt.
            BuildOptions(AuthCookieNames.RefreshTokenPath, result.IsPersistent ? result.RefreshTokenExpiresAt : null));
    }

    public void Clear(HttpResponse response)
    {
        // Xóa cookie phải dùng ĐÚNG Path lúc tạo, nếu không trình duyệt coi là cookie khác.
        response.Cookies.Delete(AuthCookieNames.AccessToken, BuildOptions(AuthCookieNames.AccessTokenPath, null));
        response.Cookies.Delete(AuthCookieNames.RefreshToken, BuildOptions(AuthCookieNames.RefreshTokenPath, null));
    }

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
