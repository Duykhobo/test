using System.Security.Claims;
using LegacyVault.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;

namespace LegacyVault.Infrastructure.Security;

/// <summary>
/// Đọc người dùng hiện tại từ HttpContext.User — thứ mà middleware JwtBearer đã điền
/// sau khi kiểm tra chữ ký và hạn của token.
/// </summary>
/// <param name="httpContextAccessor">Accessor đọc HTTP context của request hiện tại.</param>
public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    /// <summary>Principal hiện tại; null nếu đang chạy ngoài HTTP request.</summary>
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    /// <inheritdoc />
    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    /// <inheritdoc />
    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;
}
