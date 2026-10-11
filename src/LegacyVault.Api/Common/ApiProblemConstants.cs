namespace LegacyVault.Api.Common;

/// <summary>Hằng số dùng chung cho response HTTP và lỗi ProblemDetails của API.</summary>
public static class ApiProblemConstants
{
    /// <summary>Content type của response JSON thông thường.</summary>
    public const string JsonContentType = "application/json";

    /// <summary>Content type của response ProblemDetails.</summary>
    public const string ProblemJsonContentType = "application/problem+json";

    /// <summary>Tên extension chứa mã lỗi nghiệp vụ.</summary>
    public const string ErrorCodeExtension = "code";

    /// <summary>Mã lỗi khi request không vượt qua model validation.</summary>
    public const string ValidationErrorCode = "VALIDATION_ERROR";

    /// <summary>Thông báo chung cho response lỗi validation theo field.</summary>
    public const string ValidationErrorTitle = "Dữ liệu gửi lên không hợp lệ.";

    /// <summary>Mã lỗi công khai cho exception không được xử lý ở use case.</summary>
    public const string InternalErrorCode = "INTERNAL_ERROR";

    /// <summary>Thông báo an toàn cho lỗi server, không chứa chi tiết exception.</summary>
    public const string InternalErrorTitle = "Hệ thống đang gặp sự cố. Vui lòng thử lại sau.";

    /// <summary>Message template ghi lỗi không dự kiến, không ghi query string hoặc request body.</summary>
    public const string UnhandledExceptionLogMessage = "Lỗi không xử lý được tại {Path}";
}
