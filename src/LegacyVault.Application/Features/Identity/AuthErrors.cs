using LegacyVault.Domain.Common;

namespace LegacyVault.Application.Features.Identity;

/// <summary>
/// Toàn bộ lỗi của module Identity ở một chỗ, để FE và BE thống nhất mã lỗi.
/// </summary>
public static class AuthErrors
{
    /// <summary>Thông báo nội bộ cho các use case đang ở giai đoạn scaffold.</summary>
    public const string ScaffoldNotImplemented = "Auth scaffold chưa được triển khai; developer cần hoàn thiện TODO blueprint.";

    /// <summary>Lỗi chung cho email không tồn tại hoặc mật khẩu sai, không kèm bộ đếm giúp dò email.</summary>
    /// <returns>Lỗi xác thực với cấu trúc công khai đồng nhất.</returns>
    public static Error InvalidCredentials() => new(
        "INVALID_CREDENTIALS",
        "Email hoặc mật khẩu chưa đúng.",
        ErrorType.Unauthorized);

    /// <summary>Lỗi khóa tài khoản; chỉ công khai sau khi chứng minh thông tin xác thực theo policy.</summary>
    /// <param name="remainingSeconds">Số giây chờ không âm, do domain tính.</param>
    /// <returns>Lỗi khóa và metadata thời gian chờ.</returns>
    public static Error AccountLocked(int remainingSeconds) => new(
        "ACCOUNT_LOCKED",
        "Tài khoản tạm khóa do nhập sai mật khẩu nhiều lần. Vui lòng thử lại sau.",
        ErrorType.Locked,
        new Dictionary<string, object?>
        {
            ["attemptsRemaining"] = 0,
            ["remainingSeconds"] = remainingSeconds,
        });

    /// <summary>Tài khoản không active; chỉ trả sau khi chứng minh thông tin xác thực.</summary>
    public static readonly Error AccountNotActive = new(
        "ACCOUNT_NOT_ACTIVE",
        "Tài khoản chưa được kích hoạt hoặc đã bị vô hiệu hóa.",
        ErrorType.Forbidden);

    /// <summary>Phiên không còn hợp lệ; không công khai lý do token thất bại.</summary>
    public static readonly Error SessionExpired = new(
        "SESSION_EXPIRED",
        "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.",
        ErrorType.Unauthorized);
}
