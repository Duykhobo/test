namespace LegacyVault.Domain.Common;

/// <summary>
/// Loại lỗi. Lớp Api dựa vào đây để chọn HTTP status code (Domain không biết gì về HTTP).
/// </summary>
public enum ErrorType
{
    Validation,   // 400
    Unauthorized, // 401
    Forbidden,    // 403
    NotFound,     // 404
    Conflict,     // 409
    Locked,       // 423
}

/// <summary>
/// Một lỗi nghiệp vụ "dự đoán được" (sai mật khẩu, tài khoản bị khóa...).
/// Ta TRẢ VỀ Error thay vì throw exception, vì exception chỉ dành cho lỗi bất ngờ.
/// </summary>
/// <param name="Code">Mã UPPER_SNAKE_CASE để FE so sánh, ví dụ INVALID_CREDENTIALS.</param>
/// <param name="Message">Thông điệp tiếng Việt cho người dùng.</param>
/// <param name="Type">Loại lỗi.</param>
/// <param name="Metadata">Dữ liệu kèm theo, ví dụ attemptsRemaining. Sẽ nằm ở cấp cao nhất của JSON lỗi.</param>
public sealed record Error(
    string Code,
    string Message,
    ErrorType Type,
    IReadOnlyDictionary<string, object?>? Metadata = null);
