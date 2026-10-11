using LegacyVault.Domain.Common;

namespace LegacyVault.Domain.Identity;

/// <summary>Khung tài khoản Auth; giữ schema hiện có, nghiệp vụ do developer triển khai.</summary>
public sealed class User : Entity
{
    /// <summary>Ngưỡng khóa của mẫu cũ; chủ module xác nhận policy trước khi triển khai.</summary>
    public const int MaxFailedLoginAttempts = 5;
    /// <summary>Thời gian khóa của mẫu cũ; chủ module xác nhận policy trước khi triển khai.</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>Constructor dành cho EF Core materialization.</summary>
    private User() { }

    /// <summary>Email được chuẩn hóa theo policy thống nhất.</summary>
    public string Email { get; private set; } = null!;
    /// <summary>Tên hiển thị.</summary>
    public string FullName { get; private set; } = null!;
    /// <summary>Số điện thoại tùy chọn.</summary>
    public string? Phone { get; private set; }
    /// <summary>Hash mật khẩu, không phải mật khẩu gốc.</summary>
    public string PasswordHash { get; private set; } = null!;
    /// <summary>Vai trò hệ thống do server kiểm soát.</summary>
    public UserRole Role { get; private set; }
    /// <summary>Trạng thái tài khoản do server kiểm soát.</summary>
    public UserStatus Status { get; private set; }
    /// <summary>Số lần xác thực thất bại đã lưu.</summary>
    public int FailedLoginCount { get; private set; }
    /// <summary>Thời điểm kết thúc khóa, null nếu không khóa.</summary>
    public DateTimeOffset? LockoutEndsAt { get; private set; }
    /// <summary>Thời điểm tạo UTC.</summary>
    public DateTimeOffset CreatedAt { get; private set; }
    /// <summary>Thời điểm cập nhật UTC.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }
    /// <summary>Thời điểm đăng nhập thành công gần nhất.</summary>
    public DateTimeOffset? LastLoginAt { get; private set; }

    /// <summary>Tạo tài khoản từ dữ liệu đã được use case cho phép.</summary>
    /// <param name="email">Email tài khoản.</param>
    /// <param name="fullName">Tên hiển thị.</param>
    /// <param name="passwordHash">Mật khẩu đã được băm.</param>
    /// <param name="role">Vai trò được server cho phép.</param>
    /// <param name="status">Trạng thái khởi tạo.</param>
    /// <param name="now">Thời điểm tạo tài khoản.</param>
    /// <param name="phone">Số điện thoại tùy chọn.</param>
    /// <returns>Đối tượng User mới, chưa lưu vào database.</returns>
    /// <exception cref="ArgumentNullException">Dữ liệu bắt buộc là null.</exception>
    /// <exception cref="ArgumentException">Dữ liệu bắt buộc rỗng hoặc enum không hợp lệ.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Dữ liệu vượt giới hạn schema.</exception>
    public static User Create(string email, string fullName, string passwordHash,
        UserRole role, UserStatus status, DateTimeOffset now, string? phone = null)
    {
        string emailNormalized = NormalizeEmail(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        string trimmedFullName = fullName.Trim();
        string? trimmedPhone = phone?.Trim();

        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            emailNormalized.Length, UserLimits.Email, nameof(email));

        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            trimmedFullName.Length, UserLimits.FullName, nameof(fullName));

        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            trimmedPhone?.Length ?? 0, UserLimits.Phone, nameof(phone));

        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            passwordHash.Length, UserLimits.PasswordHash, nameof(passwordHash));

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentException(
                UserMessages.InvalidRole,
                nameof(role));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentException(
                UserMessages.InvalidStatus,
                nameof(status));
        }

        User user = new User
        {
            Email = emailNormalized,
            FullName = trimmedFullName,
            Phone = trimmedPhone,
            PasswordHash = passwordHash,
            Role = role,
            Status = status,
            CreatedAt = now
        };
        return user;
    }

    /// <summary>Chuẩn hóa email trước khi lưu hoặc tìm tài khoản.</summary>
    /// <param name="email">Email người dùng cung cấp.</param>
    /// <returns>Email đã bỏ khoảng trắng ngoài và chuyển thành chữ thường.</returns>
    /// <exception cref="ArgumentNullException">Email là null.</exception>
    /// <exception cref="ArgumentException">Email rỗng hoặc chỉ chứa khoảng trắng.</exception>
    public static string NormalizeEmail(string email)
    {

        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        string trimmedEmail = email.Trim();
        string normalizedEmail = trimmedEmail.ToLowerInvariant();
        return normalizedEmail;
    }

    /// <summary>Kiểm tra tài khoản còn bị khóa tại thời điểm cho trước.</summary>
    /// <param name="now">Thời điểm kiểm tra từ TimeProvider.</param>
    /// <returns>True nếu thời hạn khóa chưa kết thúc.</returns>
    public bool IsLockedOut(DateTimeOffset now)
    {
        // TODO: [BẢN THIẾT KẾ THỰC THI - DEVELOPER BLUEPRINT]
        // 1. [MỤC TIÊU & NGHIỆP VỤ]: Xác định khóa còn hiệu lực, không thay đổi entity.
        // 2. [INPUT & OUTPUT]: now + LockoutEndsAt -> bool.
        // 3. [CÁC BƯỚC TUẦN TỰ]: Xét null rồi so thời điểm kết thúc với now.
        // 4. [HÀM / THƯ VIỆN]: DateTimeOffset, nullable pattern matching.
        // 5. [ĐIỀU KIỆN BIÊN & NGOẠI LỆ]: Đúng thời điểm hết khóa phải mở;
        //    không gọi đồng hồ hệ thống trực tiếp hoặc tự kéo dài khóa khi đọc.
        return LockoutEndsAt is { } end && end > now;
    }

    /// <summary>Tính số giây chờ còn lại, làm tròn lên.</summary>
    /// <param name="now">Thời điểm kiểm tra từ TimeProvider.</param>
    /// <returns>Zero nếu không khóa; số giây còn lại nếu đang khóa.</returns>
    /// <exception cref="OverflowException">Thời gian chờ vượt phạm vi int.</exception>
    public int LockoutRemainingSeconds(DateTimeOffset now)
    {
        // TODO: [BẢN THIẾT KẾ THỰC THI - DEVELOPER BLUEPRINT]
        // 1. [MỤC TIÊU & NGHIỆP VỤ]: Cung cấp thời gian chờ cho policy response.
        // 2. [INPUT & OUTPUT]: now + LockoutEndsAt -> int không âm.
        // 3. [CÁC BƯỚC TUẦN TỰ]: Kiểm tra khóa; tính chênh lệch giây, làm tròn lên theo policy.
        // 4. [HÀM / THƯ VIỆN]: IsLockedOut, TimeSpan.TotalSeconds, Math.Ceiling.
        // 5. [ĐIỀU KIỆN BIÊN & NGOẠI LỆ]: Null, hết hạn, phần lẻ và tràn int;
        //    không công khai dữ liệu này cho người chưa chứng minh thông tin xác thực.
        if(LockoutEndsAt is not { } end || end <= now)
        {
            return 0;
        }
        double remainingSeconds = (end - now).TotalSeconds;
        return checked((int)Math.Ceiling(remainingSeconds));
    }

    /// <summary>Ghi nhận lần đăng nhập sai và khóa khi đạt ngưỡng.</summary>
    /// <param name="now">Thời điểm đăng nhập sai từ TimeProvider.</param>
    /// <returns>Số lượt còn lại; zero nếu đang khóa hoặc vừa đạt ngưỡng.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thời điểm kết thúc khóa vượt phạm vi DateTimeOffset.
    /// </exception>
    public int RegisterFailedLogin(DateTimeOffset now)
    {
        // TODO: [BẢN THIẾT KẾ THỰC THI - DEVELOPER BLUEPRINT]
        // 1. [MỤC TIÊU & NGHIỆP VỤ]: Đếm thất bại và chuyển sang khóa theo policy chủ module.
        // 2. [INPUT & OUTPUT]: now + trạng thái hiện tại -> số lần còn lại, cập nhật entity.
        // 3. [CÁC BƯỚC TUẦN TỰ]: Xét khóa hiện hữu/hết hạn; reset khi cần;
        //    tăng bộ đếm, cập nhật UpdatedAt và LockoutEndsAt tại ngưỡng đã chốt.
        // 4. [HÀM / THƯ VIỆN]: IsLockedOut, MaxFailedLoginAttempts, LockoutDuration, DateTimeOffset.Add.
        // 5. [ĐIỀU KIỆN BIÊN & NGOẠI LỆ]: Đã khóa, đúng hạn mở, vượt ngưỡng/tràn;
        //    method trong RAM không ngăn lost update. Persistence phải bảo vệ concurrency khi lưu.
        if (IsLockedOut(now))
        {
            return 0;
        }
        if (LockoutEndsAt is { } end && end <= now)
        {
            FailedLoginCount = 0;
            LockoutEndsAt = null;
        }
        if (FailedLoginCount >= MaxFailedLoginAttempts - 1)
        {
            DateTimeOffset lockoutEnd = now.Add(LockoutDuration);

            FailedLoginCount = MaxFailedLoginAttempts;
            LockoutEndsAt = lockoutEnd;
            UpdatedAt = now;

            return 0;
        }

        FailedLoginCount++;
        UpdatedAt = now;

        return MaxFailedLoginAttempts - FailedLoginCount;

    }

    /// <summary>Reset trạng thái thất bại sau khi use case cho phép đăng nhập.</summary>
    /// <param name="now">Thời điểm đăng nhập thành công từ TimeProvider.</param>
    /// <remarks>
    /// Service phải hoàn tất kiểm tra mật khẩu, trạng thái, lockout và challenge
    /// trước khi gọi hàm này.
    /// </remarks>
    public void RegisterSuccessfulLogin(DateTimeOffset now)
    {
        // TODO: [BẢN THIẾT KẾ THỰC THI - DEVELOPER BLUEPRINT]
        // 1. [MỤC TIÊU & NGHIỆP VỤ]: Reset thất bại và ghi thời điểm đăng nhập thành công.
        // 2. [INPUT & OUTPUT]: now -> cập nhật entity, không trả dữ liệu.
        // 3. [CÁC BƯỚC TUẦN TỰ]: Sau xác thực/challenge/status hợp lệ ở service,
        //    reset bộ đếm/khóa; cập nhật LastLoginAt và UpdatedAt theo policy.
        // 4. [HÀM / THƯ VIỆN]: Các property entity; transaction của use case khi lưu.
        // 5. [ĐIỀU KIỆN BIÊN & NGOẠI LỆ]: Login đồng thời với thất bại/khóa;
        //    dùng concurrency control ở DB, không gọi khi tài khoản chưa đủ điều kiện cấp phiên.
        FailedLoginCount = 0;
        LockoutEndsAt = null;
        LastLoginAt = now;
        UpdatedAt = now;
    }
}
