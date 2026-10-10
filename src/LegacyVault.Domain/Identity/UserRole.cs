namespace LegacyVault.Domain.Identity;

/// <summary>
/// Role hệ thống, được ghi vào JWT.
/// </summary>
/// <remarks>
/// Theo tài liệu kiến trúc: chỉ ADMIN / VERIFIER là role hệ thống.
/// Executor, Beneficiary KHÔNG phải role cố định mà là quan hệ với từng kho
/// (một người có thể là Owner kho A và Beneficiary kho B), nên sẽ kiểm tra trong service sau này.
/// Người dùng thường mặc định là Owner.
/// </remarks>
public enum UserRole
{
    Owner = 0,
    Verifier = 1,
    Admin = 2,
}
