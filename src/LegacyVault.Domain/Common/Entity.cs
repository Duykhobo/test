namespace LegacyVault.Domain.Common;

/// <summary>
/// Lớp cha của mọi entity. Mỗi entity có một Id kiểu Guid.
/// </summary>
/// <remarks>
/// ID được tạo bằng Guid.CreateVersion7(), có thành phần thời gian.
/// Không suy ra thứ tự clustered index của SQL Server chỉ từ phiên bản Guid;
/// hiệu quả index cần được đo theo provider và cấu hình database thực tế.
/// </remarks>
public abstract class Entity
{
    /// <summary>Định danh entity; được tạo khi khởi tạo và có thể được EF khôi phục từ database.</summary>
    public Guid Id { get; protected set; } = Guid.CreateVersion7();
}
