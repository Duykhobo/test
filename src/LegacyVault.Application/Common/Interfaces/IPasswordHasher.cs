namespace LegacyVault.Application.Common.Interfaces;

/// <summary>
/// Băm và kiểm tra mật khẩu. Cài đặt thật (Argon2id) nằm ở Infrastructure/Security.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string passwordHash);
}
