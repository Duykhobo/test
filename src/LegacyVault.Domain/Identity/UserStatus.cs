namespace LegacyVault.Domain.Identity;

/// <summary>Trạng thái tài khoản được server quản lý khi kiểm tra điều kiện mở phiên.</summary>
/// <remarks>Active không thay thế kiểm tra mật khẩu, lockout hoặc challenge bắt buộc.</remarks>
public enum UserStatus
{
    /// <summary>Đã đăng ký nhưng chưa xác minh email (dùng khi làm chức năng đăng ký).</summary>
    Pending = 0,
    /// <summary>Được phép đăng nhập.</summary>
    Active = 1,
    /// <summary>Bị Admin vô hiệu hóa.</summary>
    Inactive = 2,
}
