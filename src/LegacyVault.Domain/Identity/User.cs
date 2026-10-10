using LegacyVault.Domain.Common;

namespace LegacyVault.Domain.Identity;

/// <summary>
/// Tài khoản người dùng.
/// </summary>
/// <remarks>
/// Để ý: mọi setter đều là "private set". Bên ngoài KHÔNG thể viết user.FailedLoginCount = 0;
/// mà phải gọi method có tên nghiệp vụ (RegisterFailedLogin, RegisterSuccessfulLogin).
/// Nhờ vậy luật "sai 5 lần thì khóa 15 phút" chỉ nằm ở ĐÚNG MỘT chỗ — ngay trong entity.
/// </remarks>
public sealed class User : Entity
{
    public const int MaxFailedLoginAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    // EF Core cần một constructor không tham số để tạo object khi đọc từ DB.
    // Để private nên code của ta không dùng nhầm được.
    private User() { }

    public string Email { get; private set; } = null!;
    public string FullName { get; private set; } = null!;
    public string? Phone { get; private set; }
    public string PasswordHash { get; private set; } = null!;
    public UserRole Role { get; private set; }
    public UserStatus Status { get; private set; }

    public int FailedLoginCount { get; private set; }
    public DateTimeOffset? LockoutEndsAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }

    /// <summary>
    /// Factory method: cách DUY NHẤT để tạo User mới, đảm bảo object luôn hợp lệ ngay từ đầu.
    /// </summary>
    /// <param name="passwordHash">Mật khẩu ĐÃ BĂM. Domain không bao giờ thấy mật khẩu gốc.</param>
    public static User Create(
        string email,
        string fullName,
        string passwordHash,
        UserRole role,
        UserStatus status,
        DateTimeOffset now,
        string? phone = null)
    {
        return new User
        {
            Email = NormalizeEmail(email),
            FullName = fullName.Trim(),
            Phone = phone,
            PasswordHash = passwordHash,
            Role = role,
            Status = status,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Chuẩn hóa email để so sánh: bỏ khoảng trắng, chữ thường.
    /// "  An@Gmail.com " và "an@gmail.com" phải là cùng một tài khoản.
    /// </summary>
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public bool IsLockedOut(DateTimeOffset now) => LockoutEndsAt is { } end && end > now;

    /// <summary>Số giây còn lại đến khi mở khóa (0 nếu không bị khóa).</summary>
    public int LockoutRemainingSeconds(DateTimeOffset now) =>
        IsLockedOut(now) ? (int)Math.Ceiling((LockoutEndsAt!.Value - now).TotalSeconds) : 0;

    /// <summary>
    /// Ghi nhận một lần nhập sai mật khẩu.
    /// </summary>
    /// <returns>Số lần thử còn lại. Trả 0 nghĩa là vừa bị khóa.</returns>
    public int RegisterFailedLogin(DateTimeOffset now)
    {
        // Lần khóa trước đã hết hạn → đếm lại từ đầu.
        if (LockoutEndsAt is { } end && end <= now)
        {
            LockoutEndsAt = null;
            FailedLoginCount = 0;
        }

        FailedLoginCount++;
        UpdatedAt = now;

        if (FailedLoginCount >= MaxFailedLoginAttempts)
        {
            LockoutEndsAt = now.Add(LockoutDuration);
            return 0;
        }

        return MaxFailedLoginAttempts - FailedLoginCount;
    }

    public void RegisterSuccessfulLogin(DateTimeOffset now)
    {
        FailedLoginCount = 0;
        LockoutEndsAt = null;
        LastLoginAt = now;
        UpdatedAt = now;
    }
}
