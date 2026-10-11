using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using LegacyVault.Application.Features.Identity;
using LegacyVault.Domain.Identity;

namespace LegacyVault.AuthChecks;

/// <summary>Kiểm tra contract/validation Auth bằng BCL, không cần database hoặc test package.</summary>
internal static class Program
{
    private const string ValidEmail = "owner@example.test";
    private const string ValidPassword = "fixture-password";
    private const int EmailLimit = 256;
    private const int PasswordLimit = 128;
    private const string EmailSuffix = "@example.test";

    /// <summary>Chạy kiểm tra; exception khiến tiến trình trả exit code khác zero.</summary>
    public static void Main()
    {
        CheckValidation("valid input", ValidEmail, ValidPassword, null);
        CheckValidation("missing email", string.Empty, ValidPassword, nameof(LoginRequest.Email));
        CheckValidation("whitespace email", "   ", ValidPassword, nameof(LoginRequest.Email));
        CheckValidation("invalid email", "invalid-address", ValidPassword, nameof(LoginRequest.Email));
        CheckValidation("missing password", ValidEmail, string.Empty, nameof(LoginRequest.Password));
        CheckValidation("whitespace password", ValidEmail, "   ", nameof(LoginRequest.Password));
        CheckValidation("existing short password allowed", ValidEmail, "x", null);
        CheckValidation("email at limit", new string('a', EmailLimit - EmailSuffix.Length) + EmailSuffix, ValidPassword, null);
        CheckValidation("email beyond limit", new string('a', EmailLimit - EmailSuffix.Length + 1) + EmailSuffix, ValidPassword, nameof(LoginRequest.Email));
        CheckValidation("password at limit", ValidEmail, new string('x', PasswordLimit), null);
        CheckValidation("password beyond limit", ValidEmail, new string('x', PasswordLimit + 1), nameof(LoginRequest.Password));

        var user = new UserDto(Guid.Empty, ValidEmail, "Fixture User", null, "OWNER", "ACTIVE", DateTimeOffset.UnixEpoch, null);
        CheckPublicJson(new AuthSessionDto(user.Id, 900, user), ["userId", "expiresIn", "user"]);
        CheckPublicJson(new CurrentSessionDto(user), ["user"]);
        Console.WriteLine("PASS: 11 validation cases and 2 public response contracts.");

        UserLockoutChecks.Run();
        CheckRefreshToken();
    }

    /// <summary>Kiểm tra validation và tên field lỗi đúng contract DataAnnotations.</summary>
    /// <param name="name">Tên tình huống để báo lỗi.</param>
    /// <param name="email">Email fixture.</param>
    /// <param name="password">Mật khẩu fixture, không phải credential thật.</param>
    /// <param name="expectedField">Field phải lỗi; null nghĩa là payload hợp lệ.</param>
    /// <exception cref="InvalidOperationException">Validation không đáp ứng contract.</exception>
    private static void CheckValidation(string name, string email, string password, string? expectedField)
    {
        var request = new LoginRequest { Email = email, Password = password };
        var errors = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(request, new ValidationContext(request), errors, validateAllProperties: true);
        var matches = expectedField is null
            ? valid && errors.Count == 0
            : !valid && errors.Count > 0 && errors.All(error => error.MemberNames.Contains(expectedField));

        if (!matches)
        {
            throw new InvalidOperationException($"FAIL validation: {name}");
        }
    }

    /// <summary>Kiểm tra JSON camelCase và whitelist field để ngăn lộ token/hash qua response DTO.</summary>
    /// <typeparam name="T">Kiểu response công khai.</typeparam>
    /// <param name="response">Response fixture.</param>
    /// <param name="expectedRootFields">Whitelist field của session response.</param>
    /// <exception cref="InvalidOperationException">JSON không khớp contract công khai.</exception>
    private static void CheckPublicJson<T>(T response, string[] expectedRootFields)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var rootFields = document.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        var userFields = document.RootElement.GetProperty("user").EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        string[] expectedUserFields = ["id", "email", "fullName", "phone", "role", "status", "createdAt", "updatedAt"];

        if (!rootFields.SetEquals(expectedRootFields) || !userFields.SetEquals(expectedUserFields))
        {
            throw new InvalidOperationException($"FAIL public JSON contract: {typeof(T).Name}");
        }
    }

    /// <summary>Kiểm tra validation và lifecycle refresh token trong RAM.</summary>
    /// <exception cref="InvalidOperationException">Hành vi không đúng contract.</exception>
    private static void CheckRefreshToken()
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        TimeSpan lifetime = TimeSpan.FromMinutes(1);
        Guid userId = Guid.NewGuid();
        Guid familyId = Guid.NewGuid();
        string hash = new string('a', RefreshTokenRules.HashLength);

        RefreshToken token = RefreshToken.Create(
            userId, hash, familyId, true, now, lifetime);

        DateTimeOffset expiry = now.Add(lifetime);

        if (token.UserId != userId ||
            token.FamilyId != familyId ||
            token.TokenHash != hash.ToUpperInvariant() ||
            !token.IsPersistent ||
            token.CreatedAt != now ||
            token.ExpiresAt != expiry ||
            token.IsRevoked ||
            !token.IsActive(now) ||
            !token.IsActive(expiry.AddTicks(-1)) ||
            !token.IsExpired(expiry) ||
            token.IsActive(expiry))
        {
            throw new InvalidOperationException(
                "FAIL: refresh token creation or expiry.");
        }

        ExpectArgumentFailure(() => RefreshToken.Create(
            Guid.Empty, hash, familyId, false, now, lifetime));

        ExpectArgumentFailure(() => RefreshToken.Create(
            userId, hash, Guid.Empty, false, now, lifetime));

        ExpectArgumentFailure(() => RefreshToken.Create(
            userId, string.Empty, familyId, false, now, lifetime));

        ExpectArgumentFailure(() => RefreshToken.Create(
            userId, new string('z', RefreshTokenRules.HashLength),
            familyId, false, now, lifetime));

        ExpectArgumentFailure(() => RefreshToken.Create(
            userId, hash[..^1], familyId, false, now, lifetime));

        ExpectArgumentFailure(() => RefreshToken.Create(
            userId, hash, familyId, false, now, TimeSpan.Zero));

        ExpectArgumentFailure(() => RefreshToken.Create(
            userId, hash, familyId, false, now, -lifetime));

        ExpectArgumentFailure(() => token.Revoke(now, Guid.Empty));
        ExpectArgumentFailure(() => token.Revoke(now, token.Id));
        ExpectArgumentFailure(() => token.Revoke(now.AddTicks(-1)));

        if (token.IsRevoked || token.ReplacedByTokenId is not null)
        {
            throw new InvalidOperationException(
                "FAIL: invalid revoke must not mutate token.");
        }

        Guid replacementId = Guid.NewGuid();

        token.Revoke(now, replacementId);
        token.Revoke(now.AddSeconds(1), Guid.NewGuid());

        if (!token.IsRevoked ||
            token.IsActive(now) ||
            token.RevokedAt != now ||
            token.ReplacedByTokenId != replacementId)
        {
            throw new InvalidOperationException(
                "FAIL: refresh token revocation must be idempotent.");
        }

        Console.WriteLine("PASS: Refresh token validation and lifecycle.");
    }

    /// <summary>Kiểm tra input không hợp lệ bị từ chối.</summary>
    /// <param name="action">Thao tác phải ném ArgumentException hoặc lớp con.</param>
    /// <exception cref="InvalidOperationException">Thao tác chấp nhận input sai.</exception>
    private static void ExpectArgumentFailure(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentException)
        {
            return;
        }

        throw new InvalidOperationException(
            "FAIL: expected argument validation failure.");
    }
}
