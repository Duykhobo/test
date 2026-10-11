#:project ../../src/LegacyVault.Infrastructure/LegacyVault.Infrastructure.csproj
#:property PublishAot=false

using System;
using System.Threading;
using System.Threading.Tasks;
using LegacyVault.Application.Common.Interfaces;
using LegacyVault.Application.Features.Identity;
using LegacyVault.Domain.Identity;
using LegacyVault.Infrastructure.Persistence;
using LegacyVault.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

/// <summary>Kiểm tra nhánh mật khẩu sai của AuthService thật trên SQL Server development.</summary>
internal static class LoginFailureSqlChecks
{
    private const string ConnectionVariable = "LEGACYVAULT_TEST_CONNECTION_STRING";
    private const string RowVersionPropertyName = "RowVersion";
    private const int RowVersionByteCount = 8;
    private const int ExpectedMaxSaveAttempts = 3;

    /// <summary>Kiểm tra bộ đếm, khóa, stale-version và cancellation; fixture luôn rollback.</summary>
    public static async Task Main()
    {
        var hook = new StaleVersionHook();
        string connection = Environment.GetEnvironmentVariable(ConnectionVariable)
            ?? throw new InvalidOperationException($"Set {ConnectionVariable} to a development database before running.");
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connection)
            .AddInterceptors(hook).Options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var hasher = new Argon2PasswordHasher();
            var tokens = new JwtTokenService(Options.Create(new JwtOptions { SigningKey = new string('k', JwtOptions.MinimumSigningKeyBytes) }), TimeProvider.System);
            var service = new AuthService(db, hasher, tokens, new FixturePrincipal(), TimeProvider.System);
            var user = User.Create($"login-review-{Guid.NewGuid():N}@example.test", "Fixture", hasher.Hash("correct-fixture-password"), UserRole.Owner, UserStatus.Active, DateTimeOffset.UtcNow);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            var request = new LoginRequest { Email = user.Email, Password = "wrong-fixture-password" };
            var unknown = await service.LoginAsync(new LoginRequest { Email = $"missing-{Guid.NewGuid():N}@example.test", Password = request.Password }, CancellationToken.None);
            if (!unknown.IsFailure || unknown.Error?.Code != AuthErrors.InvalidCredentials().Code) throw new InvalidOperationException("Unknown account result failed.");
            for (int attempt = 1; attempt <= User.MaxFailedLoginAttempts; attempt++)
            {
                var result = await service.LoginAsync(request, CancellationToken.None);
                await db.Entry(user).ReloadAsync();
                if (!result.IsFailure || result.Error?.Code != AuthErrors.InvalidCredentials().Code || user.FailedLoginCount != attempt)
                    throw new InvalidOperationException("Wrong-password result or persisted counter failed.");
            }
            var lockedUntil = user.LockoutEndsAt;
            var locked = await service.LoginAsync(request, CancellationToken.None);
            await db.Entry(user).ReloadAsync();
            if (!locked.IsFailure || user.LockoutEndsAt != lockedUntil || user.FailedLoginCount != User.MaxFailedLoginAttempts)
                throw new InvalidOperationException("Locked account was extended or incremented.");

            var retryUser = User.Create($"retry-review-{Guid.NewGuid():N}@example.test", "Fixture", user.PasswordHash, UserRole.Owner, UserStatus.Active, DateTimeOffset.UtcNow);
            db.Users.Add(retryUser);
            await db.SaveChangesAsync();
            var retryRequest = new LoginRequest { Email = retryUser.Email, Password = request.Password };
            hook.ConflictsRemaining = 1;
            hook.SaveAttempts = 0;
            var retried = await service.LoginAsync(retryRequest, CancellationToken.None);
            await db.Entry(retryUser).ReloadAsync();
            if (!retried.IsFailure || hook.SaveAttempts != 2 || retryUser.FailedLoginCount != 1)
                throw new InvalidOperationException("Reload/retry failed or double-counted one login.");

            hook.ConflictsRemaining = ExpectedMaxSaveAttempts;
            hook.SaveAttempts = 0;
            try
            {
                await service.LoginAsync(retryRequest, CancellationToken.None);
                throw new InvalidOperationException("Final concurrency conflict was swallowed.");
            }
            catch (DbUpdateConcurrencyException)
            {
                if (hook.SaveAttempts != ExpectedMaxSaveAttempts) throw new InvalidOperationException("Retry limit failed.");
            }
            await db.Entry(retryUser).ReloadAsync();
            if (retryUser.FailedLoginCount != 1) throw new InvalidOperationException("Conflicted writes changed the stored counter.");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try { await service.LoginAsync(request, cancelled.Token); throw new InvalidOperationException("Cancellation swallowed."); }
            catch (OperationCanceledException) { }
        }
        finally { await transaction.RollbackAsync(); }
        Console.WriteLine("PASS: Actual LoginAsync unknown email, failed counter, lockout, SQL stale-version retry, retry limit and cancellation. Fixtures rolled back.");
    }

    /// <summary>Principal unused by the wrong-password branch.</summary>
    private sealed class FixturePrincipal : ICurrentUser
    {
        /// <inheritdoc />
        public bool IsAuthenticated => false;
        /// <inheritdoc />
        public Guid? UserId => null;
    }
    /// <summary>Forces controlled stale-version conflicts through the real SQL UPDATE.</summary>
    private sealed class StaleVersionHook : SaveChangesInterceptor
    {
        /// <summary>Number of saves whose original version will be stale.</summary>
        public int ConflictsRemaining { get; set; }
        /// <summary>Number of intercepted save attempts.</summary>
        public int SaveAttempts { get; set; }
        /// <summary>Changes only fixture original values before SQL execution.</summary>
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            SaveAttempts++;
            if (ConflictsRemaining > 0)
            {
                ConflictsRemaining--;
                foreach (var entry in eventData.Context!.ChangeTracker.Entries<User>())
                    if (entry.State == EntityState.Modified)
                        entry.Property(RowVersionPropertyName).OriginalValue = new byte[RowVersionByteCount];
            }
            return ValueTask.FromResult(result);
        }
    }
}
