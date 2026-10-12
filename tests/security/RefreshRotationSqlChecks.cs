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
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

/// <summary>Checks rotation persistence, strict replay, signing rollback and overlapping SQL refreshes.</summary>
internal static class RefreshRotationSqlChecks
{
    private const string ConnectionVariable = "LEGACYVAULT_TEST_CONNECTION_STRING";
    private const int TimeoutSeconds = 30;
    private const int ConcurrentRequests = 2;

    /// <summary>Runs against a migrated development database and cleans only owned fixtures.</summary>
    public static async Task Main()
    {
        string connection = Environment.GetEnvironmentVariable(ConnectionVariable)
            ?? throw new InvalidOperationException("Missing development fixture connection.");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options;
        var tokens = new JwtTokenService(Options.Create(new JwtOptions
        {
            SigningKey = new string('k', JwtOptions.MinimumSigningKeyBytes)
        }), TimeProvider.System);
        var ids = new List<Guid>();
        try
        {
            foreach (bool persistent in new[] { false, true })
            {
                var fixture = await SeedAsync(options, tokens, ids, persistent);
                AuthResult response;
                await using (var db = new AppDbContext(options))
                {
                    var result = await Service(db, tokens).RefreshAsync(fixture.Secret, CancellationToken.None);
                    Require(result.IsSuccess, "Valid refresh failed.");
                    response = result.Value;
                }
                await using (var verify = new AppDbContext(options))
                {
                    var family = await verify.RefreshTokens.Where(t => t.UserId == fixture.UserId).ToListAsync();
                    Require(family.Count == ConcurrentRequests, "Rotation did not persist exactly one replacement.");
                    var old = family.Single(t => t.Id == fixture.TokenId);
                    var replacement = family.Single(t => t.Id != fixture.TokenId);
                    Require(old.IsRevoked && old.ReplacedByTokenId == replacement.Id && !replacement.IsRevoked,
                        "Replacement link or revocation was not committed.");
                    Require(replacement.FamilyId == old.FamilyId && replacement.IsPersistent == persistent &&
                        replacement.ExpiresAt == old.ExpiresAt && response.RefreshTokenExpiresAt == old.ExpiresAt &&
                        response.IsPersistent == persistent && response.Session.UserId == fixture.UserId &&
                        response.Session.ExpiresIn > 0 && !string.IsNullOrWhiteSpace(response.AccessToken) &&
                        replacement.TokenHash == tokens.HashRefreshToken(response.RefreshToken),
                        "Absolute expiry, family, cookie policy or response mismatch.");
                }
                await using (var replay = new AppDbContext(options))
                {
                    var result = await Service(replay, tokens).RefreshAsync(fixture.Secret, CancellationToken.None);
                    Require(!result.IsSuccess, "Replay issued another session.");
                }
                await using (var verify = new AppDbContext(options))
                {
                    var family = await verify.RefreshTokens.Where(t => t.UserId == fixture.UserId).ToListAsync();
                    Require(family.All(t => t.IsRevoked), "Replay family revocation was not committed.");
                    Require(family.Single(t => t.Id == fixture.TokenId).ReplacedByTokenId is not null,
                        "Replay erased the replacement link.");
                }
            }
            var rollback = await SeedAsync(options, tokens, ids, persistent: false);
            await using (var db = new AppDbContext(options))
            {
                try
                {
                    await Service(db, new FailingTokens(tokens)).RefreshAsync(rollback.Secret, CancellationToken.None);
                    throw new InvalidOperationException("Signing failure was swallowed.");
                }
                catch (FixtureSigningException) { }
            }
            await using (var verify = new AppDbContext(options))
            {
                var family = await verify.RefreshTokens.Where(t => t.UserId == rollback.UserId).ToListAsync();
                Require(family.Count == 1 && !family[0].IsRevoked && family[0].ReplacedByTokenId is null,
                    "Signing failure did not roll back rotation.");
            }
            var concurrent = await SeedAsync(options, tokens, ids, persistent: false);
            var barrier = new SaveBarrier();
            var concurrentOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(connection).AddInterceptors(barrier).Options;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
            bool[] outcomes = await Task.WhenAll(
                AttemptAsync(concurrentOptions, tokens, concurrent.Secret, timeout.Token),
                AttemptAsync(concurrentOptions, tokens, concurrent.Secret, timeout.Token));
            Require(outcomes.Count(success => success) == 1, "Expected exactly one successful overlapping rotation.");
            await using (var verify = new AppDbContext(options))
            {
                var family = await verify.RefreshTokens.Where(t => t.UserId == concurrent.UserId).ToListAsync();
                Require(family.Count == ConcurrentRequests && family.Count(t => !t.IsRevoked) == 1 &&
                    family.Single(t => t.Id == concurrent.TokenId).ReplacedByTokenId ==
                        family.Single(t => t.Id != concurrent.TokenId).Id,
                    "Concurrent refresh left duplicate or inconsistent replacements.");
            }
            await CheckLogoutAsync(options, tokens, ids);
            Console.WriteLine("PASS: absolute rotation, strict replay, signing rollback, overlapping refresh, logout scope/authorization/idempotency and save rollback.");
        }
        finally
        {
            await using var cleanup = new AppDbContext(options);
            await using var transaction = await cleanup.Database.BeginTransactionAsync();
            await cleanup.RefreshTokens.Where(t => ids.Contains(t.UserId)).ExecuteDeleteAsync();
            await cleanup.Users.Where(u => ids.Contains(u.Id)).ExecuteDeleteAsync();
            await transaction.CommitAsync();
            Console.WriteLine("Fixture cleanup completed.");
        }
    }

    /// <summary>Verifies logout scope, principal/cookie ownership, revoked authorization and rollback.</summary>
    private static async Task CheckLogoutAsync(DbContextOptions<AppDbContext> options,
        ITokenService tokens, List<Guid> ids)
    {
        var owner = await SeedAsync(options, tokens, ids, persistent: false);
        var other = await SeedAsync(options, tokens, ids, persistent: false);
        Guid secondTokenId;
        await using (var seed = new AppDbContext(options))
        {
            var now = DateTimeOffset.UtcNow;
            var second = RefreshToken.Create(owner.UserId, tokens.CreateRefreshToken().Hash,
                Guid.NewGuid(), false, now, tokens.RefreshTokenLifetime);
            secondTokenId = second.Id;
            seed.RefreshTokens.Add(second);
            await seed.SaveChangesAsync();
        }
        await using (var db = new AppDbContext(options))
        {
            var service = Service(db, tokens, owner.UserId);
            var mismatch = await service.LogoutAsync(other.Secret, true, CancellationToken.None);
            Require(mismatch.IsFailure && mismatch.Error == AuthErrors.SessionExpired,
                "Cross-user cookie was accepted for all-device logout.");
        }
        await using (var verify = new AppDbContext(options))
            Require(!await verify.RefreshTokens.AnyAsync(t =>
                (t.UserId == owner.UserId || t.UserId == other.UserId) && t.RevokedAt != null),
                "Authorization failure changed fixture tokens.");
        await using (var db = new AppDbContext(options))
        {
            var service = Service(db, tokens);
            Require((await service.LogoutAsync(null, false, CancellationToken.None)).IsSuccess,
                "Missing cookie was not an idempotent current logout.");
            Require((await service.LogoutAsync("invalid-fixture-token", false, CancellationToken.None)).IsSuccess,
                "Malformed cookie was not an idempotent current logout.");
            Require((await service.LogoutAsync(owner.Secret, false, CancellationToken.None)).IsSuccess,
                "Current logout failed.");
        }
        await using (var verify = new AppDbContext(options))
        {
            var owned = await verify.RefreshTokens.Where(t => t.UserId == owner.UserId).ToListAsync();
            Require(owned.Single(t => t.Id == owner.TokenId).IsRevoked &&
                !owned.Single(t => t.Id == secondTokenId).IsRevoked &&
                !await verify.RefreshTokens.Where(t => t.UserId == other.UserId).AnyAsync(t => t.RevokedAt != null),
                "Current logout crossed family or user boundaries.");
        }
        await using (var db = new AppDbContext(options))
        {
            Require((await Service(db, tokens).LogoutAsync(owner.Secret, false, CancellationToken.None)).IsSuccess,
                "Repeated logout failed.");
            var denied = await Service(db, tokens).LogoutAsync(owner.Secret, true, CancellationToken.None);
            Require(denied.IsFailure && denied.Error == AuthErrors.SessionExpired,
                "Revoked cookie authorized all-device logout.");
        }
        await using (var verify = new AppDbContext(options))
            Require(!await verify.RefreshTokens.Where(t => t.Id == secondTokenId).AnyAsync(t => t.RevokedAt != null),
                "Denied all-device request revoked another family.");
        await using (var db = new AppDbContext(options))
            Require((await Service(db, tokens, owner.UserId).LogoutAsync(null, true, CancellationToken.None)).IsSuccess,
                "Valid principal without cookie could not log out all devices.");
        await using (var verify = new AppDbContext(options))
            Require(!await verify.RefreshTokens.AnyAsync(t => t.UserId == owner.UserId && t.RevokedAt == null),
                "Principal all-device logout was not committed.");
        await using (var db = new AppDbContext(options))
            Require((await Service(db, tokens).LogoutAsync(other.Secret, true, CancellationToken.None)).IsSuccess,
                "Active refresh token could not authorize all-device logout.");
        await using (var verify = new AppDbContext(options))
            Require(!await verify.RefreshTokens.AnyAsync(t => t.UserId == other.UserId && t.RevokedAt == null),
                "Refresh-authorized all-device logout was not committed.");

        var rollback = await SeedAsync(options, tokens, ids, persistent: false);
        var failingOptions = new DbContextOptionsBuilder<AppDbContext>(options)
            .AddInterceptors(new FailAfterSave()).Options;
        await using (var db = new AppDbContext(failingOptions))
        {
            try
            {
                await Service(db, tokens).LogoutAsync(rollback.Secret, false, CancellationToken.None);
                throw new InvalidOperationException("Controlled post-save failure was swallowed.");
            }
            catch (FixtureSaveException) { }
        }
        await using (var verify = new AppDbContext(options))
            Require(!await verify.RefreshTokens.Where(t => t.Id == rollback.TokenId).AnyAsync(t => t.RevokedAt != null),
                "Logout failure left a committed revocation.");
    }

    /// <summary>Creates a committed user and original token; only the hash is persisted.</summary>
    private static async Task<Fixture> SeedAsync(DbContextOptions<AppDbContext> options,
        ITokenService tokens, List<Guid> ids, bool persistent)
    {
        await using var db = new AppDbContext(options);
        var now = DateTimeOffset.UtcNow;
        var user = User.Create($"refresh-{Guid.NewGuid():N}@example.test", "Refresh fixture",
            "unused-fixture-password-hash", UserRole.Owner, UserStatus.Active, now);
        var generated = tokens.CreateRefreshToken();
        var token = RefreshToken.Create(user.Id, generated.Hash, Guid.NewGuid(), persistent, now,
            tokens.RefreshTokenLifetime);
        ids.Add(user.Id);
        db.Users.Add(user);
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();
        return new Fixture(user.Id, token.Id, generated.Value);
    }

    /// <summary>Executes a contender; only an actual SQL deadlock is an accepted infrastructure outcome.</summary>
    private static async Task<bool> AttemptAsync(DbContextOptions<AppDbContext> options,
        ITokenService tokens, string secret, CancellationToken ct)
    {
        await using var db = new AppDbContext(options);
        try { return (await Service(db, tokens).RefreshAsync(secret, ct)).IsSuccess; }
        catch (Exception exception) when (IsDeadlock(exception)) { return false; }
    }

    /// <summary>Recognizes only SQL error 1205 through provider exception wrappers.</summary>
    private static bool IsDeadlock(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is SqlException { Number: 1205 }) return true;
        return false;
    }

    /// <summary>Constructs the real use case without reading application secrets.</summary>
    private static AuthService Service(AppDbContext db, ITokenService tokens, Guid? userId = null) =>
        new(db, new Argon2PasswordHasher(), tokens, new FixturePrincipal(userId), TimeProvider.System);

    /// <summary>Fails the check without exposing bearer secrets.</summary>
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    /// <summary>Fixture identity and in-memory secret; never printed.</summary>
    private sealed record Fixture(Guid UserId, Guid TokenId, string Secret);

    /// <summary>Refresh does not rely on an access principal.</summary>
    private sealed class FixturePrincipal(Guid? userId) : ICurrentUser
    {
        /// <inheritdoc />
        public bool IsAuthenticated => userId is not null;
        /// <inheritdoc />
        public Guid? UserId => userId;
    }

    /// <summary>Controlled failure after SQL writes and before transaction commit.</summary>
    private sealed class FixtureSaveException : Exception { }

    /// <summary>Throws only after the real save has completed inside the use-case transaction.</summary>
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        /// <inheritdoc />
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken ct = default) => throw new FixtureSaveException();
    }

    /// <summary>Controlled signer failure after the rotation save.</summary>
    private sealed class FixtureSigningException : Exception { }

    /// <summary>Delegates token operations except access signing.</summary>
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

    /// <summary>Holds both tracked rotations before SQL writes to force overlapping transactions.</summary>
    private sealed class SaveBarrier : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        /// <inheritdoc />
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _arrivals) == ConcurrentRequests) _ready.TrySetResult(true);
            await _ready.Task.WaitAsync(ct);
            return result;
        }
    }
}
