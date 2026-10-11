#:project ../../src/LegacyVault.Infrastructure/LegacyVault.Infrastructure.csproj
#:property PublishAot=false

using System;
using System.Collections.Generic;
using System.Linq;
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
using Microsoft.IdentityModel.JsonWebTokens;

/// <summary>Kiểm tra cấp phiên và rollback trên SQL Server development; cleanup đúng fixture ID.</summary>
internal static class LoginSessionSqlChecks
{
    private const string ConnectionVariable = "LEGACYVAULT_TEST_CONNECTION_STRING";
    private const string RowVersionPropertyName = "RowVersion";
    private const int RowVersionByteCount = 8;
    private const int ExpectedMaxExpiresIn = 900;

    /// <summary>Chạy login thật, kiểm tra commit/rollback và xóa fixture trong finally.</summary>
    public static async Task Main()
    {
        var connection = Environment.GetEnvironmentVariable(ConnectionVariable) ?? throw new InvalidOperationException("Missing fixture connection.");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options;
        var hasher = new Argon2PasswordHasher();
        const string password = "correct-session-fixture-password";
        string hash = hasher.Hash(password);
        var tokens = new JwtTokenService(Options.Create(new JwtOptions { SigningKey = new string('k', JwtOptions.MinimumSigningKeyBytes) }), TimeProvider.System);
        var ids = new List<Guid>();
        try
        {
            foreach (var role in new[] { UserRole.Owner, UserRole.Verifier, UserRole.Admin })
            {
                await using var db = new AppDbContext(options);
                var user = User.Create($"grant-{Guid.NewGuid():N}@example.test", "Fixture", hash, role, UserStatus.Active, DateTimeOffset.UtcNow);
                user.RegisterFailedLogin(DateTimeOffset.UtcNow);
                ids.Add(user.Id);
                db.Users.Add(user);
                await db.SaveChangesAsync();
                bool remember = role == UserRole.Owner;
                var service = CreateService(db, tokens);
                if (role == UserRole.Admin)
                {
                    try { await service.LoginAsync(new LoginRequest { Email = user.Email, Password = password }, CancellationToken.None); throw new InvalidOperationException("Admin bypassed challenge guard."); }
                    catch (NotImplementedException) { }
                    db.ChangeTracker.Clear();
                    var admin = await db.Users.SingleAsync(candidate => candidate.Id == user.Id);
                    if (admin.LastLoginAt is not null || admin.FailedLoginCount != 1 || await db.RefreshTokens.AnyAsync(token => token.UserId == user.Id))
                        throw new InvalidOperationException("Admin challenge guard left committed session changes.");
                    continue;
                }
                var result = await service.LoginAsync(new LoginRequest { Email = user.Email, Password = password, RememberMe = remember }, CancellationToken.None);
                if (!result.IsSuccess) throw new InvalidOperationException("Valid login failed.");
                var response = result.Value;
                db.ChangeTracker.Clear();
                var persistedUser = await db.Users.SingleAsync(candidate => candidate.Id == user.Id);
                var persistedToken = await db.RefreshTokens.SingleAsync(token => token.UserId == user.Id);
                var jwt = new JsonWebTokenHandler().ReadJsonWebToken(response.AccessToken);
                if (persistedUser.FailedLoginCount != 0 || persistedUser.LastLoginAt is null ||
                    response.Session.User != UserDto.From(persistedUser) || response.Session.UserId != user.Id ||
                    persistedToken.TokenHash != tokens.HashRefreshToken(response.RefreshToken) || persistedToken.FamilyId == Guid.Empty ||
                    response.RefreshTokenExpiresAt != persistedToken.ExpiresAt || persistedToken.ExpiresAt - persistedToken.CreatedAt != tokens.RefreshTokenLifetime ||
                    persistedToken.IsPersistent != remember || response.IsPersistent != remember ||
                    response.Session.ExpiresIn <= 0 || response.Session.ExpiresIn > ExpectedMaxExpiresIn ||
                    jwt.ValidTo != response.AccessTokenExpiresAt.UtcDateTime || jwt.GetClaim(JwtClaimTypes.Role).Value != role.ToString().ToUpperInvariant())
                    throw new InvalidOperationException("Persisted session or response contract failed.");
            }
            foreach (bool staleVersion in new[] { false, true })
            {
                var builder = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection);
                if (staleVersion) builder.AddInterceptors(new StaleUserHook());
                await using var db = new AppDbContext(builder.Options);
                var user = User.Create($"grant-rollback-{Guid.NewGuid():N}@example.test", "Fixture", hash, UserRole.Owner, UserStatus.Active, DateTimeOffset.UtcNow);
                user.RegisterFailedLogin(DateTimeOffset.UtcNow);
                ids.Add(user.Id);
                db.Users.Add(user);
                await db.SaveChangesAsync();
                var service = CreateService(db, staleVersion ? tokens : new FailingTokens(tokens));
                try
                {
                    await service.LoginAsync(new LoginRequest { Email = user.Email, Password = password }, CancellationToken.None);
                    throw new InvalidOperationException("Expected session failure was swallowed.");
                }
                catch (FixtureSigningException) when (!staleVersion) { }
                catch (DbUpdateConcurrencyException) when (staleVersion) { }
                db.ChangeTracker.Clear();
                var persisted = await db.Users.SingleAsync(candidate => candidate.Id == user.Id);
                if (persisted.FailedLoginCount != 1 || persisted.LastLoginAt is not null || await db.RefreshTokens.AnyAsync(token => token.UserId == user.Id))
                    throw new InvalidOperationException("Failed session did not roll back atomically.");
            }
            Console.WriteLine("PASS: Owner/Verifier committed sessions, claims/hash/expiry/RememberMe, Admin blocked, signing failure and stale update roll back.");
        }
        finally
        {
            await using var cleanup = new AppDbContext(options);
            await using var cleanupTransaction = await cleanup.Database.BeginTransactionAsync();
            await cleanup.RefreshTokens.Where(token => ids.Contains(token.UserId)).ExecuteDeleteAsync();
            await cleanup.Users.Where(user => ids.Contains(user.Id)).ExecuteDeleteAsync();
            await cleanupTransaction.CommitAsync();
            Console.WriteLine("Fixture cleanup completed.");
        }
    }

    /// <summary>Constructs the actual AuthService for the fixture.</summary>
    private static AuthService CreateService(AppDbContext db, ITokenService tokens) => new(db, new Argon2PasswordHasher(), tokens, new FixturePrincipal(), TimeProvider.System);
    /// <summary>Unused principal for login checks.</summary>
    private sealed class FixturePrincipal : ICurrentUser
    {
        /// <inheritdoc />
        public bool IsAuthenticated => false;
        /// <inheritdoc />
        public Guid? UserId => null;
    }
    /// <summary>Controlled fixture signing failure.</summary>
    private sealed class FixtureSigningException : Exception { }
    /// <summary>Delegates refresh operations and fails access signing.</summary>
    /// <param name="inner">Real fixture token service.</param>
    private sealed class FailingTokens(ITokenService inner) : ITokenService
    {
        /// <inheritdoc />
        public TimeSpan RefreshTokenLifetime => inner.RefreshTokenLifetime;
        /// <inheritdoc />
        public GeneratedRefreshToken CreateRefreshToken() => inner.CreateRefreshToken();
        /// <inheritdoc />
        public string HashRefreshToken(string value) => inner.HashRefreshToken(value);
        /// <inheritdoc />
        public AccessToken CreateAccessToken(User user) => throw new FixtureSigningException();
    }
    /// <summary>Forces a real SQL concurrency conflict on the fixture user update.</summary>
    private sealed class StaleUserHook : SaveChangesInterceptor
    {
        /// <inheritdoc />
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            foreach (var entry in eventData.Context!.ChangeTracker.Entries<User>())
                if (entry.State == EntityState.Modified) entry.Property(RowVersionPropertyName).OriginalValue = new byte[RowVersionByteCount];
            return ValueTask.FromResult(result);
        }
    }
}
