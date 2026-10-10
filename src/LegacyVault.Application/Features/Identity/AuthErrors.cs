using LegacyVault.Domain.Common;

namespace LegacyVault.Application.Features.Identity;

/// <summary>
/// Toàn bộ lỗi của module Identity ở một chỗ, để FE và BE thống nhất mã lỗi.
/// </summary>
public static class AuthErrors
{
    public static Error InvalidCredentials(int? attemptsRemaining = null) => new(
        "INVALID_CREDENTIALS",
        "Email hoặc mật khẩu chưa đúng.",
        ErrorType.Unauthorized,
        // FE (LoginForm.tsx) đọc error.response.data.attemptsRemaining để hiện "Bạn còn N lần thử".
        attemptsRemaining is null
            ? null
            : new Dictionary<string, object?> { ["attemptsRemaining"] = attemptsRemaining });

    public static Error AccountLocked(int remainingSeconds) => new(
        "ACCOUNT_LOCKED",
        "Tài khoản tạm khóa do nhập sai mật khẩu nhiều lần. Vui lòng thử lại sau.",
        ErrorType.Locked,
        new Dictionary<string, object?>
        {
            ["attemptsRemaining"] = 0,
            ["remainingSeconds"] = remainingSeconds,
        });

    public static readonly Error AccountNotActive = new(
        "ACCOUNT_NOT_ACTIVE",
        "Tài khoản chưa được kích hoạt hoặc đã bị vô hiệu hóa.",
        ErrorType.Forbidden);

    public static readonly Error SessionExpired = new(
        "SESSION_EXPIRED",
        "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.",
        ErrorType.Unauthorized);
}
