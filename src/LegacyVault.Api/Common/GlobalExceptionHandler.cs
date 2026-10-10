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
public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Lỗi không xử lý được tại {Path}", httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Hệ thống đang gặp sự cố. Vui lòng thử lại sau.",
                Extensions = { ["code"] = "INTERNAL_ERROR" },
            },
        });
    }
}
