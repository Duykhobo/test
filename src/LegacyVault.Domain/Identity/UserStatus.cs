namespace LegacyVault.Domain.Identity;

public enum UserStatus
{
    /// <summary>Đã đăng ký nhưng chưa xác minh email (dùng khi làm chức năng đăng ký).</summary>
    Pending = 0,
    /// <summary>Được phép đăng nhập.</summary>
    Active = 1,
    /// <summary>Bị Admin vô hiệu hóa.</summary>
    Inactive = 2,
}
