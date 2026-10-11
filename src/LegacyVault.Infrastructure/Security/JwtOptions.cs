namespace LegacyVault.Infrastructure.Security;

/// <summary>
/// Cấu hình JWT, đọc từ mục "Jwt" trong appsettings / user-secrets (Options pattern).
/// </summary>
public sealed class JwtOptions
{
    /// <summary>Tên section chứa cấu hình JWT.</summary>
    public const string SectionName = "Jwt";

    /// <summary>Số byte UTF-8 tối thiểu của khóa ký đối xứng theo policy hiện tại.</summary>
    public const int MinimumSigningKeyBytes = 32;

    /// <summary>Dung sai thời gian dùng khi kiểm tra hạn JWT.</summary>
    public static readonly TimeSpan ValidationClockSkew = TimeSpan.FromSeconds(30);

    /// <summary>Issuer được signer phát hành và JwtBearer chấp nhận.</summary>
    public string Issuer { get; init; } = "legacyvault-api";

    /// <summary>Audience được signer phát hành và JwtBearer chấp nhận.</summary>
    public string Audience { get; init; } = "legacyvault-web";

    /// <summary>
    /// Khóa bí mật để KÝ token. Ai có khóa này là tự tạo được token Admin,
    /// nên KHÔNG commit lên git — đặt bằng: dotnet user-secrets set "Jwt:SigningKey" "..."
    /// </summary>
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>Thời gian sống access token, tính bằng phút.</summary>
    public int AccessTokenMinutes { get; init; } = 15;

    /// <summary>Thời gian sống refresh token, tính bằng ngày.</summary>
    public int RefreshTokenDays { get; init; } = 7;
}
