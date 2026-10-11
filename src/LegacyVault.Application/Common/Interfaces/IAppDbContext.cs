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
    /// <summary>Tập tài khoản để truy vấn và theo dõi thay đổi bằng EF Core.</summary>
    DbSet<User> Users { get; }

    /// <summary>Tập bản ghi refresh token; chỉ lưu hash của token gốc.</summary>
    DbSet<RefreshToken> RefreshTokens { get; }

    /// <summary>Lưu các thay đổi đang được DbContext theo dõi vào database.</summary>
    /// <param name="cancellationToken">Token hủy thao tác lưu.</param>
    /// <returns>Số state entries đã được ghi vào database.</returns>
    /// <exception cref="DbUpdateException">Không thể ghi thay đổi vào database.</exception>
    /// <exception cref="DbUpdateConcurrencyException">Phát hiện xung đột theo cấu hình concurrency của model.</exception>
    /// <exception cref="OperationCanceledException">Thao tác bị hủy.</exception>
    /// <remarks>
    /// Một lần lưu không tự bảo vệ truy vấn trước đó khỏi race condition.
    /// Use case phải bổ sung transaction/concurrency control theo nghiệp vụ;
    /// model hiện tại chưa cấu hình concurrency token.
    /// </remarks>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
