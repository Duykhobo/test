#:project ../../src/LegacyVault.Infrastructure/LegacyVault.Infrastructure.csproj
#:property PublishAot=false

using System;
using System.Linq;
using LegacyVault.Domain.Identity;
using LegacyVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

/// <summary>Kiểm tra metadata concurrency của User mà không kết nối SQL Server.</summary>
internal static class UserConcurrencyModelChecks
{
    private const string ExpectedColumnName = "row_version";

    /// <summary>Dựng model bằng provider SQL Server thật rồi kiểm tra version do DB sinh.</summary>
    public static void Main()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=fixture.invalid;Database=Fixture;Integrated Security=True").Options);
        var entity = db.Model.FindEntityType(typeof(User))
            ?? throw new InvalidOperationException("User mapping is missing.");
        var version = entity.GetProperties().SingleOrDefault(property => property.IsConcurrencyToken);
        if (version is null || !version.IsShadowProperty() || version.ClrType != typeof(byte[]) ||
            version.ValueGenerated != ValueGenerated.OnAddOrUpdate ||
            version.GetColumnName() != ExpectedColumnName || version.GetColumnType() != "rowversion")
        {
            throw new InvalidOperationException("User SQL Server rowversion mapping is incorrect.");
        }

        Console.WriteLine("PASS: User maps a database-generated shadow rowversion concurrency token.");
    }
}
