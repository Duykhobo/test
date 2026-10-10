using LegacyVault.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Common;

/// <summary>
/// Lớp cha của mọi controller: biến Result/Error thành HTTP response theo một chuẩn chung.
/// </summary>
/// <remarks>
/// [ApiController] bật: tự trả 400 khi DTO không hợp lệ, tự suy ra [FromBody]...
/// Controller nên MỎNG: nhận request → gọi 1 method service → đổi kết quả thành HTTP.
/// </remarks>
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    protected IActionResult OkData<T>(T data) => Ok(new ApiResponse<T>(data));

    /// <summary>
    /// Error → ProblemDetails, ví dụ:
    /// { "status": 401, "title": "Email hoặc mật khẩu chưa đúng.", "code": "INVALID_CREDENTIALS", "attemptsRemaining": 3 }
    /// </summary>
    protected IActionResult Fail(Error error)
    {
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Locked => StatusCodes.Status423Locked,
            _ => StatusCodes.Status500InternalServerError,
        };

        // Factory của ASP.NET tự thêm "type" và "traceId" (để tra log khi có sự cố).
        var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, status, title: error.Message);
        problem.Extensions["code"] = error.Code;

        if (error.Metadata is not null)
        {
            foreach (var (key, value) in error.Metadata)
            {
                problem.Extensions[key] = value;
            }
        }

        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };
    }
}
