using LegacyVault.Api.Common;
using LegacyVault.Application.Features.Identity;
using LegacyVault.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace LegacyVault.Api.Controllers.V1;

/// <summary>
/// Đăng nhập / làm mới phiên / xem phiên / đăng xuất.
/// URL khớp với FE: src/features/auth/api/authService.ts và src/app/providers/AuthBootstrap.tsx
/// </summary>
[Route("api/v1/auth")]
public sealed class AuthController(IAuthService authService, AuthCookieWriter cookies) : ApiControllerBase
{
    /// <summary>POST /api/v1/auth/login</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
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
    [HttpPost("refresh")]
    [AllowAnonymous]
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
    [HttpGet("session")]
    [Authorize] // không có access token hợp lệ → middleware trả 401, không vào tới đây
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
    /// </remarks>
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] LogoutRequest? request,
        CancellationToken ct)
    {
        Request.Cookies.TryGetValue(AuthCookieNames.RefreshToken, out var refreshToken);

        await authService.LogoutAsync(refreshToken, request?.RevokeAllDevices ?? false, ct);

        cookies.Clear(Response);
        return NoContent(); // 204: thành công, không có nội dung
    }
}
