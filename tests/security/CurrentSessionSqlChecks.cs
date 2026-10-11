#:project ../../src/LegacyVault.Infrastructure/LegacyVault.Infrastructure.csproj
#:property PublishAot=false

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LegacyVault.Application.Common.Interfaces;
using LegacyVault.Application.Features.Identity;
using LegacyVault.Domain.Identity;
using LegacyVault.Infrastructure.Persistence;
using LegacyVault.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

/// <summary>Integration checks session trên database development đã có migration.</summary>
internal static class CurrentSessionSqlChecks
{
    private const string ConnectionVariable = "LEGACYVAULT_TEST_CONNECTION_STRING";

    /// <summary>Tạo fixture trong transaction và rollback cả khi check thất bại.</summary>
    public static async Task Main()
    {
        string connection = Environment.GetEnvironmentVariable(ConnectionVariable)
            ?? throw new InvalidOperationException($"Set {ConnectionVariable} to a development database before running.");
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connection).Options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var tokens = new JwtTokenService(Options.Create(new JwtOptions
            {
                SigningKey = new string('k', JwtOptions.MinimumSigningKeyBytes)
            }), TimeProvider.System);
            var principal = new FixtureCurrentUser();
            var service = new AuthService(db, new Argon2PasswordHasher(), tokens, principal, TimeProvider.System);

            foreach (UserStatus status in Enum.GetValues<UserStatus>())
            {
                var user = User.Create($"session-{Guid.NewGuid():N}@example.test", "Session fixture",
                    "fixture-hash-not-used-for-login", UserRole.Owner, status, DateTimeOffset.UtcNow);
                db.Users.Add(user);
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                principal.UserId = user.Id;

                var result = await service.GetCurrentUserAsync(CancellationToken.None);
                if (status == UserStatus.Active)
                {
                    if (!result.IsSuccess || result.Value != UserDto.From(user))
                    {
                        throw new InvalidOperationException("Active user DTO does not match persisted user.");
                    }
                }
                else if (!result.IsFailure || result.Error != AuthErrors.SessionExpired)
                {
                    throw new InvalidOperationException("Non-active user accepted by current session.");
                }

                if (db.ChangeTracker.Entries().Any())
                {
                    throw new InvalidOperationException("Current session unexpectedly tracks entities.");
                }

                db.Users.Remove(user);
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                var deleted = await service.GetCurrentUserAsync(CancellationToken.None);
                if (!deleted.IsFailure || deleted.Error != AuthErrors.SessionExpired)
                {
                    throw new InvalidOperationException("Deleted user accepted by current session.");
                }
            }

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try
            {
                await service.GetCurrentUserAsync(cancelled.Token);
                throw new InvalidOperationException("Database query ignored cancellation.");
            }
            catch (OperationCanceledException)
            {
                // Cancellation phải propagate ra khỏi use case.
            }
        }
        finally
        {
            await transaction.RollbackAsync();
        }

        Console.WriteLine("PASS: SQL session status, DTO, deleted user, no tracking and cancellation; fixtures rolled back.");
    }

    /// <summary>Principal xác thực với ID fixture có thể thay đổi giữa các case.</summary>
    private sealed class FixtureCurrentUser : ICurrentUser
    {
        /// <inheritdoc />
        public bool IsAuthenticated => true;

        /// <inheritdoc />
        public Guid? UserId { get; set; }
    }
}
