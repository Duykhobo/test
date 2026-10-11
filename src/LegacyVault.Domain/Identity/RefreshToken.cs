using LegacyVault.Domain.Common;

namespace LegacyVault.Domain.Identity;

/// <summary>Quy chuẩn dữ liệu refresh token.</summary>
public static class RefreshTokenRules
{
    /// <summary>Độ dài SHA-256 dạng hexadecimal.</summary>
    public const int HashLength = 64;

    /// <summary>Thông báo ID không hợp lệ.</summary>
    public const string InvalidId = "ID không được rỗng.";

    /// <summary>Thông báo hash sai định dạng.</summary>
    public const string InvalidHash = "Hash phải là SHA-256 dạng hexadecimal.";

    /// <summary>Thông báo token thay thế không hợp lệ.</summary>
    public const string InvalidReplacement =
        "Token thay thế không được rỗng hoặc trùng token hiện tại.";
}

/// <summary>Bản ghi refresh token; chỉ lưu hash, không lưu token gốc.</summary>
public sealed class RefreshToken : Entity
{
    /// <summary>Constructor dành cho EF Core.</summary>
    private RefreshToken() { }

    /// <summary>ID người sở hữu.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Navigation người dùng, chỉ đọc khi đã tải.</summary>
    public User User { get; private set; } = null!;

    /// <summary>SHA-256 dạng hexadecimal chữ hoa.</summary>
    public string TokenHash { get; private set; } = null!;

    /// <summary>ID nhóm token cùng phiên đăng nhập.</summary>
    public Guid FamilyId { get; private set; }

    /// <summary>Cho phép cookie tồn tại qua lần đóng trình duyệt.</summary>
    public bool IsPersistent { get; private set; }

    /// <summary>Thời điểm tạo.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Thời điểm hết hạn.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Thời điểm thu hồi, null nếu chưa thu hồi.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>ID token thay thế khi rotation.</summary>
    public Guid? ReplacedByTokenId { get; private set; }

    /// <summary>Token đã được thu hồi.</summary>
    public bool IsRevoked => RevokedAt is not null;

    /// <summary>Tạo bản ghi refresh token chưa được lưu vào database.</summary>
    /// <param name="userId">ID người sở hữu.</param>
    /// <param name="tokenHash">SHA-256 dạng hexadecimal.</param>
    /// <param name="familyId">ID phiên đăng nhập.</param>
    /// <param name="isPersistent">Lựa chọn cookie persistent.</param>
    /// <param name="now">Thời điểm từ TimeProvider.</param>
    /// <param name="lifetime">Thời gian sống dương.</param>
    /// <returns>Bản ghi token mới.</returns>
    /// <exception cref="ArgumentNullException">Hash là null.</exception>
    /// <exception cref="ArgumentException">ID hoặc hash không hợp lệ.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Lifetime không dương hoặc thời điểm hết hạn bị tràn.
    /// </exception>
    public static RefreshToken Create(
        Guid userId,
        string tokenHash,
        Guid familyId,
        bool isPersistent,
        DateTimeOffset now,
        TimeSpan lifetime)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                RefreshTokenRules.InvalidId,
                nameof(userId));
        }

        if (familyId == Guid.Empty)
        {
            throw new ArgumentException(
                RefreshTokenRules.InvalidId,
                nameof(familyId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        if (tokenHash.Length != RefreshTokenRules.HashLength ||
            !tokenHash.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException(
                RefreshTokenRules.InvalidHash,
                nameof(tokenHash));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            lifetime,
            TimeSpan.Zero,
            nameof(lifetime));

        DateTimeOffset expiresAt = now.Add(lifetime);

        return new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash.ToUpperInvariant(),
            FamilyId = familyId,
            IsPersistent = isPersistent,
            CreatedAt = now,
            ExpiresAt = expiresAt
        };
    }

    /// <summary>Kiểm tra token đã hết hạn.</summary>
    /// <param name="now">Thời điểm kiểm tra từ TimeProvider.</param>
    /// <returns>True tại hoặc sau thời điểm hết hạn.</returns>
    public bool IsExpired(DateTimeOffset now)
    {
        return ExpiresAt <= now;
    }

    /// <summary>Kiểm tra bản ghi chưa thu hồi và chưa hết hạn.</summary>
    /// <param name="now">Thời điểm kiểm tra từ TimeProvider.</param>
    /// <returns>True nếu bản ghi còn hiệu lực.</returns>
    /// <remarks>Use case vẫn phải kiểm tra trạng thái user và family.</remarks>
    public bool IsActive(DateTimeOffset now)
    {
        return !IsRevoked && !IsExpired(now);
    }

    /// <summary>Thu hồi token; gọi lặp không ghi đè lần thu hồi đầu.</summary>
    /// <param name="now">Thời điểm thu hồi từ TimeProvider.</param>
    /// <param name="replacedByTokenId">ID token thay thế nếu rotation.</param>
    /// <exception cref="ArgumentException">ID thay thế không hợp lệ.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thời điểm thu hồi trước thời điểm tạo.
    /// </exception>
    public void Revoke(
        DateTimeOffset now,
        Guid? replacedByTokenId = null)
    {
        if (IsRevoked)
        {
            return;
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(
            now,
            CreatedAt,
            nameof(now));

        if (replacedByTokenId is { } replacement &&
            (replacement == Guid.Empty || replacement == Id))
        {
            throw new ArgumentException(
                RefreshTokenRules.InvalidReplacement,
                nameof(replacedByTokenId));
        }

        RevokedAt = now;
        ReplacedByTokenId = replacedByTokenId;
    }
}