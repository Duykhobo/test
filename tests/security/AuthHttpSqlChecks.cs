#:project ../../src/LegacyVault.Api/LegacyVault.Api.csproj
#:property PublishAot=false

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using LegacyVault.Api.Controllers.V1;
using LegacyVault.Api.Common;
using LegacyVault.Api.Extensions;
using LegacyVault.Application;
using LegacyVault.Domain.Identity;
using LegacyVault.Infrastructure;
using LegacyVault.Infrastructure.Persistence;
using LegacyVault.Infrastructure.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

/// <summary>Real loopback HTTP checks using production controllers/auth middleware and isolated fixture config.</summary>
internal static class AuthHttpSqlChecks
{
    private const string ConnectionVariable = "LEGACYVAULT_TEST_CONNECTION_STRING";
    private const string Password = "http-fixture-password-only";
    private const string AuthPath = "/api/v1/auth/";

    /// <summary>Starts an ephemeral host, checks cookies/session/replay/logout and cleans fixture IDs.</summary>
    public static async Task Main()
    {
        string connection = Environment.GetEnvironmentVariable(ConnectionVariable)
            ?? throw new InvalidOperationException("Missing development fixture connection.");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = connection,
            ["Jwt:SigningKey"] = new string('h', JwtOptions.MinimumSigningKeyBytes),
            ["Cors:AllowedOrigins:0"] = "http://localhost:5173"
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddApiServices(builder.Configuration);
        builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
        await using var app = builder.Build();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseRouting();
        app.UseCors(ApiServiceExtensions.FrontendCorsPolicy);
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();
        app.MapControllers();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options;
        var ids = new List<Guid>();
        try
        {
            await app.StartAsync();
            string address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            var baseUri = new Uri(address);
            int loginRequests = 0;
            int refreshRequests = 0;
            int logoutRequests = 0;
            foreach (bool remember in new[] { false, true })
            {
                var email = $"http-{Guid.NewGuid():N}@example.test";
                await using (var seed = new AppDbContext(options))
                {
                    var user = User.Create(email, "HTTP fixture", new Argon2PasswordHasher().Hash(Password),
                        UserRole.Owner, UserStatus.Active, DateTimeOffset.UtcNow);
                    ids.Add(user.Id);
                    seed.Users.Add(user);
                    await seed.SaveChangesAsync();
                }
                var jar = new CookieContainer();
                using var handler = new HttpClientHandler { CookieContainer = jar };
                using var client = new HttpClient(handler) { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(30) };
                using (var anonymous = await client.GetAsync(AuthPath + "session"))
                    Require(anonymous.StatusCode == HttpStatusCode.Unauthorized, "Anonymous session was accepted.");
                using var login = await client.PostAsJsonAsync(AuthPath + "login",
                    new { email, password = Password, rememberMe = remember });
                loginRequests++;
                Require(login.StatusCode == HttpStatusCode.OK, "HTTP login failed.");
                var cookies = SetCookieHeaderValue.ParseList(login.Headers.GetValues("Set-Cookie").ToList());
                var access = cookies.Single(c => c.Name.ToString() == AuthCookieNames.AccessToken);
                var refresh = cookies.Single(c => c.Name.ToString() == AuthCookieNames.RefreshToken);
                Require(access.HttpOnly && refresh.HttpOnly && !access.Secure && !refresh.Secure &&
                    access.SameSite == SameSiteMode.Lax && refresh.SameSite == SameSiteMode.Lax &&
                    access.Path.ToString() == AuthCookieNames.AccessTokenPath &&
                    refresh.Path.ToString() == AuthCookieNames.RefreshTokenPath &&
                    refresh.Expires.HasValue == remember, "Development cookie flags/path/persistence mismatch.");
                using (var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync()))
                {
                    var data = body.RootElement.GetProperty("data");
                    Require(!data.TryGetProperty("accessToken", out _) && !data.TryGetProperty("refreshToken", out _) &&
                        !data.GetProperty("user").TryGetProperty("passwordHash", out _), "Response leaked secret fields.");
                }
                string oldSecret = jar.GetCookies(new Uri(baseUri, AuthPath))[AuthCookieNames.RefreshToken]!.Value;
                using (var session = await client.GetAsync(AuthPath + "session"))
                    Require(session.StatusCode == HttpStatusCode.OK, "Access cookie did not authenticate session.");
                using (var rotated = await client.PostAsync(AuthPath + "refresh", null))
                    Require(rotated.StatusCode == HttpStatusCode.OK, "HTTP refresh failed.");
                refreshRequests++;
                Require(jar.GetCookies(new Uri(baseUri, AuthPath))[AuthCookieNames.RefreshToken]!.Value != oldSecret,
                    "Refresh cookie was not replaced.");
                using (var logout = await client.PostAsync(AuthPath + "logout", null))
                    Require(logout.StatusCode == HttpStatusCode.NoContent, "Bodyless HTTP logout failed.");
                logoutRequests++;
                Require(jar.GetCookies(new Uri(baseUri, AuthPath))[AuthCookieNames.RefreshToken] is null &&
                    jar.GetCookies(baseUri)[AuthCookieNames.AccessToken] is null, "Logout did not clear cookie paths.");
                using (var session = await client.GetAsync(AuthPath + "session"))
                    Require(session.StatusCode == HttpStatusCode.Unauthorized, "Cleared jar still authenticated.");
                using var replayHandler = new HttpClientHandler { UseCookies = false };
                using var replayClient = new HttpClient(replayHandler) { BaseAddress = baseUri };
                using var replayRequest = new HttpRequestMessage(HttpMethod.Post, AuthPath + "refresh");
                replayRequest.Headers.Add("Cookie", AuthCookieNames.RefreshToken + "=" + oldSecret);
                using var replay = await replayClient.SendAsync(replayRequest);
                refreshRequests++;
                Require(replay.StatusCode == HttpStatusCode.Unauthorized && replay.Headers.Contains("Set-Cookie"),
                    "Old refresh cookie was accepted or failed refresh did not clear cookies.");
            }
            using var limiterHandler = new HttpClientHandler { UseCookies = false };
            using var limiterClient = new HttpClient(limiterHandler) { BaseAddress = baseUri };
            await CheckLimitAsync(limiterClient, "login", AuthRateLimitPolicies.LoginPermitLimit - loginRequests,
                HttpStatusCode.BadRequest);
            using (var spoof = new HttpRequestMessage(HttpMethod.Post, AuthPath + "login"))
            {
                spoof.Headers.Add("X-Forwarded-For", "192.0.2.123");
                spoof.Content = JsonContent.Create(new { });
                using var rejected = await limiterClient.SendAsync(spoof);
                Require(rejected.StatusCode == HttpStatusCode.TooManyRequests,
                    "Untrusted forwarded header bypassed the IP limiter.");
            }
            await CheckLimitAsync(limiterClient, "refresh", AuthRateLimitPolicies.SessionPermitLimit - refreshRequests,
                HttpStatusCode.Unauthorized);
            await CheckLimitAsync(limiterClient, "logout", AuthRateLimitPolicies.SessionPermitLimit - logoutRequests,
                HttpStatusCode.NoContent);
            using (var session = await limiterClient.GetAsync(AuthPath + "session"))
                Require(session.StatusCode == HttpStatusCode.Unauthorized,
                    "Session incorrectly shared the exhausted logout policy.");
            Console.WriteLine("PASS: HTTP cookies/session/refresh/logout; independent IP limits, 429/Retry-After and spoofed forwarded header rejected.");
        }
        finally
        {
            await app.StopAsync();
            await using var cleanup = new AppDbContext(options);
            await using var transaction = await cleanup.Database.BeginTransactionAsync();
            await cleanup.RefreshTokens.Where(t => ids.Contains(t.UserId)).ExecuteDeleteAsync();
            await cleanup.Users.Where(u => ids.Contains(u.Id)).ExecuteDeleteAsync();
            await transaction.CommitAsync();
            Console.WriteLine("HTTP fixture cleanup completed; host stopped.");
        }
    }

    /// <summary>Consumes the remaining budget, checks endpoint independence and rejection metadata.</summary>
    private static async Task CheckLimitAsync(HttpClient client, string action, int remaining,
        HttpStatusCode allowedStatus)
    {
        for (int index = 0; index < remaining; index++)
        {
            using var allowed = action == "login"
                ? await client.PostAsJsonAsync(AuthPath + action, new { })
                : await client.PostAsync(AuthPath + action, null);
            Require(allowed.StatusCode == allowedStatus, "Endpoint rejected before its own budget was exhausted.");
        }
        using var rejected = action == "login"
            ? await client.PostAsJsonAsync(AuthPath + action, new { })
            : await client.PostAsync(AuthPath + action, null);
        Require(rejected.StatusCode == HttpStatusCode.TooManyRequests &&
            rejected.Headers.RetryAfter?.Delta is { } delay && delay > TimeSpan.Zero &&
            delay <= AuthRateLimitPolicies.Window,
            "Exhausted endpoint did not return 429 with bounded positive Retry-After.");
    }

    /// <summary>Fails without including credentials or bearer values in diagnostics.</summary>
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
