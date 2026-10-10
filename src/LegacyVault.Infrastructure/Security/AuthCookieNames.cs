namespace LegacyVault.Infrastructure.Security;

/// <summary>
/// Tên cookie và đường dẫn, dùng chung giữa chỗ GHI cookie (Api) và chỗ ĐỌC cookie (JwtBearer).
/// </summary>
public static class AuthCookieNames
{
    public const string AccessToken = "lv_access";
    public const string RefreshToken = "lv_refresh";

    /// <summary>Access token được gửi kèm mọi request tới /api.</summary>
    public const string AccessTokenPath = "/api";

    /// <summary>
    /// Refresh token CHỈ được trình duyệt gửi tới /api/v1/auth (refresh, logout),
    /// không đi theo mọi request khác → giảm cơ hội bị lộ.
    /// </summary>
    public const string RefreshTokenPath = "/api/v1/auth";
}
