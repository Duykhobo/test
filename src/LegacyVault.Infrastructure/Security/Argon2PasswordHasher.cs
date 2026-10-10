using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using LegacyVault.Application.Common.Interfaces;

namespace LegacyVault.Infrastructure.Security;

/// <summary>
/// Băm mật khẩu bằng Argon2id — thuật toán được OWASP khuyến nghị số 1.
/// </summary>
/// <remarks>
/// Vì sao không dùng SHA-256 cho mật khẩu? SHA-256 quá NHANH: GPU thử hàng tỷ mật khẩu/giây.
/// Argon2id cố tình chậm và tốn RAM (ở đây ~19 MB mỗi lần), nên dò mật khẩu cực kỳ đắt.
///
/// Chuỗi lưu DB có dạng:  argon2id$m=19456,t=2,p=1$&lt;salt base64&gt;$&lt;hash base64&gt;
/// Lưu kèm tham số để sau này tăng độ khó mà hash cũ vẫn kiểm tra được.
/// Salt ngẫu nhiên mỗi user → hai người cùng mật khẩu vẫn có hash khác nhau.
/// </remarks>
public sealed class Argon2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int MemoryKb = 19456; // 19 MiB — mức tối thiểu OWASP
    private const int Iterations = 2;
    private const int Parallelism = 1;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Compute(password, salt, MemoryKb, Iterations, Parallelism, HashSize);
        return $"argon2id$m={MemoryKb},t={Iterations},p={Parallelism}" +
               $"${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string passwordHash)
    {
        var parts = passwordHash.Split('$');
        if (parts.Length != 4 || parts[0] != "argon2id")
        {
            return false;
        }

        try
        {
            var p = parts[1].Split(',').Select(x => int.Parse(x[2..])).ToArray(); // "m=19456" → 19456
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Compute(password, salt, p[0], p[1], p[2], expected.Length);

            // So sánh thời gian cố định: không dừng sớm ở byte sai đầu tiên,
            // nên không ai đoán được hash qua việc đo thời gian.
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] Compute(string password, byte[] salt, int memoryKb, int iterations, int parallelism, int size)
    {
        var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKb,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };
        return argon2.GetBytes(size);
    }
}
