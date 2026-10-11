using LegacyVault.Application.Common.Interfaces;
using LegacyVault.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LegacyVault.Infrastructure.Persistence;

/// <summary>
/// Chỉ chạy ở môi trường Development: áp migration rồi tạo sẵn tài khoản để thử đăng nhập.
/// </summary>
public static class DbInitializer
{
    /// <summary>Mật khẩu fixture công khai, chỉ dùng cho tài khoản demo ở Development.</summary>
    public const string DemoPassword = "Password123!";

    private const string DemoSeedCompletedMessage = "Đã tạo tài khoản demo cho môi trường Development.";

    /// <summary>Áp migration và seed tài khoản demo nếu database chưa có user.</summary>
    /// <param name="services">Service provider của ứng dụng Development.</param>
    /// <returns>Task hoàn tất sau khi migration và seed thành công.</returns>
    /// <remarks>Bên gọi phải giới hạn ở Development; lỗi database được truyền lên và không ghi mật khẩu vào log.</remarks>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services)
    {
        // Service Scoped (DbContext) phải lấy trong một scope; lúc khởi động chưa có HTTP request nào.
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var hasher = sp.GetRequiredService<IPasswordHasher>();
        var time = sp.GetRequiredService<TimeProvider>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DbInitializer));

        // Tạo DB (nếu chưa có) và chạy các migration chưa áp dụng.
        await db.Database.MigrateAsync();

        if (await db.Users.AnyAsync())
        {
            return;
        }

        var now = time.GetUtcNow();
        var hash = hasher.Hash(DemoPassword);
        db.Users.AddRange(
            User.Create("owner@legacyvault.local", "Nguyễn Văn Chủ", hash, UserRole.Owner, UserStatus.Active, now),
            User.Create("admin@legacyvault.local", "Quản Trị Viên", hash, UserRole.Admin, UserStatus.Active, now),
            User.Create("inactive@legacyvault.local", "Tài Khoản Khóa", hash, UserRole.Owner, UserStatus.Inactive, now));
        await db.SaveChangesAsync();

        logger.LogInformation(DemoSeedCompletedMessage);
    }
}
