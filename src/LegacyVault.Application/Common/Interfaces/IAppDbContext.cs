using LegacyVault.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Application.Common.Interfaces;

/// <summary>
/// "Cổng" vào cơ sở dữ liệu mà service nhìn thấy.
/// </summary>
/// <remarks>
/// Service phụ thuộc vào interface này, KHÔNG phụ thuộc AppDbContext (nằm ở Infrastructure).
/// Đây là Dependency Inversion: lớp trong định nghĩa interface, lớp ngoài cài đặt.
/// Theo quyết định của nhóm: không viết Repository riêng, DbSet chính là repository.
/// Mỗi module mới chỉ cần thêm một dòng DbSet ở đây.
/// </remarks>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
