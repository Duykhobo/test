using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using LegacyVault.Application.Features.Identity;

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
}
