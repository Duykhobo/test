using LegacyVault.Api.Common;
using LegacyVault.Application.Features.Identity;
using LegacyVault.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;

namespace LegacyVault.Api.Controllers.V1;

/// <summary>
/// Đăng nhập / làm mới phiên / xem phiên / đăng xuất.
/// Cookie-auth endpoints use MVC antiforgery validation before service execution.
/// Client integration and production browser acceptance require separate checks.
/// </summary>
[Route("api/v1/auth")]
public sealed class AuthController(IAuthService authService, AuthCookieWriter cookies,
    IAntiforgery antiforgery) : ApiControllerBase
{
    /// <summary>Issues identity-bound antiforgery proof without exposing authentication secrets.</summary>
    /// <returns>Public request token and a framework-owned HttpOnly antiforgery cookie.</returns>
    [HttpGet(AuthCsrfContract.TokenRoute)]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<ApiResponse<AuthCsrfTokenDto>>(StatusCodes.Status200OK)]
    public IActionResult Csrf()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return OkData(new AuthCsrfTokenDto(tokens.RequestToken
            ?? throw new InvalidOperationException("Antiforgery request token was not issued.")));
    }

    /// <summary>Nhận payload đăng nhập đã qua DataAnnotations và chuyển cho use case.</summary>
    /// <param name="request">Thông tin đăng nhập; không ghi log mật khẩu.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns>Session công khai và cookie khi thành công, ProblemDetails khi thất bại.</returns>
    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<AuthSessionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [EnableRateLimiting(AuthRateLimitPolicies.Login)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await authService.LoginAsync(request, ct);
        if (result.IsFailure)
        {
            return Fail(result.Error!);
        }

        cookies.Append(Response, result.Value);
        return OkData(result.Value.Session);
    }

    /// <summary>
    /// POST /api/v1/auth/refresh — đổi refresh token (trong cookie) lấy cặp token mới.
    /// FE nên gọi khi một request bị 401, rồi thử lại request đó.
    /// </summary>
    /// <param name="ct">Token hủy request.</param>
    /// <returns>Session và cookie thay thế hoặc lỗi phiên.</returns>
    [HttpPost("refresh")]
    [ValidateAntiForgeryToken]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<AuthSessionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [EnableRateLimiting(AuthRateLimitPolicies.Refresh)]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        Request.Cookies.TryGetValue(AuthCookieNames.RefreshToken, out var refreshToken);

        var result = await authService.RefreshAsync(refreshToken, ct);
        if (result.IsFailure)
        {
            cookies.Clear(Response); // phiên hỏng → xóa luôn cookie cũ
            return Fail(result.Error!);
        }

        cookies.Append(Response, result.Value);
        return OkData(result.Value.Session);
    }

    /// <summary>GET /api/v1/auth/session — FE gọi khi tải trang để biết ai đang đăng nhập.</summary>
    /// <param name="ct">Token hủy request.</param>
    /// <returns>Người dùng công khai hoặc lỗi xác thực.</returns>
    [HttpGet("session")]
    [Authorize] // không có access token hợp lệ → middleware trả 401, không vào tới đây
    [ProducesResponseType<ApiResponse<CurrentSessionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Session(CancellationToken ct)
    {
        var result = await authService.GetCurrentUserAsync(ct);
        return result.IsSuccess ? OkData(new CurrentSessionDto(result.Value)) : Fail(result.Error!);
    }

    /// <summary>
    /// POST /api/v1/auth/logout — body không bắt buộc: { "revokeAllDevices": true }.
    /// </summary>
    /// <remarks>
    /// [AllowAnonymous] vì access token có thể đã hết hạn mà người dùng vẫn phải đăng xuất được;
    /// phiên được nhận diện qua refresh token trong cookie.
    /// AllowAnonymous không cho phép logout mọi thiết bị bằng token hết hạn/revoked;
    /// use case phải xác minh quyền trước khi thu hồi các phiên khác.
    /// </remarks>
    /// <param name="request">Tùy chọn thu hồi mọi thiết bị; body có thể thiếu.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns>204 chỉ sau khi use case hoàn tất; lỗi không được coi là logout thành công.</returns>
    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(AuthRateLimitPolicies.Logout)]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] LogoutRequest? request,
        CancellationToken ct)
    {
        Request.Cookies.TryGetValue(AuthCookieNames.RefreshToken, out var refreshToken);

        var result = await authService.LogoutAsync(refreshToken, request?.RevokeAllDevices ?? false, ct);

        if (result.IsFailure)
        {
            return Fail(result.Error!);
        }

        cookies.Clear(Response);
        return NoContent();
    }
}
