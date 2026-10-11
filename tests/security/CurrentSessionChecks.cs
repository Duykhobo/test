#:project ../../src/LegacyVault.Infrastructure/LegacyVault.Infrastructure.csproj

using System;
using System.Threading;
using System.Threading.Tasks;
using LegacyVault.Application.Common.Interfaces;
using LegacyVault.Application.Features.Identity;
using LegacyVault.Infrastructure.Persistence;
using LegacyVault.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

/// <summary>Kiểm tra session từ chối principal sai trước khi truy cập database.</summary>
internal static class CurrentSessionChecks
{
    /// <summary>Chạy guards với implementation AuthService thật và dependency fixture.</summary>
    public static async Task Main()
    {
        // Không đăng ký provider: truy vấn database ngoài ý muốn phải làm check thất bại.
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().Options);
        var hasher = new Argon2PasswordHasher();
        var tokens = new JwtTokenService(Options.Create(new JwtOptions
        {
            SigningKey = new string('k', JwtOptions.MinimumSigningKeyBytes)
        }), TimeProvider.System);

        foreach (var principal in new[]
        {
            new FixtureCurrentUser(false, null),
            new FixtureCurrentUser(false, Guid.NewGuid()),
            new FixtureCurrentUser(true, null),
            new FixtureCurrentUser(true, Guid.Empty)
        })
        {
            var service = new AuthService(db, hasher, tokens, principal, TimeProvider.System);
            var result = await service.GetCurrentUserAsync(CancellationToken.None);
            if (!result.IsFailure || result.Error != AuthErrors.SessionExpired)
            {
                throw new InvalidOperationException("Invalid principal accepted by current session.");
            }
        }

        Console.WriteLine("PASS: Current session rejects 4 invalid principal cases without database access.");
    }

    /// <summary>Principal fixture để kiểm tra riêng các nhánh guard.</summary>
    private sealed class FixtureCurrentUser : ICurrentUser
    {
        /// <summary>Khởi tạo principal theo case cần kiểm tra.</summary>
        /// <param name="isAuthenticated">Trạng thái xác thực fixture.</param>
        /// <param name="userId">ID fixture, có thể thiếu hoặc rỗng.</param>
        public FixtureCurrentUser(bool isAuthenticated, Guid? userId)
        {
            IsAuthenticated = isAuthenticated;
            UserId = userId;
        }

        /// <inheritdoc />
        public bool IsAuthenticated { get; }

        /// <inheritdoc />
        public Guid? UserId { get; }
    }
}
