using LegacyVault.Application.Common.Interfaces;
using LegacyVault.Domain.Common;
using LegacyVault.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Application.Features.Identity;

public interface IAuthService
{
    Task<Result<AuthResult>> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<Result<AuthResult>> RefreshAsync(string? refreshToken, CancellationToken ct);
    Task LogoutAsync(string? refreshToken, bool revokeAllDevices, CancellationToken ct);
    Task<Result<UserDto>> GetCurrentUserAsync(CancellationToken ct);
}

/// <summary>
/// Use case đăng nhập / làm mới phiên / đăng xuất.
/// </summary>
/// <remarks>
/// Đây là "primary constructor" (C# 12): các tham số trong ngoặc sau tên class được
/// DI container truyền vào và dùng được trong toàn bộ class.
/// Mọi thứ service cần đều là INTERFACE → khi viết unit test có thể thay bằng bản giả.
/// TimeProvider (có sẵn từ .NET 8) thay cho DateTime.UtcNow để test được "15 phút sau".
/// </remarks>
public sealed class AuthService(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IAuthService
{
    // Hash của một mật khẩu ngẫu nhiên, dùng để "giả vờ" kiểm tra khi email không tồn tại (xem LoginAsync).
    private static string? _dummyHash;

    public async Task<Result<AuthResult>> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var email = User.NormalizeEmail(request.Email);

        // 1. Tải dữ liệu. EF dịch biểu thức này thành: SELECT ... WHERE Email = @email
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);

        if (user is null)
        {
            // Vẫn chạy Argon2 cho tốn đúng bấy nhiêu thời gian như khi email có thật.
            // Nếu bỏ dòng này, kẻ tấn công đo thời gian phản hồi là biết email nào đã đăng ký.
            _dummyHash ??= passwordHasher.Hash(Guid.NewGuid().ToString());
            passwordHasher.Verify(request.Password, _dummyHash);
            return AuthErrors.InvalidCredentials();
        }

        // 2. Kiểm tra quy tắc nghiệp vụ — mỗi quy tắc nằm trong entity, service chỉ hỏi.
        if (user.IsLockedOut(now))
        {
            return AuthErrors.AccountLocked(user.LockoutRemainingSeconds(now));
        }

        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            var attemptsRemaining = user.RegisterFailedLogin(now);
            await db.SaveChangesAsync(ct); // lưu số lần sai
            return attemptsRemaining == 0
                ? AuthErrors.AccountLocked(user.LockoutRemainingSeconds(now))
                : AuthErrors.InvalidCredentials(attemptsRemaining);
        }

        // Kiểm tra trạng thái SAU khi mật khẩu đúng, để người lạ không dò được trạng thái tài khoản.
        if (user.Status != UserStatus.Active)
        {
            return AuthErrors.AccountNotActive;
        }

        // 3. Gọi method của entity để đổi trạng thái.
        user.RegisterSuccessfulLogin(now);

        // 4. Mở phiên mới: một family refresh token mới cho thiết bị này.
        var result = IssueTokens(user, familyId: Guid.CreateVersion7(), request.RememberMe, now);

        // 5. Lưu MỘT lần cho mọi thay đổi (user + refresh token) → cùng một transaction.
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<Result<AuthResult>> RefreshAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return AuthErrors.SessionExpired;
        }

        var now = timeProvider.GetUtcNow();
        var hash = tokenService.HashRefreshToken(refreshToken);

        // Include = JOIN để lấy luôn User trong một câu SQL.
        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .SingleOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null)
        {
            return AuthErrors.SessionExpired;
        }

        if (stored.IsRevoked)
        {
            // Token đã bị xoay vòng/thu hồi mà vẫn có người dùng lại → nghi bị đánh cắp.
            // Thu hồi cả family để cả kẻ gian lẫn chủ thật đều phải đăng nhập lại.
            await RevokeFamilyAsync(stored.FamilyId, now, ct);
            await db.SaveChangesAsync(ct);
            return AuthErrors.SessionExpired;
        }

        var user = stored.User;
        if (stored.IsExpired(now) || user.Status != UserStatus.Active || user.IsLockedOut(now))
        {
            stored.Revoke(now);
            await db.SaveChangesAsync(ct);
            return AuthErrors.SessionExpired;
        }

        // Xoay vòng: cấp token mới cùng family, thu hồi token cũ và ghi lại token thay thế.
        var result = IssueTokens(user, stored.FamilyId, stored.IsPersistent, now, out var newToken);
        stored.Revoke(now, replacedByTokenId: newToken.Id);

        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task LogoutAsync(string? refreshToken, bool revokeAllDevices, CancellationToken ct)
    {
        // Đăng xuất luôn "thành công" với FE, kể cả khi token đã hết hạn hay không tồn tại.
        // (Idempotent: bấm đăng xuất 2 lần không được báo lỗi.)
        var now = timeProvider.GetUtcNow();

        RefreshToken? stored = null;
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            var hash = tokenService.HashRefreshToken(refreshToken);
            stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        }

        if (revokeAllDevices)
        {
            // Xác định người dùng từ refresh token, hoặc từ access token nếu còn hạn.
            var userId = stored?.UserId ?? currentUser.UserId;
            if (userId is not null)
            {
                var all = await db.RefreshTokens
                    .Where(t => t.UserId == userId && t.RevokedAt == null)
                    .ToListAsync(ct);
                all.ForEach(t => t.Revoke(now));
            }
        }
        else if (stored is not null)
        {
            await RevokeFamilyAsync(stored.FamilyId, now, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<Result<UserDto>> GetCurrentUserAsync(CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId)
        {
            return AuthErrors.SessionExpired;
        }

        // AsNoTracking: chỉ đọc, không sửa → EF không cần theo dõi thay đổi, nhanh hơn.
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || user.Status != UserStatus.Active)
        {
            return AuthErrors.SessionExpired;
        }

        return UserDto.From(user);
    }

    // ----- Hàm phụ -----

    private AuthResult IssueTokens(User user, Guid familyId, bool isPersistent, DateTimeOffset now) =>
        IssueTokens(user, familyId, isPersistent, now, out _);

    private AuthResult IssueTokens(
        User user, Guid familyId, bool isPersistent, DateTimeOffset now, out RefreshToken entity)
    {
        var access = tokenService.CreateAccessToken(user);
        var refresh = tokenService.CreateRefreshToken();

        entity = RefreshToken.Create(
            user.Id, refresh.Hash, familyId, isPersistent, now, tokenService.RefreshTokenLifetime);
        db.RefreshTokens.Add(entity); // chưa ghi DB, chỉ đánh dấu "sẽ INSERT" khi SaveChanges

        var session = new AuthSessionDto(
            user.Id,
            ExpiresIn: (int)(access.ExpiresAt - now).TotalSeconds,
            UserDto.From(user));

        return new AuthResult(
            access.Value, access.ExpiresAt, refresh.Value, entity.ExpiresAt, isPersistent, session);
    }

    private async Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken ct)
    {
        var family = await db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(ct);
        family.ForEach(t => t.Revoke(now));
    }
}
