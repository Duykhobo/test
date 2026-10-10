using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LegacyVault.Application.Common.Interfaces;
using LegacyVault.Domain.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LegacyVault.Infrastructure.Security;

/// <summary>
/// Tạo access token (JWT) và refresh token (chuỗi ngẫu nhiên).
/// </summary>
/// <remarks>
/// JWT gồm 3 phần: header.payload.signature. Payload chỉ được mã hóa base64, AI CŨNG ĐỌC ĐƯỢC
/// (dán thử vào jwt.io) → không bao giờ để mật khẩu hay dữ liệu nhạy cảm trong JWT.
/// Chữ ký (HMAC-SHA256 với SigningKey) đảm bảo không ai sửa được payload.
/// </remarks>
public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_options.RefreshTokenDays);

    public AccessToken CreateAccessToken(User user)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        // Claim = một mẩu thông tin về người dùng nằm trong token.
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),   // "sub" = subject = userId
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtClaimTypes.Role, user.Role.ToString().ToUpperInvariant()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()), // id riêng của token
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                CreateSigningKey(_options.SigningKey), SecurityAlgorithms.HmacSha256),
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return new AccessToken(token, expiresAt);
    }

    public GeneratedRefreshToken CreateRefreshToken()
    {
        // 64 byte ngẫu nhiên từ bộ sinh số an toàn mật mã (KHÔNG dùng new Random()).
        var bytes = RandomNumberGenerator.GetBytes(64);
        var value = Base64UrlEncoder.Encode(bytes);
        return new GeneratedRefreshToken(value, HashRefreshToken(value));
    }

    public string HashRefreshToken(string value)
    {
        // Refresh token đã đủ ngẫu nhiên nên SHA-256 là đủ (khác với mật khẩu do người đặt).
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash); // 64 ký tự hex
    }

    internal static SymmetricSecurityKey CreateSigningKey(string key) => new(Encoding.UTF8.GetBytes(key));
}

/// <summary>Tên claim ngắn gọn ta tự quy ước (thay cho URL dài của Microsoft).</summary>
public static class JwtClaimTypes
{
    public const string Role = "role";
}
