namespace LegacyVault.Infrastructure.Security;

/// <summary>
/// Tên cookie và đường dẫn, dùng chung giữa chỗ GHI cookie (Api) và chỗ ĐỌC cookie (JwtBearer).
/// </summary>
public static class AuthCookieNames
{
    /// <summary>Tên cookie chứa access JWT, dùng khi ghi, đọc và xóa phiên phía client.</summary>
    public const string AccessToken = "lv_access";

    /// <summary>Tên cookie chứa refresh token gốc; database chỉ lưu hash của token.</summary>
    public const string RefreshToken = "lv_refresh";

    /// <summary>Path cookie access: /api và các đường dẫn con phù hợp.</summary>
    public const string AccessTokenPath = "/api";

    /// <summary>
    /// Path cookie refresh: /api/v1/auth và các đường dẫn con,
    /// bao gồm login, session, refresh và logout.
    /// </summary>
    /// <remarks>Path giới hạn việc gửi cookie; không thay thế kiểm tra quyền ở server.</remarks>
    public const string RefreshTokenPath = "/api/v1/auth";
}
