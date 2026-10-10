namespace LegacyVault.Infrastructure.Security;

/// <summary>
/// Cấu hình JWT, đọc từ mục "Jwt" trong appsettings / user-secrets (Options pattern).
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = "legacyvault-api";
    public string Audience { get; init; } = "legacyvault-web";

    /// <summary>
    /// Khóa bí mật để KÝ token. Ai có khóa này là tự tạo được token Admin,
    /// nên KHÔNG commit lên git — đặt bằng: dotnet user-secrets set "Jwt:SigningKey" "..."
    /// </summary>
    public string SigningKey { get; init; } = string.Empty;

    public int AccessTokenMinutes { get; init; } = 15;
    public int RefreshTokenDays { get; init; } = 7;
}
