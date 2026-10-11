namespace LegacyVault.Application.Common.Interfaces;

/// <summary>
/// Băm và kiểm tra mật khẩu. Cài đặt thật (Argon2id) nằm ở Infrastructure/Security.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Băm mật khẩu bằng salt ngẫu nhiên, không chuẩn hóa đầu vào.</summary>
    /// <param name="password">Mật khẩu nguyên bản trong giới hạn Auth.</param>
    /// <returns>Chuỗi hash tự mô tả để lưu trong database.</returns>
    /// <exception cref="ArgumentNullException">Mật khẩu là null.</exception>
    /// <exception cref="ArgumentException">Mật khẩu rỗng hoặc chỉ có khoảng trắng.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Mật khẩu vượt giới hạn đầu vào.</exception>
    string Hash(string password);

    /// <summary>Kiểm tra mật khẩu mà không trim hoặc đổi casing.</summary>
    /// <param name="password">Mật khẩu cần xác thực.</param>
    /// <param name="passwordHash">Chuỗi hash được lưu trong database.</param>
    /// <returns>True khi khớp; false khi sai mật khẩu, đầu vào hoặc định dạng không hợp lệ.</returns>
    /// <remarks>Lỗi tài nguyên và lỗi hệ thống được truyền lên bên gọi.</remarks>
    bool Verify(string password, string passwordHash);
}
