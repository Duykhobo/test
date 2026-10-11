#:project ../../src/LegacyVault.Infrastructure/LegacyVault.Infrastructure.csproj

using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using LegacyVault.Domain.Identity;
using LegacyVault.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

/// <summary>Kiểm tra signer thật bằng khóa fixture; không đọc secrets hoặc kết nối database.</summary>
internal static class JwtTokenServiceChecks
{
    private const int ExpectedAccessMinutes = 15;
    private const int ExpectedRefreshDays = 7;
    private const int ExpectedRefreshBytes = 64;
    private const string ExpectedRole = "OWNER";

    /// <summary>Chạy checks; lỗi làm tiến trình trả exit code khác zero.</summary>
    public static async Task Main()
    {
        var options = new JwtOptions { SigningKey = new string('k', JwtOptions.MinimumSigningKeyBytes) };
        var clock = new FixtureClock();
        var service = new JwtTokenService(Options.Create(options), clock);
        var user = User.Create("owner@example.test", "Fixture", "fixture-hash",
            UserRole.Owner, UserStatus.Active, clock.GetUtcNow());
        var access = service.CreateAccessToken(user);
        var reader = new JsonWebTokenHandler();
        var jwt = reader.ReadJsonWebToken(access.Value);

        if (jwt.Alg != SecurityAlgorithms.HmacSha256 ||
            jwt.ValidTo != access.ExpiresAt.UtcDateTime ||
            access.ExpiresAt != DateTimeOffset.FromUnixTimeSeconds(clock.GetUtcNow().ToUnixTimeSeconds())
                .AddMinutes(ExpectedAccessMinutes) ||
            jwt.GetClaim(JwtRegisteredClaimNames.Sub).Value != user.Id.ToString() ||
            jwt.GetClaim(JwtClaimTypes.Role).Value != ExpectedRole ||
            !Guid.TryParse(jwt.GetClaim(JwtRegisteredClaimNames.Jti).Value, out var tokenId) ||
            tokenId == Guid.Empty ||
            jwt.GetClaim(JwtRegisteredClaimNames.Jti).Value == reader.ReadJsonWebToken(
                service.CreateAccessToken(user).Value).GetClaim(JwtRegisteredClaimNames.Jti).Value ||
            service.RefreshTokenLifetime != TimeSpan.FromDays(ExpectedRefreshDays))
        {
            throw new InvalidOperationException("JWT algorithm, claims or lifetime failed.");
        }

        var parameters = new TokenValidationParameters
        {
            ValidIssuer = options.Issuer,
            ValidAudience = options.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            // Kiểm tra chính xác exp bằng đồng hồ fixture, không kiểm tra clock skew của middleware.
            LifetimeValidator = (notBefore, expires, _, _) =>
                notBefore <= clock.GetUtcNow().UtcDateTime && expires > clock.GetUtcNow().UtcDateTime
        };

        await ExpectValidation(reader, access.Value, parameters, true);
        parameters.ValidIssuer = "wrong-issuer";
        await ExpectValidation(reader, access.Value, parameters, false);
        parameters.ValidIssuer = options.Issuer;
        parameters.ValidAudience = "wrong-audience";
        await ExpectValidation(reader, access.Value, parameters, false);
        parameters.ValidAudience = options.Audience;
        parameters.IssuerSigningKey = new SymmetricSecurityKey(
            RandomNumberGenerator.GetBytes(JwtOptions.MinimumSigningKeyBytes));
        await ExpectValidation(reader, access.Value, parameters, false);
        parameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        clock.Now = access.ExpiresAt;
        await ExpectValidation(reader, access.Value, parameters, false);

        var refresh = service.CreateRefreshToken();
        if (Base64UrlEncoder.DecodeBytes(refresh.Value).Length != ExpectedRefreshBytes ||
            refresh.Value == service.CreateRefreshToken().Value ||
            refresh.Hash != service.HashRefreshToken(refresh.Value) ||
            refresh.Hash != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refresh.Value))) ||
            refresh.Hash.Length != RefreshTokenRules.HashLength)
        {
            throw new InvalidOperationException("Refresh token generation or hash failed.");
        }

        foreach (string invalidToken in new[]
        {
            string.Empty, "   ", refresh.Value + "a", "!" + refresh.Value[1..]
        })
        {
            ExpectInvalidRefreshToken(service, invalidToken);
        }

        Console.WriteLine("PASS: JWT signature, claims, expiry and refresh token checks.");
    }

    /// <summary>So sánh kết quả xác thực thực tế với kỳ vọng của case.</summary>
    /// <param name="reader">JWT handler dùng xác thực chữ ký và claims.</param>
    /// <param name="token">JWT fixture cần xác thực.</param>
    /// <param name="parameters">Quy tắc xác thực của case.</param>
    /// <param name="expected">Kết quả hợp lệ mong đợi.</param>
    private static async Task ExpectValidation(JsonWebTokenHandler reader, string token,
        TokenValidationParameters parameters, bool expected)
    {
        if ((await reader.ValidateTokenAsync(token, parameters)).IsValid != expected)
        {
            throw new InvalidOperationException("Unexpected JWT validation result.");
        }
    }

    /// <summary>Xác nhận refresh token sai định dạng bị từ chối.</summary>
    /// <param name="service">Implementation thật cần kiểm tra.</param>
    /// <param name="token">Token không hợp lệ.</param>
    private static void ExpectInvalidRefreshToken(JwtTokenService service, string token)
    {
        try
        {
            service.HashRefreshToken(token);
        }
        catch (ArgumentException)
        {
            return;
        }

        throw new InvalidOperationException("Invalid refresh token accepted.");
    }

    /// <summary>Đồng hồ cố định để checks không phụ thuộc thời gian chạy CI.</summary>
    private sealed class FixtureClock : TimeProvider
    {
        /// <summary>Thời điểm có phần lẻ để kiểm tra JWT chuẩn hóa đến giây.</summary>
        public DateTimeOffset Now { get; set; } =
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMilliseconds(123);

        /// <summary>Trả thời điểm fixture hiện tại.</summary>
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
