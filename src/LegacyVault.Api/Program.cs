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

app.UseRateLimiter();
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
