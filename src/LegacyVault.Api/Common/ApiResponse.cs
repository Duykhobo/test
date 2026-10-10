namespace LegacyVault.Api.Common;

/// <summary>
/// Vỏ bọc cho mọi response THÀNH CÔNG: { "data": ... } (theo ADR-0002 của nhóm).
/// </summary>
/// <remarks>
/// FE đọc response.data rồi lại .data (axios interceptor bóc lớp 1, authService bóc lớp 2),
/// nên body bắt buộc phải có trường "data".
/// Response LỖI không dùng vỏ này mà dùng ProblemDetails (chuẩn RFC 9457).
/// </remarks>
public sealed record ApiResponse<T>(T Data);
