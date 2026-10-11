#:project ../../src/LegacyVault.Infrastructure/LegacyVault.Infrastructure.csproj

using System;
using System.Globalization;
using LegacyVault.Application.Features.Identity;
using LegacyVault.Infrastructure.Security;

/// <summary>Kiểm tra password hasher độc lập, không cần database hoặc test package.</summary>
internal static class PasswordHasherChecks
{
    private const string Password = " Mật-khẩu-🔐 ";
    private const string UnsupportedParameters = "m=99999,t=2,p=1";
    private const string LegacyParameters = "m=19456,t=2,p=1";
    private const string LegacyPrefix = "argon2id$" + LegacyParameters + "$";

    /// <summary>Chạy checks; exception khiến tiến trình trả exit code khác zero.</summary>
    public static void Main()
    {
        var hasher = new Argon2PasswordHasher();
        string first = hasher.Hash(Password);
        string second = hasher.Hash(Password);

        Assert(first.StartsWith(LegacyPrefix, StringComparison.Ordinal), "Legacy format");
        Assert(first != second, "Random salt");
        Assert(hasher.Verify(Password, first), "Unicode round trip");
        Assert(hasher.Verify(Password, second), "Second salt round trip");
        Assert(!hasher.Verify(Password.Trim(), first), "Password must not be trimmed");
        Assert(!hasher.Verify("wrong-password", first), "Wrong password");
        Assert(!hasher.Verify(Password, first.Replace(
            LegacyParameters, UnsupportedParameters, StringComparison.Ordinal)), "Unsupported parameters");

        string[] parts = first.Split('$');
        byte[] alteredHash = Convert.FromBase64String(parts[3]);
        alteredHash[0] ^= 1;
        parts[3] = Convert.ToBase64String(alteredHash);
        Assert(!hasher.Verify(Password, string.Join('$', parts)), "Tampered hash");
        parts[2] = new string('!', parts[2].Length);
        Assert(!hasher.Verify(Password, string.Join('$', parts)), "Invalid Base64");
        Assert(!hasher.Verify(Password, first + "$extra"), "Extra segment");
        Assert(!hasher.Verify(Password, string.Empty), "Empty hash");
        Assert(!hasher.Verify(string.Empty, first), "Empty password");
        Assert(!hasher.Verify("   ", first), "Whitespace password");
        Assert(!hasher.Verify(null!, first), "Null password");
        Assert(!hasher.Verify(Password, null!), "Null hash");

        string atLimit = new string('x', AuthValidation.PasswordMaxLength);
        Assert(hasher.Verify(atLimit, hasher.Hash(atLimit)), "Password at limit");
        string beyondLimit = atLimit + "x";
        Assert(!hasher.Verify(beyondLimit, first), "Password beyond limit");
        ExpectArgumentFailure(() => hasher.Hash(beyondLimit));
        ExpectArgumentFailure(() => hasher.Hash(string.Empty));
        ExpectArgumentFailure(() => hasher.Hash("   "));
        ExpectArgumentFailure(() => hasher.Hash(null!));

        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            Assert(hasher.Verify(Password, first), "Verify across culture");
            Assert(hasher.Hash(Password).StartsWith(
                LegacyPrefix, StringComparison.Ordinal), "Invariant parameters");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }

        Console.WriteLine("PASS: Password hasher format, validation and round-trip checks.");
    }

    /// <summary>Báo lỗi khi kết quả khác kỳ vọng.</summary>
    /// <param name="condition">Điều kiện cần đúng.</param>
    /// <param name="scenario">Tên tình huống kiểm tra.</param>
    /// <exception cref="InvalidOperationException">Check thất bại.</exception>
    private static void Assert(bool condition, string scenario)
    {
        if (!condition)
        {
            throw new InvalidOperationException(scenario);
        }
    }

    /// <summary>Xác nhận đầu vào sai bị từ chối bằng argument exception.</summary>
    /// <param name="action">Thao tác với đầu vào không hợp lệ.</param>
    /// <exception cref="InvalidOperationException">Đầu vào sai không bị từ chối.</exception>
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

        throw new InvalidOperationException("Expected an argument exception.");
    }
}
