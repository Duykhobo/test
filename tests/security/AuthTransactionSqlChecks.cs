#:project ../../src/LegacyVault.Infrastructure/LegacyVault.Infrastructure.csproj
#:property PublishAot=false

using System;
using System.Data;
using System.Threading.Tasks;
using LegacyVault.Application.Common.Interfaces;
using LegacyVault.Domain.Identity;
using LegacyVault.Infrastructure.Persistence;
using LegacyVault.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

/// <summary>Kiểm tra transaction qua contract Application trên SQL Server development.</summary>
internal static class AuthTransactionSqlChecks
{
    private const string ConnectionVariable = "LEGACYVAULT_TEST_CONNECTION_STRING";

    /// <summary>Ghi hai bảng rồi kiểm tra dispose transaction chưa commit rollback cả hai.</summary>
    public static async Task Main()
    {
        string connection = Environment.GetEnvironmentVariable(ConnectionVariable)
            ?? throw new InvalidOperationException($"Set {ConnectionVariable} to a development database before running.");
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connection).Options);
        IAppDbContext db = context;
        await CheckRollbackAsync(context, db, useSerializable: false);
        await CheckRollbackAsync(context, db, useSerializable: true);
        Console.WriteLine("PASS: default and Serializable transactions roll back user and refresh token writes.");
    }

    /// <summary>Kiểm tra isolation thực tế và rollback của từng overload transaction.</summary>
    /// <param name="context">Context SQL Server development.</param>
    /// <param name="db">Contract Application cần kiểm chứng.</param>
    /// <param name="useSerializable">Chọn overload có isolation level rõ ràng.</param>
    private static async Task CheckRollbackAsync(AppDbContext context, IAppDbContext db, bool useSerializable)
    {
        var now = DateTimeOffset.UtcNow;
        var user = User.Create($"transaction-{Guid.NewGuid():N}@example.test", "Transaction fixture",
            "fixture-hash-not-used-for-login", UserRole.Owner, UserStatus.Active, now);
        var tokens = new JwtTokenService(Options.Create(new JwtOptions
        {
            SigningKey = new string('k', JwtOptions.MinimumSigningKeyBytes)
        }), TimeProvider.System);
        var refresh = RefreshToken.Create(user.Id, tokens.CreateRefreshToken().Hash,
            Guid.NewGuid(), false, now, tokens.RefreshTokenLifetime);

        await using (var transaction = useSerializable
            ? await db.BeginTransactionAsync(IsolationLevel.Serializable)
            : await db.BeginTransactionAsync())
        {
            if (useSerializable && transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            {
                throw new InvalidOperationException("Requested Serializable isolation was not applied.");
            }
            db.Users.Add(user);
            db.RefreshTokens.Add(refresh);
            await db.SaveChangesAsync();
            if (!await db.Users.AnyAsync(candidate => candidate.Id == user.Id) ||
                !await db.RefreshTokens.AnyAsync(candidate => candidate.Id == refresh.Id))
            {
                throw new InvalidOperationException("Fixture writes are missing inside transaction.");
            }
            // Không commit: mô phỏng use case thoát trước commit, dispose phải rollback.
        }

        context.ChangeTracker.Clear();
        if (await db.Users.AnyAsync(candidate => candidate.Id == user.Id) ||
            await db.RefreshTokens.AnyAsync(candidate => candidate.Id == refresh.Id))
        {
            throw new InvalidOperationException("Uncommitted Auth transaction left persisted fixture data.");
        }

    }
}
