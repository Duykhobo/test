using LegacyVault.Application.Common.Interfaces;
using LegacyVault.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Persistence;

/// <summary>
/// DbContext thật của EF Core, cài đặt IAppDbContext mà service dùng.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IAppDbContext
{
    // Mỗi DbSet ứng với một bảng. "=> Set<T>()" để không cần setter.
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Tự tìm mọi class IEntityTypeConfiguration<T> trong project này (thư mục Configurations/)
        // → mỗi entity có file cấu hình riêng, không dồn hết vào đây.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
