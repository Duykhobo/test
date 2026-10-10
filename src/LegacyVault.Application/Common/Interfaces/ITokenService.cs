using LegacyVault.Domain.Identity;

namespace LegacyVault.Application.Common.Interfaces;

/// <summary>Access token đã ký và thời điểm hết hạn.</summary>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>Refresh token: Value gửi cho trình duyệt, Hash lưu vào DB.</summary>
public sealed record GeneratedRefreshToken(string Value, string Hash);

/// <summary>
/// Sinh token. Application không đọc config (appsettings) nên thời hạn token
/// cũng do Infrastructure cung cấp qua interface này.
/// </summary>
public interface ITokenService
{
    TimeSpan RefreshTokenLifetime { get; }

    AccessToken CreateAccessToken(User user);
    GeneratedRefreshToken CreateRefreshToken();
    string HashRefreshToken(string value);
}
