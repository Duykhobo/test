using System.ComponentModel.DataAnnotations;
using LegacyVault.Domain.Identity;

namespace LegacyVault.Application.Features.Identity;

// DTO (Data Transfer Object) = hình dạng dữ liệu đi qua API.
// Tách riêng khỏi entity để: (1) không lộ PasswordHash, (2) đổi DB không làm vỡ hợp đồng với FE.
// Tên property khớp với file FE: src/features/auth/model/auth.types.ts và src/entities/user/model/user.types.ts

/// <summary>Body của POST /api/v1/auth/login.</summary>
/// <remarks>
/// [Required], [EmailAddress]... là DataAnnotations. Nhờ [ApiController],
/// ASP.NET tự kiểm tra và trả 400 trước khi vào controller — ta không phải viết if.
/// </remarks>
public sealed class LoginRequest
{
    [Required(ErrorMessage = "Email là bắt buộc.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu là bắt buộc.")]
    [MaxLength(128)]
    public string Password { get; init; } = string.Empty;

    public bool RememberMe { get; init; }
}

/// <summary>Body (không bắt buộc) của POST /api/v1/auth/logout.</summary>
public sealed class LogoutRequest
{
    /// <summary>true = đăng xuất khỏi mọi thiết bị.</summary>
    public bool RevokeAllDevices { get; init; }
}

/// <summary>Thông tin người dùng trả về cho FE (khớp interface User bên FE).</summary>
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
    // Map tay (nhóm không dùng AutoMapper): rõ ràng, dễ debug, IDE tìm được chỗ dùng.
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

/// <summary>Body trả về khi đăng nhập thành công (khớp AuthSession bên FE, trừ token).</summary>
/// <remarks>
/// Token KHÔNG nằm trong body: chúng được đặt vào cookie HttpOnly,
/// JavaScript không đọc được → mã độc XSS không lấy cắp được token.
/// </remarks>
public sealed record AuthSessionDto(Guid UserId, int ExpiresIn, UserDto User);

/// <summary>Body của GET /api/v1/auth/session (FE AuthBootstrap đọc data.user).</summary>
public sealed record CurrentSessionDto(UserDto User);

/// <summary>
/// Kết quả nội bộ giữa service và controller: có token để controller ghi vào cookie.
/// Không bao giờ serialize nguyên object này ra JSON.
/// </summary>
public sealed record AuthResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    bool IsPersistent,
    AuthSessionDto Session);
