using LegacyVault.Application.Features.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace LegacyVault.Application;

/// <summary>
/// Đăng ký các service của lớp Application vào DI container.
/// Program.cs chỉ cần gọi builder.Services.AddApplication().
/// </summary>
public static class DependencyInjection
{
    /// <summary>Đăng ký các implementation use case của Application với lifetime scoped.</summary>
    /// <param name="services">Service collection của ứng dụng.</param>
    /// <returns>Service collection sau khi đăng ký use case.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Scoped = mỗi HTTP request một instance, dùng chung DbContext của request đó.
        // Thêm service mới: thêm một dòng, giữ thứ tự alphabet để tránh conflict khi merge.
        services.AddScoped<IAuthService, AuthService>();

        return services;
    }
}
