using LegacyVault.Api.Extensions;
using LegacyVault.Application;
using LegacyVault.Infrastructure;
using LegacyVault.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// ===== 1. Đăng ký service vào DI container (theo từng lớp) =====
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiServices(builder.Configuration);

var app = builder.Build();

// ===== 2. Pipeline middleware: THỨ TỰ RẤT QUAN TRỌNG, request đi qua từ trên xuống =====
app.UseExceptionHandler();   // bọc ngoài cùng để bắt lỗi của mọi middleware phía sau
app.UseStatusCodePages();    // 401/403/404 rỗng → tự thành ProblemDetails JSON

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRouting();           // xác định endpoint trước các policy phụ thuộc endpoint
app.UseCors(ApiServiceExtensions.FrontendCorsPolicy); // trước Authentication để preflight OPTIONS không bị 401
app.UseAuthentication();     // đọc cookie/JWT → điền HttpContext.User

// TODO: [BẢN THIẾT KẾ THỰC THI - DEVELOPER BLUEPRINT]
// 1. [MỤC TIÊU & NGHIỆP VỤ]: Lắp limiter và kiểm tra CSRF trước khi Auth use case chạy.
// 2. [INPUT & OUTPUT]: HttpContext + endpoint metadata + policy đã đăng ký -> tiếp tục hoặc từ chối.
// 3. [CÁC BƯỚC TUẦN TỰ]:
//    - Hoàn thiện đăng ký trong AddApiServices và metadata policy ở AuthController.
//    - Thêm UseRateLimiter sau routing; đặt sau authentication nếu partition cần principal.
//    - Đặt CSRF sau authentication/authorization theo cơ chế đã chọn, trước controller mutation.
//    - Nếu dùng UseAntiforgery, đặt sau UseAuthorization và bảo đảm MVC endpoint thực sự validate;
//      middleware đơn lẻ không tự bảo vệ mọi MVC action. Kiểm tra filter/metadata tương ứng.
//    - Xử lý trusted forwarded headers trước HTTPS/routing nếu triển khai sau proxy.
// 4. [HÀM / THƯ VIỆN]: ASP.NET Core middleware, UseRateLimiter, antiforgery/filter theo policy.
// 5. [ĐIỀU KIỆN BIÊN & NGOẠI LỆ]: OPTIONS preflight, anonymous login/refresh,
//    cookie thiếu, origin sai/thiếu và reverse proxy. Không dùng role header làm bằng chứng quyền.
//    Kiểm tra 429/Retry-After và CSRF reject trước khi gọi service; hiện các guard này chưa triển khai.
app.UseAuthorization();      // kiểm tra [Authorize]

app.MapControllers();

// ===== 3. Chuẩn bị DB cho môi trường dev =====
if (app.Environment.IsDevelopment())
{
    await app.Services.InitializeDatabaseAsync();
}

app.Run();

// Để sau này project test tích hợp (WebApplicationFactory<Program>) truy cập được.
/// <summary>Entry point công khai để integration test truy cập ứng dụng.</summary>
public partial class Program;
