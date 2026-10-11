#:project ../../src/LegacyVault.Infrastructure/LegacyVault.Infrastructure.csproj
#:property PublishAot=false

using System;
using System.Threading.Tasks;
using LegacyVault.Domain.Identity;
using LegacyVault.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

/// <summary>Kiểm tra stale update và reload/reapply bằng hai context trên SQL Server development.</summary>
internal static class UserConcurrencySqlChecks
{
    private const string ConnectionVariable = "LEGACYVAULT_TEST_CONNECTION_STRING";
    private const int ExpectedFailedAttempts = 2;

    /// <summary>Tái hiện thứ tự đọc/ghi xung đột có kiểm soát; fixture luôn rollback.</summary>
    public static async Task Main()
    {
        string connection = Environment.GetEnvironmentVariable(ConnectionVariable)
            ?? throw new InvalidOperationException($"Set {ConnectionVariable} to a development database before running.");
        await using var first = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connection).Options);
        await using var transaction = await first.Database.BeginTransactionAsync();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var user = User.Create($"concurrency-{Guid.NewGuid():N}@example.test", "Concurrency fixture",
                "fixture-hash-not-used-for-login", UserRole.Owner, UserStatus.Active, now);
            first.Users.Add(user);
            await first.SaveChangesAsync();
            first.ChangeTracker.Clear();

            // Chung connection/transaction để không commit fixture; context giữ snapshot riêng.
            await using var second = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(first.Database.GetDbConnection()).Options);
            await second.Database.UseTransactionAsync(transaction.GetDbTransaction());
            User firstCopy = await first.Users.SingleAsync(candidate => candidate.Id == user.Id);
            User staleCopy = await second.Users.SingleAsync(candidate => candidate.Id == user.Id);

            firstCopy.RegisterFailedLogin(now);
            await first.SaveChangesAsync();
            staleCopy.RegisterFailedLogin(now);
            try
            {
                await second.SaveChangesAsync();
                throw new InvalidOperationException("Stale update overwrote the persisted failed-login count.");
            }
            catch (DbUpdateConcurrencyException)
            {
                // Đây là retry minh họa trong check, chưa phải policy retry của LoginAsync.
                await second.Entry(staleCopy).ReloadAsync();
                staleCopy.RegisterFailedLogin(now);
                await second.SaveChangesAsync();
            }

            first.ChangeTracker.Clear();
            User persisted = await first.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == user.Id);
            if (persisted.FailedLoginCount != ExpectedFailedAttempts)
            {
                throw new InvalidOperationException("Reload/reapply lost a failed-login attempt.");
            }
        }
        finally
        {
            await transaction.RollbackAsync();
        }

        Console.WriteLine("PASS: Stale user update rejected; reload/reapply preserves both failures; fixture rolled back.");
    }
}
