using LegacyVault.Domain.Identity;

namespace LegacyVault.Application.Common.Interfaces;

/// <summary>Access token đã ký và thời điểm hết hạn.</summary>
/// <param name="Value">JWT đã ký, chỉ dùng nội bộ để ghi cookie.</param>
/// <param name="ExpiresAt">Thời điểm hết hạn khớp với claim exp của JWT.</param>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>Refresh token: Value gửi cho trình duyệt, Hash lưu vào DB.</summary>
/// <param name="Value">Bearer secret ngẫu nhiên; không lưu vào database hoặc log.</param>
/// <param name="Hash">SHA-256 dạng hexadecimal chữ hoa của Value.</param>
public sealed record GeneratedRefreshToken(string Value, string Hash);

/// <summary>
/// Sinh token. Application không đọc config (appsettings) nên thời hạn token
/// cũng do Infrastructure cung cấp qua interface này.
/// </summary>
public interface ITokenService
{
    /// <summary>Thời gian sống refresh token từ cấu hình đã được kiểm tra.</summary>
    /// <remarks>Use case chịu trách nhiệm chính sách absolute/sliding expiry của family.</remarks>
    TimeSpan RefreshTokenLifetime { get; }

    /// <summary>Ký access JWT cho tài khoản đã được use case cho phép mở phiên.</summary>
    /// <param name="user">User đã qua xác thực, trạng thái và challenge bắt buộc nếu có.</param>
    /// <returns>JWT đã ký và thời điểm hết hạn tương ứng.</returns>
    /// <remarks>Không tự xác thực mật khẩu hoặc lấy role từ request/header.</remarks>
    AccessToken CreateAccessToken(User user);

    /// <summary>Sinh refresh token bằng CSPRNG và hash để lưu vào database.</summary>
    /// <returns>Token gốc để ghi cookie và hash để persistence lưu trữ.</returns>
    GeneratedRefreshToken CreateRefreshToken();

    /// <summary>Băm refresh token để tra cứu bản ghi mà không lưu token gốc.</summary>
    /// <param name="value">Chuỗi token nguyên bản, không trim hoặc đổi casing.</param>
    /// <returns>SHA-256 dạng hexadecimal chữ hoa, khớp RefreshToken.TokenHash.</returns>
    /// <remarks>Hash khớp không chứng minh token còn active; use case phải kiểm tra bản ghi.</remarks>
    string HashRefreshToken(string value);
}
