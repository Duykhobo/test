namespace LegacyVault.Domain.Common;

/// <summary>
/// Loại lỗi. Lớp Api dựa vào đây để chọn HTTP status code (Domain không biết gì về HTTP).
/// </summary>
public enum ErrorType
{
    /// <summary>Dữ liệu đầu vào vi phạm ràng buộc.</summary>
    Validation,
    /// <summary>Thiếu hoặc không có bằng chứng xác thực hợp lệ.</summary>
    Unauthorized,
    /// <summary>Danh tính không được phép thực hiện thao tác.</summary>
    Forbidden,
    /// <summary>Không tìm thấy đối tượng được phép công khai.</summary>
    NotFound,
    /// <summary>Thao tác xung đột với trạng thái hoặc dữ liệu hiện tại.</summary>
    Conflict,
    /// <summary>Thao tác bị khóa tạm thời theo chính sách nghiệp vụ.</summary>
    Locked,
}

/// <summary>
/// Một lỗi nghiệp vụ "dự đoán được" (sai mật khẩu, tài khoản bị khóa...).
/// Ta TRẢ VỀ Error thay vì throw exception, vì exception chỉ dành cho lỗi bất ngờ.
/// </summary>
/// <param name="Code">Mã UPPER_SNAKE_CASE để FE so sánh, ví dụ INVALID_CREDENTIALS.</param>
/// <param name="Message">Thông điệp tiếng Việt cho người dùng.</param>
/// <param name="Type">Loại lỗi.</param>
/// <param name="Metadata">Metadata an toàn để công khai; API đưa vào extensions của ProblemDetails.</param>
/// <remarks>
/// Không chứa password, token, khóa ký hoặc chi tiết giúp dò tài khoản.
/// Metadata không được ghi đè các field chuẩn của ProblemDetails hoặc mã lỗi do API quản lý.
/// </remarks>
public sealed record Error(
    string Code,
    string Message,
    ErrorType Type,
    IReadOnlyDictionary<string, object?>? Metadata = null);
