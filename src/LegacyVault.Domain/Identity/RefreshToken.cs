using LegacyVault.Domain.Common;

namespace LegacyVault.Domain.Identity;

/// <summary>
/// Một "phiên đăng nhập" lưu ở server. Mỗi thiết bị đăng nhập có một chuỗi refresh token.
/// </summary>
/// <remarks>
/// Ý tưởng:
/// - Access token (JWT) sống ngắn (15 phút) và KHÔNG lưu DB → server không thu hồi được.
/// - Refresh token sống dài (7 ngày) và CÓ lưu DB → đăng xuất = đánh dấu Revoked.
/// - Xoay vòng: mỗi lần dùng refresh token để lấy token mới, token cũ bị revoke
///   và trỏ tới token mới (ReplacedByTokenId). Tất cả token cùng một lần đăng nhập
///   chung một FamilyId.
/// - Nếu ai đó dùng lại một token ĐÃ bị revoke → nhiều khả năng token bị đánh cắp
///   → revoke cả family, buộc đăng nhập lại.
/// - Ta chỉ lưu HASH của token. Lộ DB cũng không lấy được token thật để dùng.
/// </remarks>
public sealed class RefreshToken : Entity
{
    private RefreshToken() { }

    public Guid UserId { get; private set; }
    public User User { get; private set; } = null!;

    /// <summary>SHA-256 của token gốc, dạng hex.</summary>
    public string TokenHash { get; private set; } = null!;
    public Guid FamilyId { get; private set; }

    /// <summary>true = người dùng chọn "Ghi nhớ đăng nhập" → cookie sống qua khi tắt trình duyệt.</summary>
    public bool IsPersistent { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }

    public static RefreshToken Create(
        Guid userId,
        string tokenHash,
        Guid familyId,
        bool isPersistent,
        DateTimeOffset now,
        TimeSpan lifetime)
    {
        return new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            FamilyId = familyId,
            IsPersistent = isPersistent,
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime),
        };
    }

    public bool IsRevoked => RevokedAt is not null;
    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;
    public bool IsActive(DateTimeOffset now) => !IsRevoked && !IsExpired(now);

    /// <summary>Thu hồi token. Gọi nhiều lần vẫn an toàn (idempotent).</summary>
    public void Revoke(DateTimeOffset now, Guid? replacedByTokenId = null)
    {
        if (IsRevoked) return;
        RevokedAt = now;
        ReplacedByTokenId = replacedByTokenId;
    }
}
