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
    /// <summary>ID parse từ claim sub của principal hiện tại; null nếu thiếu hoặc sai định dạng.</summary>
    /// <remarks>
    /// Bên gọi phải kiểm tra IsAuthenticated trước khi dùng ID làm bằng chứng danh tính.
    /// ID không tự chứng minh tài khoản còn active hoặc có quyền với đối tượng yêu cầu.
    /// </remarks>
    Guid? UserId { get; }

    /// <summary>Principal hiện tại có identity đã được authentication middleware xác thực.</summary>
    /// <remarks>False nếu không có HTTP context hoặc identity chưa xác thực.</remarks>
    bool IsAuthenticated { get; }
}
