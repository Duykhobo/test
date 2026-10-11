namespace LegacyVault.Api.Common;

/// <summary>
/// Vỏ bọc cho response thành công có body: field data chứa DTO công khai.
/// </summary>
/// <remarks>
/// Response lỗi dùng ProblemDetails; HTTP 204 không có envelope.
/// Frontend phải bóc field data theo contract đã thống nhất trước khi dùng DTO.
/// </remarks>
/// <typeparam name="T">Kiểu DTO công khai, không chứa token hoặc hash bí mật.</typeparam>
/// <param name="Data">Dữ liệu mà use case cho phép trả về client.</param>
public sealed record ApiResponse<T>(T Data);
