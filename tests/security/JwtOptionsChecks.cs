#:project ../../src/LegacyVault.Infrastructure/LegacyVault.Infrastructure.csproj

using System;
using System.Collections.Generic;
using LegacyVault.Infrastructure;
using LegacyVault.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

/// <summary>Kiểm tra JWT options qua DI thật, không kết nối database hoặc đọc secret thật.</summary>
internal static class JwtOptionsChecks
{
    private const string KeySetting = "Jwt:SigningKey";
    private const string IssuerSetting = "Jwt:Issuer";
    private const string AudienceSetting = "Jwt:Audience";
    private const string AccessLifetimeSetting = "Jwt:AccessTokenMinutes";
    private const string RefreshLifetimeSetting = "Jwt:RefreshTokenDays";
    private const int ExpectedAccessMinutes = 15;
    private const int ExpectedRefreshDays = 7;

    /// <summary>Chạy checks; exception khiến tiến trình trả exit code khác zero.</summary>
    public static void Main()
    {
        using (ServiceProvider provider = CreateProvider())
        {
            JwtOptions options = provider.GetRequiredService<IOptions<JwtOptions>>().Value;
            if (options.AccessTokenMinutes != ExpectedAccessMinutes ||
                options.RefreshTokenDays != ExpectedRefreshDays)
            {
                throw new InvalidOperationException("Default token lifetime changed.");
            }
        }

        ExpectInvalid(KeySetting, null);
        ExpectInvalid(KeySetting, string.Empty);
        ExpectInvalid(KeySetting, new string(' ', JwtOptions.MinimumSigningKeyBytes));
        ExpectInvalid(KeySetting, new string('k', JwtOptions.MinimumSigningKeyBytes - 1));
        ExpectInvalid(IssuerSetting, string.Empty);
        ExpectInvalid(IssuerSetting, "   ");
        ExpectInvalid(AudienceSetting, string.Empty);
        ExpectInvalid(AudienceSetting, "   ");
        ExpectInvalid(AccessLifetimeSetting, "0");
        ExpectInvalid(AccessLifetimeSetting, "-1");
        ExpectInvalid(RefreshLifetimeSetting, "0");
        ExpectInvalid(RefreshLifetimeSetting, "-1");
        ExpectInvalid(RefreshLifetimeSetting, "2147483647");

        Console.WriteLine("PASS: JWT options defaults and invalid configuration checks.");
    }

    /// <summary>Đăng ký Infrastructure với cấu hình fixture, chỉ resolve options khi kiểm tra.</summary>
    /// <param name="setting">Tên cấu hình cần ghi đè hoặc null.</param>
    /// <param name="value">Giá trị fixture thay thế, có thể null để thử cấu hình thiếu.</param>
    /// <returns>Service provider cần được dispose bởi bên gọi.</returns>
    private static ServiceProvider CreateProvider(string? setting = null, string? value = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Server=fixture.invalid;Database=Fixture;Integrated Security=True",
            [KeySetting] = new string('k', JwtOptions.MinimumSigningKeyBytes)
        };

        if (setting is not null)
        {
            values[setting] = value;
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    /// <summary>Xác nhận cấu hình không hợp lệ bị options validation từ chối.</summary>
    /// <param name="setting">Tên cấu hình cần thử.</param>
    /// <param name="value">Giá trị không hợp lệ.</param>
    /// <exception cref="InvalidOperationException">Validation chấp nhận cấu hình sai.</exception>
    private static void ExpectInvalid(string setting, string? value)
    {
        using ServiceProvider provider = CreateProvider(setting, value);
        try
        {
            _ = provider.GetRequiredService<IOptions<JwtOptions>>().Value;
        }
        catch (OptionsValidationException)
        {
            return;
        }

        throw new InvalidOperationException($"Invalid JWT configuration accepted: {setting}");
    }
}
