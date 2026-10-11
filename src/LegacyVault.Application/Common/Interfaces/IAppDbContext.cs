using LegacyVault.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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

    /// <summary>Mở transaction cho nghiệp vụ ghi nhiều bảng.</summary>
    /// <param name="cancellationToken">Token hủy thao tác mở transaction.</param>
    /// <returns>Transaction cần await using; chỉ commit sau khi lưu thành công.</returns>
    /// <remarks>Transaction chưa commit được rollback khi dispose; không tự giải quyết stale update.</remarks>
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>Lưu các thay đổi đang được DbContext theo dõi vào database.</summary>
    /// <param name="cancellationToken">Token hủy thao tác lưu.</param>
    /// <returns>Số state entries đã được ghi vào database.</returns>
    /// <exception cref="DbUpdateException">Không thể ghi thay đổi vào database.</exception>
    /// <exception cref="DbUpdateConcurrencyException">Phát hiện xung đột theo cấu hình concurrency của model.</exception>
    /// <exception cref="OperationCanceledException">Thao tác bị hủy.</exception>
    /// <remarks>
    /// Một lần lưu không tự bảo vệ truy vấn trước đó khỏi race condition.
    /// Use case phải bổ sung transaction/concurrency control theo nghiệp vụ;
    /// User được mapping concurrency token tại Infrastructure; schema cần migration tương ứng.
    /// Concurrency token chỉ phát hiện xung đột, không tự retry hoặc hợp nhất bộ đếm thất bại.
    /// Refresh token/family vẫn cần cơ chế tiêu thụ và thu hồi nguyên tử riêng.
    /// </remarks>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
