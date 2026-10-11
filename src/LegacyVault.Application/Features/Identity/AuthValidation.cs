namespace LegacyVault.Application.Features.Identity;

/// <summary>Giới hạn và thông báo validation cho hợp đồng Auth hiện tại.</summary>
public static class AuthValidation
{
    /// <summary>Độ dài email tối đa, khớp cột Users.Email.</summary>
    public const int EmailMaxLength = 256;

    /// <summary>Giới hạn mật khẩu đầu vào; không áp policy đăng ký lên đăng nhập.</summary>
    public const int PasswordMaxLength = 128;

    /// <summary>Thông báo thiếu email.</summary>
    public const string EmailRequired = "Email là bắt buộc.";

    /// <summary>Thông báo email sai định dạng.</summary>
    public const string EmailInvalid = "Email không hợp lệ.";

    /// <summary>Thông báo email vượt giới hạn.</summary>
    public const string EmailTooLong = "Email vượt quá độ dài cho phép.";

    /// <summary>Thông báo thiếu mật khẩu.</summary>
    public const string PasswordRequired = "Mật khẩu là bắt buộc.";

    /// <summary>Thông báo mật khẩu vượt giới hạn.</summary>
    public const string PasswordTooLong = "Mật khẩu vượt quá độ dài cho phép.";
}
