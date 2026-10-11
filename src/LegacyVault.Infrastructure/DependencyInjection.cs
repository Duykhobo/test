using System.Text;
using LegacyVault.Application.Common.Interfaces;
using LegacyVault.Infrastructure.Persistence;
using LegacyVault.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace LegacyVault.Infrastructure;

/// <summary>Đăng ký persistence, security và JWT authentication của Infrastructure.</summary>
public static class DependencyInjection
{
    private const string ConnectionStringName = "Default";
    private const string MissingConnectionStringMessage = "Thiếu ConnectionStrings:Default trong appsettings.";
    private const string InvalidIssuerMessage = "Jwt:Issuer không được rỗng hoặc chỉ chứa khoảng trắng.";
    private const string InvalidAudienceMessage = "Jwt:Audience không được rỗng hoặc chỉ chứa khoảng trắng.";
    private const string InvalidAccessLifetimeMessage = "Jwt:AccessTokenMinutes phải lớn hơn zero.";
    private const string InvalidRefreshLifetimeMessage = "Jwt:RefreshTokenDays phải dương và nằm trong phạm vi TimeSpan.";
    private static readonly string InvalidSigningKeyMessage = FormattableString.Invariant(
        $"Jwt:SigningKey phải dài ít nhất {JwtOptions.MinimumSigningKeyBytes} byte UTF-8. Đặt khóa ngẫu nhiên bằng user-secrets cho project src/LegacyVault.Api.");

    /// <summary>Đăng ký DbContext, đồng hồ, các implementation security và authentication.</summary>
    /// <param name="services">Service collection của ứng dụng.</param>
    /// <param name="configuration">Cấu hình connection string và JWT.</param>
    /// <returns>Service collection sau khi đăng ký Infrastructure.</returns>
    /// <exception cref="InvalidOperationException">Thiếu connection string bắt buộc.</exception>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // ---- Đồng hồ hệ thống (test có thể thay bằng đồng hồ giả) ----
        services.AddSingleton(TimeProvider.System);

        // ---- Cơ sở dữ liệu ----
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(MissingConnectionStringMessage);

        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
        // Ai xin IAppDbContext sẽ nhận đúng AppDbContext của request hiện tại.
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // ---- Bảo mật ----
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        AddJwtAuthentication(services, configuration);

        return services;
    }

    /// <summary>Bind JWT options và đăng ký validator đọc access token từ header hoặc cookie.</summary>
    /// <param name="services">Service collection cần đăng ký authentication.</param>
    /// <param name="configuration">Nguồn cấu hình section JWT.</param>
    private static void AddJwtAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        // Options pattern + kiểm tra ngay lúc khởi động: thiếu khóa thì app dừng với thông báo rõ ràng,
        // thay vì chạy được rồi mới lỗi khi có người đăng nhập.
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.SigningKey) &&
                     Encoding.UTF8.GetByteCount(o.SigningKey) >= JwtOptions.MinimumSigningKeyBytes,
                InvalidSigningKeyMessage)
            .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer), InvalidIssuerMessage)
            .Validate(o => !string.IsNullOrWhiteSpace(o.Audience), InvalidAudienceMessage)
            .Validate(o => o.AccessTokenMinutes > 0, InvalidAccessLifetimeMessage)
            .Validate(
                o => o.RefreshTokenDays > 0 &&
                     o.RefreshTokenDays <= TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerDay,
                InvalidRefreshLifetimeMessage)
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Cấu hình JwtBearer SAU khi đã có JwtOptions (lấy qua DI, không đọc config thủ công).
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;

                // Giữ nguyên tên claim "sub", "role" (mặc định .NET đổi sang URL dài khó dùng).
                bearer.MapInboundClaims = false;

                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = JwtTokenService.CreateSigningKey(jwt.SigningKey),
                    ValidateLifetime = true,
                    ClockSkew = JwtOptions.ValidationClockSkew,
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                    RoleClaimType = JwtClaimTypes.Role, // để [Authorize(Roles = "ADMIN")] hoạt động
                };

                bearer.Events = new JwtBearerEvents
                {
                    // Mặc định JwtBearer đọc header "Authorization: Bearer xxx".
                    // FE của nhóm dùng cookie HttpOnly nên ta lấy token từ cookie
                    // (vẫn ưu tiên header nếu có, tiện khi thử bằng Postman).
                    OnMessageReceived = context =>
                    {
                        var hasHeader = context.Request.Headers.Authorization.Count > 0;
                        if (!hasHeader &&
                            context.Request.Cookies.TryGetValue(AuthCookieNames.AccessToken, out var token))
                        {
                            context.Token = token;
                        }
                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorization();
    }
}
