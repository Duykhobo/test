using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Common;

/// <summary>
/// Bắt mọi exception KHÔNG lường trước (mất kết nối DB, bug null...) ở một chỗ duy nhất.
/// </summary>
/// <remarks>
/// Ghi log đầy đủ ở server, nhưng chỉ trả cho FE thông báo chung chung + traceId.
/// Không bao giờ gửi stack trace ra ngoài: nó lộ cấu trúc code cho kẻ tấn công.
/// </remarks>
/// <param name="logger">Logger ghi exception ở server.</param>
/// <param name="problemDetailsService">Dịch vụ ghi response ProblemDetails.</param>
public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    /// <summary>Ghi log exception và thử trả response lỗi server với thông báo an toàn.</summary>
    /// <param name="httpContext">HTTP context của request gặp lỗi.</param>
    /// <param name="exception">Exception cần ghi vào log server, không serialize ra response.</param>
    /// <param name="cancellationToken">Token hủy do exception handler middleware cung cấp.</param>
    /// <returns>True nếu dịch vụ ProblemDetails đã ghi response; false để middleware xử lý tiếp.</returns>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, ApiProblemConstants.UnhandledExceptionLogMessage, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = ApiProblemConstants.InternalErrorTitle,
                Extensions = { [ApiProblemConstants.ErrorCodeExtension] = ApiProblemConstants.InternalErrorCode },
            },
        });
    }
}
