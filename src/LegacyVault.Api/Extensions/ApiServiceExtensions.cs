using System.Text.Json;
using LegacyVault.Api.Common;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Threading.RateLimiting;
using System.Globalization;

namespace LegacyVault.Api.Extensions;

/// <summary>
/// Đăng ký những thứ thuộc tầng HTTP: controller, chuẩn lỗi, CORS.
/// </summary>
public static class ApiServiceExtensions
{
    /// <summary>Tên policy CORS dùng chung với pipeline.</summary>
    public const string FrontendCorsPolicy = "Frontend";

    private const string CorsOriginsSection = "Cors:AllowedOrigins";

    /// <summary>Đăng ký controller, validation response, lỗi toàn cục, cookie writer và CORS.</summary>
    /// <param name="services">Service collection của ứng dụng.</param>
    /// <param name="configuration">Cấu hình API và danh sách origin cho phép.</param>
    /// <returns>Service collection đã đăng ký hạ tầng HTTP.</returns>
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        // MVC antiforgery authorization filters require view-feature services, even for JSON actions.
        services.AddControllersWithViews()
            .ConfigureApiBehaviorOptions(options =>
            {
                // Khi DTO sai (thiếu email...), [ApiController] tự trả 400.
                // Ta thêm "code" để lỗi validate cũng theo đúng chuẩn chung của nhóm.
                options.InvalidModelStateResponseFactory = context =>
                {
                    // Đổi tên trường "Email" → "email" cho khớp tên field bên FE.
                    var errors = context.ModelState
                        .Where(e => e.Value is { Errors.Count: > 0 })
                        .ToDictionary(
                            e => JsonNamingPolicy.CamelCase.ConvertName(e.Key),
                            e => e.Value!.Errors.Select(x => x.ErrorMessage).ToArray());

                    var problem = new ValidationProblemDetails(errors)
                    {
                        Status = StatusCodes.Status400BadRequest,
                        Title = ApiProblemConstants.ValidationErrorTitle,
                    };
                    problem.Extensions[ApiProblemConstants.ErrorCodeExtension] = ApiProblemConstants.ValidationErrorCode;
                    return new BadRequestObjectResult(problem)
                    {
                        ContentTypes = { ApiProblemConstants.ProblemJsonContentType }
                    };
                };
            });

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddScoped<AuthCookieWriter>();
        services.AddOptions<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>()
            .Configure<IHostEnvironment>((options, environment) =>
            {
                options.HeaderName = AuthCsrfContract.HeaderName;
                options.Cookie.Name = AuthCsrfContract.CookieName;
                options.Cookie.Path = AuthCsrfContract.CookiePath;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.None
                    : CookieSecurePolicy.Always;
            });
        services.AddAntiforgery();

        // CORS: FE chạy ở localhost:5173 (Vite), API ở localhost:5000 → khác origin.
        // Trình duyệt chặn trừ khi API cho phép rõ ràng. AllowCredentials để cookie được gửi kèm
        // (FE đặt withCredentials: true). Khi có AllowCredentials thì KHÔNG được dùng AllowAnyOrigin.
        var allowedOrigins = configuration.GetSection(CorsOriginsSection).Get<string[]>() ?? [];
        services.AddCors(options => options.AddPolicy(FrontendCorsPolicy, policy => policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));

        // TODO: [BẢN THIẾT KẾ THỰC THI - DEVELOPER BLUEPRINT]
        // 1. [MỤC TIÊU & NGHIỆP VỤ]: Nghiệm thu CSRF/cookie và giới hạn Auth trên môi trường deploy.
        // 2. [INPUT & OUTPUT]: Cấu hình deploy + HTTP/browser checks -> bằng chứng bảo vệ thực tế.
        // 3. [CÁC BƯỚC TUẦN TỰ]:
        //    - Giữ named limiter và MVC antiforgery trên login/refresh/logout.
        //    - Đồng bộ FE cấp proof theo danh tính hiện tại; kiểm tra các auth transition nhiều tab.
        //    - Chốt same-site deployment, HTTPS và proxy tin cậy trước nghiệm thu production.
        //    - Audit các POST/PUT/PATCH/DELETE dùng cookie ngoài Auth trước tuyên bố CSRF toàn cục.
        // 4. [HÀM / THƯ VIỆN]: ASP.NET Core AddRateLimiter, partitioned limiter,
        //    EnableRateLimiting/RequireRateLimiting; AddAntiforgery nếu chọn antiforgery token.
        // 5. [ĐIỀU KIỆN BIÊN & NGOẠI LỆ]: NAT nhiều user, IP spoof qua proxy,
        //    proxy headers chỉ tin proxy cấu hình; limiter phải chạy trước Argon2.
        //    CORS và SameSite=Lax không thay thế toàn bộ CSRF policy; origin cùng site vẫn cần xét.
        //    Thiếu/sai CSRF proof phải từ chối trước mutation, cancellation phải propagate.
        //    Limiter và antiforgery đã đăng ký; HTTP checks không thay thế browser acceptance.
        services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, ct) =>
{
    if (context.Lease.TryGetMetadata(
        MetadataName.RetryAfter,
        out TimeSpan retryAfter))
    {
        int seconds = checked((int)Math.Max(
            1,
            Math.Ceiling(retryAfter.TotalSeconds)));

        context.HttpContext.Response.Headers.RetryAfter =
            seconds.ToString(CultureInfo.InvariantCulture);
    }

    return ValueTask.CompletedTask;
};

    var policies = new[]
    {
        (AuthRateLimitPolicies.Login,
            AuthRateLimitPolicies.LoginPermitLimit),
        (AuthRateLimitPolicies.Refresh,
            AuthRateLimitPolicies.SessionPermitLimit),
        (AuthRateLimitPolicies.Logout,
            AuthRateLimitPolicies.SessionPermitLimit)
    };

    foreach (var (policyName, permitLimit) in policies)
    {
        options.AddPolicy(policyName, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.MapToIPv6()
                    ?? IPAddress.IPv6None,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = AuthRateLimitPolicies.Window,
                    QueueLimit = AuthRateLimitPolicies.QueueLimit,
                    AutoReplenishment = true
                }));
    }
});

        return services;
    }
}
