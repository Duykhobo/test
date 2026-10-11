namespace LegacyVault.Domain.Identity;

/// <summary>Giới hạn dữ liệu tài khoản, khớp schema database.</summary>
public static class UserLimits
{
    /// <summary>Độ dài email tối đa.</summary>
    public const int Email = 256;

    /// <summary>Độ dài tên tối đa.</summary>
    public const int FullName = 200;

    /// <summary>Độ dài số điện thoại tối đa.</summary>
    public const int Phone = 20;

    /// <summary>Độ dài hash mật khẩu tối đa.</summary>
    public const int PasswordHash = 512;
}

/// <summary>Thông báo lỗi invariant của tài khoản.</summary>
public static class UserMessages
{
    /// <summary>Vai trò không thuộc enum được hỗ trợ.</summary>
    public const string InvalidRole = "Vai trò tài khoản không hợp lệ.";

    /// <summary>Trạng thái không thuộc enum được hỗ trợ.</summary>
    public const string InvalidStatus = "Trạng thái tài khoản không hợp lệ.";
}
