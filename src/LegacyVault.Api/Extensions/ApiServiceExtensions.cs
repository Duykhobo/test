using System.Text.Json;
using LegacyVault.Api.Common;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Extensions;

/// <summary>
/// Đăng ký những thứ thuộc tầng HTTP: controller, chuẩn lỗi, CORS.
/// </summary>
public static class ApiServiceExtensions
{
    public const string FrontendCorsPolicy = "Frontend";

    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers()
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
                        Title = "Dữ liệu gửi lên không hợp lệ.",
                    };
                    problem.Extensions["code"] = "VALIDATION_ERROR";
                    return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
                };
            });

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddScoped<AuthCookieWriter>();

        // CORS: FE chạy ở localhost:5173 (Vite), API ở localhost:5000 → khác origin.
        // Trình duyệt chặn trừ khi API cho phép rõ ràng. AllowCredentials để cookie được gửi kèm
        // (FE đặt withCredentials: true). Khi có AllowCredentials thì KHÔNG được dùng AllowAnyOrigin.
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options => options.AddPolicy(FrontendCorsPolicy, policy => policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));

        return services;
    }
}
