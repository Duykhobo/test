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
            Console.WriteLine("PASS: absolute rotation, persistence policy, strict replay, signing rollback and overlapping refresh (one SQL deadlock victim).");
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
    private static AuthService Service(AppDbContext db, ITokenService tokens) =>
        new(db, new Argon2PasswordHasher(), tokens, new FixturePrincipal(), TimeProvider.System);

    /// <summary>Fails the check without exposing bearer secrets.</summary>
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    /// <summary>Fixture identity and in-memory secret; never printed.</summary>
    private sealed record Fixture(Guid UserId, Guid TokenId, string Secret);

    /// <summary>Refresh does not rely on an access principal.</summary>
    private sealed class FixturePrincipal : ICurrentUser
    {
        /// <inheritdoc />
        public bool IsAuthenticated => false;
        /// <inheritdoc />
        public Guid? UserId => null;
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
