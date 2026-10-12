using LegacyVault.Domain.Common;
using LegacyVault.Application.Common.Interfaces;
using LegacyVault.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Application.Features.Identity;

/// <summary>Hợp đồng use case Auth; cookie và HTTP thuộc tầng Api.</summary>
public interface IAuthService
{
    /// <summary>Xác thực thông tin đăng nhập và mở phiên.</summary>
    /// <param name="request">Email, mật khẩu và lựa chọn ghi nhớ.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns>Kết quả phiên nội bộ hoặc lỗi xác thực an toàn.</returns>
    Task<Result<AuthResult>> LoginAsync(LoginRequest request, CancellationToken ct);

    /// <summary>Xoay vòng refresh token của một phiên.</summary>
    /// <param name="refreshToken">Token lấy từ cookie HttpOnly, có thể thiếu.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns>Cặp token thay thế hoặc lỗi hết phiên.</returns>
    Task<Result<AuthResult>> RefreshAsync(string? refreshToken, CancellationToken ct);

    /// <summary>Thu hồi phiên hiện tại hoặc mọi phiên khi có đủ quyền.</summary>
    /// <param name="refreshToken">Token phiên hiện tại, có thể thiếu.</param>
    /// <param name="revokeAllDevices">Yêu cầu thu hồi mọi thiết bị.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns>Thành công sau commit hoặc no-op hợp lệ; lỗi khi không đủ bằng chứng phiên.</returns>
    Task<Result> LogoutAsync(string? refreshToken, bool revokeAllDevices, CancellationToken ct);

    /// <summary>Đọc người dùng của principal đã được middleware xác thực.</summary>
    /// <param name="ct">Token hủy request.</param>
    /// <returns>DTO công khai hoặc lỗi hết phiên.</returns>
    Task<Result<UserDto>> GetCurrentUserAsync(CancellationToken ct);
}

/// <summary>Khung Auth cho developer tự hoàn thiện nghiệp vụ trên .NET 10.</summary>
/// <remarks>
/// Đã triển khai đọc session, đăng nhập Owner/Verifier và refresh absolute với strict replay.
/// Đã triển khai logout theo family hoặc mọi thiết bị; challenge Admin chưa triển khai.
/// </remarks>
public sealed class AuthService : IAuthService
{
    /// <summary>
    /// Hash giả cho email không tồn tại; không thuộc tài khoản thật.
    /// Phải cập nhật khi thay đổi bộ tham số của password hasher.
    /// </summary>
    private const string DummyPasswordHash =
        "argon2id$m=19456,t=2,p=1$Gf9inP6XtC2sfIYwgNyTzQ==$s+HO+WC+25wQwHWuJL0jQbOXq7y+pSyQ8K/2RmCfcoc=";

    /// <summary>Tổng số lần thử lưu bộ đếm, gồm lần đầu và các lần retry.</summary>
    private const int MaxFailedLoginSaveAttempts = 3;
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    /// <summary>Nhận các dependency của use case; không thực hiện I/O trong constructor.</summary>
    /// <param name="db">Context persistence scoped của request.</param>
    /// <param name="passwordHasher">Hasher dùng xác thực mật khẩu.</param>
    /// <param name="tokenService">Service cấp token và hash refresh token.</param>
    /// <param name="currentUser">Principal đã được middleware xác thực.</param>
    /// <param name="timeProvider">Đồng hồ để nghiệp vụ có thể kiểm thử.</param>
    /// <exception cref="ArgumentNullException">Một dependency là null.</exception>
    public AuthService(IAppDbContext db, IPasswordHasher passwordHasher,
        ITokenService tokenService, ICurrentUser currentUser, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(passwordHasher);
        ArgumentNullException.ThrowIfNull(tokenService);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _db = db;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Request là null.</exception>
    /// <exception cref="OperationCanceledException">Thao tác bị hủy.</exception>
    /// <exception cref="DbUpdateConcurrencyException">Hết lượt retry hoặc conflict ngoài user hiện tại.</exception>
    /// <exception cref="NotImplementedException">Challenge Admin chưa triển khai.</exception>
    public async Task<Result<AuthResult>> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        string email = User.NormalizeEmail(request.Email);
        User? user = await _db.Users.SingleOrDefaultAsync(
            candidate => candidate.Email == email,
            ct);
        string hashToVerify = user?.PasswordHash ?? DummyPasswordHash;

        bool passwordMatches = _passwordHasher.Verify(
            request.Password,
            hashToVerify);

        ct.ThrowIfCancellationRequested();
        if (user is null)
        {
            return Result<AuthResult>.Failure(
                AuthErrors.InvalidCredentials());
        }
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (!passwordMatches)
        {
            string verifiedPasswordHash = user.PasswordHash;
            for (int attempt = 1; attempt <= MaxFailedLoginSaveAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                now = _timeProvider.GetUtcNow();
                if (user.IsLockedOut(now))
                {
                    return Result<AuthResult>.Failure(
                        AuthErrors.InvalidCredentials());
                }
                try
                {
                    user.RegisterFailedLogin(now);

                    await _db.SaveChangesAsync(ct);

                    return Result<AuthResult>.Failure(
                        AuthErrors.InvalidCredentials());
                }
                catch (DbUpdateConcurrencyException exception)
                    when (attempt < MaxFailedLoginSaveAttempts)
                {
                    foreach (var entry in exception.Entries)
                    {
                        if (!ReferenceEquals(entry.Entity, user))
                        {
                            throw;
                        }

                        await entry.ReloadAsync(ct);

                        if (entry.State == EntityState.Detached)
                        {
                            return Result<AuthResult>.Failure(
                                AuthErrors.InvalidCredentials());
                        }
                    }

                    if (user.PasswordHash != verifiedPasswordHash)
                    {
                        return Result<AuthResult>.Failure(
                            AuthErrors.InvalidCredentials());
                    }
                }
            }
        }
        if (user.IsLockedOut(now))
        {
            return Result<AuthResult>.Failure(
                AuthErrors.AccountLocked(
                    user.LockoutRemainingSeconds(now)));
        }

        if (user.Status != UserStatus.Active)
        {
            return Result<AuthResult>.Failure(
                AuthErrors.AccountNotActive);
        }
        if (user.Role == UserRole.Admin)
        {
            // TODO: [BẢN THIẾT KẾ THỰC THI - DEVELOPER BLUEPRINT]
            // 1. [MỤC TIÊU & NGHIỆP VỤ]: Yêu cầu Admin hoàn thành 2FA trước khi cấp phiên.
            // 2. [INPUT & OUTPUT]: User đã đúng mật khẩu -> challenge, chưa trả token phiên.
            // 3. [CÁC BƯỚC TUẦN TỰ]: Chốt contract challenge; tạo challenge có hạn;
            //    chỉ cấp phiên sau khi endpoint verify xác nhận thành công.
            // 4. [HÀM / THƯ VIỆN]: Contract challenge và dịch vụ 2FA sẽ được chốt riêng.
            // 5. [ĐIỀU KIỆN BIÊN & NGOẠI LỆ]: Challenge hết hạn, replay, sai mã;
            //    không cấp JWT Admin từ password-only login.
            throw new NotImplementedException(AuthErrors.ScaffoldNotImplemented);
        }
        await using var transaction = await _db.BeginTransactionAsync(ct);
        now = _timeProvider.GetUtcNow();
        user.RegisterSuccessfulLogin(now);
        var generatedRefreshToken = _tokenService.CreateRefreshToken();
        Guid familyId = Guid.NewGuid();
        var refreshToken = RefreshToken.Create(
            user.Id,
            generatedRefreshToken.Hash,
            familyId,
            request.RememberMe,
            now,
            _tokenService.RefreshTokenLifetime);
        _db.RefreshTokens.Add(refreshToken);
        await _db.SaveChangesAsync(ct);

        var accessToken = _tokenService.CreateAccessToken(user);
        UserDto userDto = UserDto.From(user);
        TimeSpan remainingLifetime = accessToken.ExpiresAt - _timeProvider.GetUtcNow();
        int expiresIn = checked((int)Math.Max(
            0, Math.Ceiling(remainingLifetime.TotalSeconds)));
        var session = new AuthSessionDto(
            user.Id,
            expiresIn,
            userDto);
        var authResult = new AuthResult(
            accessToken.Value,
            accessToken.ExpiresAt,
            generatedRefreshToken.Value,
            refreshToken.ExpiresAt,
            refreshToken.IsPersistent,
            session);
        await transaction.CommitAsync(ct);

        return Result<AuthResult>.Success(authResult);
    }

    /// <inheritdoc />
    /// <exception cref="OperationCanceledException">Request bị hủy.</exception>
    /// <remarks>
    /// Rotation giữ hạn absolute; replay commit thu hồi cả family trước khi trả lỗi phiên.
    /// Lỗi ký token hoặc persistence được truyền lên; không retry tự động khi deadlock.
    /// </remarks>
    public async Task<Result<AuthResult>> RefreshAsync(string? refreshToken, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Result<AuthResult>.Failure(AuthErrors.SessionExpired);
        }
        string tokenHash;
        try
        {
            tokenHash = _tokenService.HashRefreshToken(refreshToken);

        }
        catch (ArgumentException)
        {
            return Result<AuthResult>.Failure(AuthErrors.SessionExpired);
        }
        await using var transaction = await _db.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        RefreshToken? storedToken = await _db.RefreshTokens.Include(token => token.User).SingleOrDefaultAsync(token => token.TokenHash == tokenHash, ct);
        if (storedToken is null)
        {
            return Result<AuthResult>.Failure(AuthErrors.SessionExpired);
        }
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (storedToken.IsRevoked)
        {
            List<RefreshToken> familyTokens = await _db.RefreshTokens.Where(token => token.FamilyId == storedToken.FamilyId).ToListAsync(ct);
            now = _timeProvider.GetUtcNow();
            foreach (RefreshToken familyToken in familyTokens)
            {
                if (!familyToken.IsRevoked)
                {
                    familyToken.Revoke(now);
                }
            }
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Result<AuthResult>.Failure(AuthErrors.SessionExpired);
        }
        User user = storedToken.User;
        if (storedToken.IsExpired(now) || user.Status != UserStatus.Active || user.Role == UserRole.Admin || user.IsLockedOut(now))
        {
            return Result<AuthResult>.Failure(AuthErrors.SessionExpired);
        }
        now = _timeProvider.GetUtcNow();
        TimeSpan remainingLifetime = storedToken.ExpiresAt - now;
        if (remainingLifetime <= TimeSpan.Zero)
        {
            return Result<AuthResult>.Failure(AuthErrors.SessionExpired);
        }
        GeneratedRefreshToken generatedRefreshToken = _tokenService.CreateRefreshToken();
        RefreshToken replacementToken = RefreshToken.Create(
            storedToken.UserId,
            generatedRefreshToken.Hash,
            storedToken.FamilyId,
            storedToken.IsPersistent,
            now,
            remainingLifetime
        );
        storedToken.Revoke(now, replacementToken.Id);
        _db.RefreshTokens.Add(replacementToken);
        await _db.SaveChangesAsync(ct);
        AccessToken accessToken = _tokenService.CreateAccessToken(user);
        UserDto userDto = UserDto.From(user);
        TimeSpan accessRemaining = accessToken.ExpiresAt - _timeProvider.GetUtcNow();
        int expiresIn = checked((int)Math.Max(0, Math.Ceiling(accessRemaining.TotalSeconds)));
        var session = new AuthSessionDto(
            user.Id,
            expiresIn,
            userDto
        );
        var authResult = new AuthResult(
            accessToken.Value,
            accessToken.ExpiresAt,
            generatedRefreshToken.Value,
            replacementToken.ExpiresAt,
            replacementToken.IsPersistent,
            session
        );
        await transaction.CommitAsync(ct);
        return Result<AuthResult>.Success(authResult);
    }

    /// <inheritdoc />
    /// <exception cref="OperationCanceledException">Request bị hủy.</exception>
    /// <remarks>
    /// Logout mọi thiết bị yêu cầu principal hợp lệ hoặc refresh token active của user active.
    /// Cookie khác user bị từ chối ở nhánh principal; lỗi persistence được truyền lên.
    /// Thu hồi refresh token không tự vô hiệu access JWT đã cấp.
    /// </remarks>
    public async Task<Result> LogoutAsync(string? refreshToken, bool revokeAllDevices, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (revokeAllDevices)
        {
            Guid? principalUserId = _currentUser.UserId;

            if (_currentUser.IsAuthenticated &&
                principalUserId is { } userId &&
                userId != Guid.Empty)
            {
                await using var allDevicesTransaction =
                    await _db.BeginTransactionAsync(
                        System.Data.IsolationLevel.Serializable,
                        ct);

                User? authenticatedUser = await _db.Users
                    .SingleOrDefaultAsync(
                        user => user.Id == userId,
                        ct);

                if (authenticatedUser is null ||
                    authenticatedUser.Status != UserStatus.Active)
                {
                    return Result.Failure(AuthErrors.SessionExpired);
                }
                if (!string.IsNullOrWhiteSpace(refreshToken))
                {
                    string cookieTokenHash;

                    try
                    {
                        cookieTokenHash = _tokenService.HashRefreshToken(refreshToken);
                    }
                    catch (ArgumentException)
                    {
                        return Result.Failure(AuthErrors.SessionExpired);
                    }

                    RefreshToken? cookieToken = await _db.RefreshTokens
                        .SingleOrDefaultAsync(
                            token => token.TokenHash == cookieTokenHash,
                            ct);

                    if (cookieToken is null || cookieToken.UserId != userId)
                    {
                        return Result.Failure(AuthErrors.SessionExpired);
                    }
                }
                List<RefreshToken> userTokens = await _db.RefreshTokens
                    .Where(token => token.UserId == userId)
                    .ToListAsync(ct);

                DateTimeOffset revokeAt = _timeProvider.GetUtcNow();

                foreach (RefreshToken userToken in userTokens)
                {
                    if (!userToken.IsRevoked)
                    {
                        userToken.Revoke(revokeAt);
                    }
                }

                await _db.SaveChangesAsync(ct);
                await allDevicesTransaction.CommitAsync(ct);

                return Result.Success();
            }

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return Result.Failure(AuthErrors.SessionExpired);
            }

            string fallbackTokenHash;

            try
            {
                fallbackTokenHash = _tokenService.HashRefreshToken(refreshToken);
            }
            catch (ArgumentException)
            {
                return Result.Failure(AuthErrors.SessionExpired);
            }

            await using var fallbackTransaction =
                await _db.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable,
                    ct);

            RefreshToken? fallbackToken = await _db.RefreshTokens
                .Include(token => token.User)
                .SingleOrDefaultAsync(
                    token => token.TokenHash == fallbackTokenHash,
                    ct);

            DateTimeOffset fallbackNow = _timeProvider.GetUtcNow();

            if (fallbackToken is null ||
                !fallbackToken.IsActive(fallbackNow) ||
                fallbackToken.User.Status != UserStatus.Active ||
                fallbackToken.User.IsLockedOut(fallbackNow) ||
                fallbackToken.User.Role == UserRole.Admin)
            {
                return Result.Failure(AuthErrors.SessionExpired);
            }

            List<RefreshToken> fallbackUserTokens = await _db.RefreshTokens
    .Where(token => token.UserId == fallbackToken.UserId)
    .ToListAsync(ct);

            fallbackNow = _timeProvider.GetUtcNow();

            if (!fallbackToken.IsActive(fallbackNow))
            {
                return Result.Failure(AuthErrors.SessionExpired);
            }

            foreach (RefreshToken userToken in fallbackUserTokens)
            {
                if (!userToken.IsRevoked)
                {
                    userToken.Revoke(fallbackNow);
                }
            }

            await _db.SaveChangesAsync(ct);
            await fallbackTransaction.CommitAsync(ct);

            return Result.Success();
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Result.Success();
        }

        string tokenHash;

        try
        {
            tokenHash = _tokenService.HashRefreshToken(refreshToken);
        }
        catch (ArgumentException)
        {
            return Result.Success();
        }
        await using var transaction = await _db.BeginTransactionAsync(
    System.Data.IsolationLevel.Serializable,
    ct);

        RefreshToken? storedToken = await _db.RefreshTokens
            .SingleOrDefaultAsync(
                token => token.TokenHash == tokenHash,
                ct);

        if (storedToken is null)
        {
            return Result.Success();
        }
        List<RefreshToken> familyTokens = await _db.RefreshTokens
    .Where(token => token.FamilyId == storedToken.FamilyId)
    .ToListAsync(ct);

        DateTimeOffset now = _timeProvider.GetUtcNow();

        foreach (RefreshToken familyToken in familyTokens)
        {
            if (!familyToken.IsRevoked)
            {
                familyToken.Revoke(now);
            }
        }

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<UserDto>> GetCurrentUserAsync(CancellationToken ct)
    {
        Guid? currentUserId = _currentUser.UserId;
        if (!_currentUser.IsAuthenticated || currentUserId is null || currentUserId == Guid.Empty)
        {
            return Result<UserDto>.Failure(AuthErrors.SessionExpired);
        }
        User? user = await _db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(
                user => user.Id == currentUserId.Value,
                ct);

        if (user is null || user.Status != UserStatus.Active)
        {
            return Result<UserDto>.Failure(AuthErrors.SessionExpired);
        }

        return Result<UserDto>.Success(UserDto.From(user));
    }
}
