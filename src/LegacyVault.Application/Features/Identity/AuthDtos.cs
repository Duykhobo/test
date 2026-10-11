using System.ComponentModel.DataAnnotations;
using LegacyVault.Domain.Identity;

namespace LegacyVault.Application.Features.Identity;

// DTO (Data Transfer Object) = hình dạng dữ liệu đi qua API.
// Tách riêng khỏi entity để: (1) không lộ PasswordHash, (2) đổi DB không làm vỡ hợp đồng với FE.
// Đây là contract của backend mẫu; DTO FE legacy cần được đối chiếu trước tích hợp.

/// <summary>Body của POST /api/v1/auth/login.</summary>
/// <remarks>
/// [Required], [EmailAddress]... là DataAnnotations. Nhờ [ApiController],
/// ASP.NET tự kiểm tra và trả 400 trước khi vào controller — ta không phải viết if.
/// </remarks>
public sealed class LoginRequest
{
    /// <summary>Email đăng nhập; service chịu trách nhiệm chuẩn hóa trước khi truy vấn.</summary>
    [Required(ErrorMessage = AuthValidation.EmailRequired)]
    [EmailAddress(ErrorMessage = AuthValidation.EmailInvalid)]
    [MaxLength(AuthValidation.EmailMaxLength, ErrorMessage = AuthValidation.EmailTooLong)]
    public string Email { get; init; } = string.Empty;

    /// <summary>Mật khẩu đầu vào, chỉ dùng để xác thực và không được ghi log.</summary>
    [Required(ErrorMessage = AuthValidation.PasswordRequired)]
    [MaxLength(AuthValidation.PasswordMaxLength, ErrorMessage = AuthValidation.PasswordTooLong)]
    public string Password { get; init; } = string.Empty;

    /// <summary>Cho phép refresh cookie tồn tại qua lần đóng trình duyệt.</summary>
    public bool RememberMe { get; init; }
}

/// <summary>Body (không bắt buộc) của POST /api/v1/auth/logout.</summary>
public sealed class LogoutRequest
{
    /// <summary>true = đăng xuất khỏi mọi thiết bị.</summary>
    public bool RevokeAllDevices { get; init; }
}

/// <summary>Thông tin người dùng công khai; không chứa mật khẩu hoặc token.</summary>
/// <param name="Id">Định danh tài khoản.</param>
/// <param name="Email">Email đã chuẩn hóa.</param>
/// <param name="FullName">Tên hiển thị.</param>
/// <param name="Phone">Số điện thoại tùy chọn.</param>
/// <param name="Role">Vai trò do server quyết định, dạng chữ hoa.</param>
/// <param name="Status">Trạng thái tài khoản dạng chữ hoa.</param>
/// <param name="CreatedAt">Thời điểm tạo tài khoản.</param>
/// <param name="UpdatedAt">Thời điểm cập nhật gần nhất hoặc null.</param>
public sealed record UserDto(
    Guid Id,
    string Email,
    string FullName,
    string? Phone,
    string Role,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt)
{
    /// <summary>Chuyển entity sang DTO công khai theo contract backend mẫu.</summary>
    /// <param name="user">Entity người dùng đã tải từ database.</param>
    /// <returns>DTO không chứa PasswordHash hoặc dữ liệu token.</returns>
    public static UserDto From(User user) => new(
        user.Id,
        user.Email,
        user.FullName,
        user.Phone,
        user.Role.ToString().ToUpperInvariant(),   // Owner → "OWNER"
        user.Status.ToString().ToUpperInvariant(), // Active → "ACTIVE"
        user.CreatedAt,
        user.UpdatedAt);
}

/// <summary>Body trả về khi đăng nhập hoặc refresh thành công.</summary>
/// <remarks>
/// Token không nằm trong body; controller ghi chúng vào cookie HttpOnly.
/// HttpOnly hạn chế JavaScript đọc cookie, không ngăn XSS gửi request bằng phiên hiện tại.
/// </remarks>
/// <param name="UserId">Định danh tài khoản của phiên.</param>
/// <param name="ExpiresIn">Thời gian sống access token, tính bằng giây tại thời điểm cấp.</param>
/// <param name="User">Thông tin người dùng công khai.</param>
public sealed record AuthSessionDto(Guid UserId, int ExpiresIn, UserDto User);

/// <summary>Body của GET /api/v1/auth/session (FE AuthBootstrap đọc data.user).</summary>
/// <param name="User">Thông tin người dùng sau khi use case xác nhận phiên hợp lệ.</param>
public sealed record CurrentSessionDto(UserDto User);

/// <summary>
/// Kết quả nội bộ giữa service và controller: có token để controller ghi vào cookie.
/// Không bao giờ serialize nguyên object này ra JSON.
/// </summary>
/// <param name="AccessToken">JWT nguyên bản chỉ dùng để ghi cookie.</param>
/// <param name="AccessTokenExpiresAt">Thời điểm hết hạn access token.</param>
/// <param name="RefreshToken">Refresh token gốc; database chỉ lưu hash.</param>
/// <param name="RefreshTokenExpiresAt">Thời điểm hết hạn refresh token ở server.</param>
/// <param name="IsPersistent">Cho phép cookie refresh có hạn lưu phía trình duyệt.</param>
/// <param name="Session">DTO session công khai trả về trong response body.</param>
public sealed record AuthResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    bool IsPersistent,
    AuthSessionDto Session);
