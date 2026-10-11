using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LegacyVault.Application.Common.Interfaces;
using LegacyVault.Domain.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LegacyVault.Infrastructure.Security;

/// <summary>Sinh access JWT và refresh token theo cấu hình đã được DI validate.</summary>
/// <remarks>Singleton chỉ giữ cấu hình và khóa ký; dữ liệu phiên nằm trong biến cục bộ.</remarks>
public sealed class JwtTokenService : ITokenService
{
    private const int RefreshTokenByteCount = 64;

    private static readonly int RefreshTokenLength =
        Base64Url.GetEncodedLength(RefreshTokenByteCount);

    private const string InvalidUserMessage =
        "User ID hoặc role không hợp lệ.";

    private const string InvalidRefreshTokenMessage =
        "Refresh token không đúng định dạng.";

    private const string InvalidSigningKeyMessage =
        "Khóa ký JWT không đủ số byte UTF-8.";

    private readonly JwtOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SigningCredentials _signingCredentials;

    /// <summary>Nhận cấu hình đã validate và đồng hồ của ứng dụng.</summary>
    /// <param name="options">Options được đăng ký bằng AddInfrastructure.</param>
    /// <param name="timeProvider">Đồng hồ dùng khi cấp JWT.</param>
    /// <exception cref="ArgumentNullException">Dependency là null.</exception>
    /// <exception cref="OptionsValidationException">Cấu hình không vượt qua validator đã đăng ký.</exception>
    public JwtTokenService(
        IOptions<JwtOptions> options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _options = options.Value;
        _timeProvider = timeProvider;

        _signingCredentials = new SigningCredentials(
            CreateSigningKey(_options.SigningKey),
            SecurityAlgorithms.HmacSha256);
    }

    /// <inheritdoc />
    public TimeSpan RefreshTokenLifetime =>
        TimeSpan.FromDays(_options.RefreshTokenDays);

    /// <summary>Ký access JWT cho user đã được use case cho phép mở phiên.</summary>
    /// <param name="user">User đã qua xác thực, trạng thái và challenge bắt buộc nếu có.</param>
    /// <returns>JWT và expiry khớp claim exp đến độ chính xác giây.</returns>
    /// <exception cref="ArgumentNullException">User là null.</exception>
    /// <exception cref="ArgumentException">User ID hoặc role không hợp lệ.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Expiry vượt phạm vi DateTimeOffset.</exception>
    public AccessToken CreateAccessToken(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.Id == Guid.Empty || !Enum.IsDefined(user.Role))
        {
            throw new ArgumentException(
                InvalidUserMessage,
                nameof(user));
        }

        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(
            _timeProvider.GetUtcNow().ToUnixTimeSeconds());

        DateTimeOffset expiresAt =
            now.AddMinutes(_options.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,

            Subject = new ClaimsIdentity(
            [
                new Claim(
                    JwtRegisteredClaimNames.Sub,
                    user.Id.ToString()),

                new Claim(
                    JwtRegisteredClaimNames.Jti,
                    Guid.NewGuid().ToString()),

                new Claim(
                    JwtClaimTypes.Role,
                    user.Role.ToString().ToUpperInvariant())
            ]),

            SigningCredentials = _signingCredentials
        };

        string value = new JsonWebTokenHandler()
            .CreateToken(descriptor);

        return new AccessToken(value, expiresAt);
    }

    /// <summary>Sinh refresh token bằng CSPRNG và hash đúng chuỗi sẽ gửi cookie.</summary>
    /// <returns>Token gốc và SHA-256 hexadecimal chữ hoa để lưu database.</returns>
    public GeneratedRefreshToken CreateRefreshToken()
    {
        byte[] bytes =
            RandomNumberGenerator.GetBytes(RefreshTokenByteCount);

        try
        {
            string value = Base64Url.EncodeToString(bytes);

            return new GeneratedRefreshToken(
                value,
                HashRefreshToken(value));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <summary>Băm refresh token có định dạng Base64Url do service phát hành.</summary>
    /// <param name="value">Token nguyên bản, không trim hoặc đổi casing.</param>
    /// <returns>SHA-256 hexadecimal chữ hoa khớp RefreshToken.TokenHash.</returns>
    /// <exception cref="ArgumentNullException">Token là null.</exception>
    /// <exception cref="ArgumentException">Token rỗng hoặc sai độ dài/ký tự.</exception>
    public string HashRefreshToken(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length != RefreshTokenLength ||
            !value.All(character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_'))
        {
            throw new ArgumentException(
                InvalidRefreshTokenMessage,
                nameof(value));
        }

        byte[] bytes = Encoding.UTF8.GetBytes(value);

        try
        {
            return Convert.ToHexString(
                SHA256.HashData(bytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <summary>Bọc khóa UTF-8, dùng thống nhất cho signer và JWT validator.</summary>
    /// <param name="key">Khóa ký từ cấu hình; không phải chuỗi Base64 cần decode.</param>
    /// <returns>Khóa đối xứng dùng cho HS256.</returns>
    /// <exception cref="ArgumentNullException">Khóa là null.</exception>
    /// <exception cref="ArgumentException">Khóa rỗng hoặc không đủ độ dài.</exception>
    internal static SymmetricSecurityKey CreateSigningKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        byte[] bytes = Encoding.UTF8.GetBytes(key);

        if (bytes.Length < JwtOptions.MinimumSigningKeyBytes)
        {
            CryptographicOperations.ZeroMemory(bytes);

            throw new ArgumentException(
                InvalidSigningKeyMessage,
                nameof(key));
        }

        return new SymmetricSecurityKey(bytes);
    }
}

/// <summary>Tên claim dùng thống nhất giữa signer và middleware phân quyền.</summary>
public static class JwtClaimTypes
{
    /// <summary>Claim vai trò hệ thống.</summary>
    public const string Role = "role";
}