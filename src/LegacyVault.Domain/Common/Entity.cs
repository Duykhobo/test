namespace LegacyVault.Domain.Common;

/// <summary>
/// Lớp cha của mọi entity. Mỗi entity có một Id kiểu Guid.
/// </summary>
/// <remarks>
/// Guid.CreateVersion7() (từ .NET 9) sinh Guid có thành phần thời gian ở đầu,
/// nên các bản ghi mới chèn vào cuối index → SQL Server ít bị phân mảnh hơn Guid.NewGuid().
/// </remarks>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();
}
