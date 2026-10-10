namespace LegacyVault.Application.Common.Interfaces;

/// <summary>
/// Ai đang gọi API, lấy từ JWT đã được xác thực.
/// </summary>
/// <remarks>
/// Quy tắc quan trọng: KHÔNG BAO GIỜ nhận userId từ body/query của FE.
/// FE có thể gửi bất kỳ id nào; chỉ token đã ký mới đáng tin.
/// </remarks>
public interface ICurrentUser
{
    Guid? UserId { get; }
    bool IsAuthenticated { get; }
}
