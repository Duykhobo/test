using LegacyVault.Application.Common.Interfaces;
using LegacyVault.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LegacyVault.Infrastructure.Persistence;

/// <summary>
/// DbContext thật của EF Core, cài đặt IAppDbContext mà service dùng.
/// </summary>
/// <param name="options">Cấu hình provider và connection được đăng ký qua DI.</param>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IAppDbContext
{
    /// <inheritdoc />
    public DbSet<User> Users => Set<User>();

    /// <inheritdoc />
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <inheritdoc />
    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        => Database.BeginTransactionAsync(cancellationToken);

    /// <summary>Áp dụng các mapping entity được khai báo trong assembly Infrastructure.</summary>
    /// <param name="modelBuilder">Builder cấu hình model EF Core.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Tự tìm mọi class IEntityTypeConfiguration<T> trong project này (thư mục Configurations/)
        // → mỗi entity có file cấu hình riêng, không dồn hết vào đây.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
