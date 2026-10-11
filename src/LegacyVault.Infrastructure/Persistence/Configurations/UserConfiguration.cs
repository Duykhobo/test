using LegacyVault.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegacyVault.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mô tả bảng Users cho EF Core: độ dài cột, index, cách lưu enum.
/// Domain không có attribute EF nào — mọi chi tiết lưu trữ nằm ở đây.
/// </summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    private const string RowVersionPropertyName = "RowVersion";
    private const string RowVersionColumnName = "row_version";

    /// <summary>Cấu hình schema User, dùng chung giới hạn với Domain.</summary>
    /// <param name="builder">Builder mapping entity User.</param>
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);

        // Shadow property: SQL Server sinh version; Domain/DTO không cần mang chi tiết persistence.
        // EF đưa version gốc vào WHERE của UPDATE/DELETE để phát hiện ghi đè đồng thời.
        builder.Property<byte[]>(RowVersionPropertyName)
            .HasColumnName(RowVersionColumnName)
            .IsRowVersion();

        builder.Property(u => u.Email).HasMaxLength(UserLimits.Email).IsRequired();
        // Unique index: DB tự chặn 2 tài khoản trùng email, kể cả khi code có bug.
        builder.HasIndex(u => u.Email).IsUnique();

        builder.Property(u => u.FullName).HasMaxLength(UserLimits.FullName).IsRequired();
        builder.Property(u => u.Phone).HasMaxLength(UserLimits.Phone);
        builder.Property(u => u.PasswordHash).HasMaxLength(UserLimits.PasswordHash).IsRequired();

        // Lưu enum dạng chữ ("Admin") thay vì số (2): đọc DB trực tiếp dễ hiểu,
        // và chèn thêm giá trị enum ở giữa không làm sai dữ liệu cũ.
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(32);
        builder.Property(u => u.Status).HasConversion<string>().HasMaxLength(32);
    }
}
