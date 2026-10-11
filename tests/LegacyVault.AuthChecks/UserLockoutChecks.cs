using LegacyVault.Domain.Identity;

namespace LegacyVault.AuthChecks;

/// <summary>Kiểm tra hành vi lockout mà không cần database.</summary>
internal static class UserLockoutChecks
{
    /// <summary>Kiểm tra ngưỡng khóa, thời hạn, gọi lặp và reset khi thành công.</summary>
    /// <exception cref="InvalidOperationException">Hành vi không đúng policy.</exception>
    public static void Run()
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;

        User user = User.Create(
            "owner@example.test",
            "Fixture User",
            "fixture-hash",
            UserRole.Owner,
            UserStatus.Active,
            now);

        Assert(!user.IsLockedOut(now), "New user must not be locked.");
        Assert(user.LockoutRemainingSeconds(now) == 0, "New user must have no wait.");

        for (int attempt = 1; attempt <= User.MaxFailedLoginAttempts; attempt++)
        {
            int remaining = user.RegisterFailedLogin(now);

            Assert(
                remaining == User.MaxFailedLoginAttempts - attempt,
                "Remaining attempts must decrease correctly.");
        }

        DateTimeOffset expectedEnd = now.Add(User.LockoutDuration);

        Assert(user.IsLockedOut(now), "User must lock at the threshold.");
        Assert(user.LockoutEndsAt == expectedEnd, "Lockout end must match policy.");

        user.RegisterFailedLogin(now.AddSeconds(1));

        Assert(
            user.LockoutEndsAt == expectedEnd,
            "Repeated failure during lockout must not extend it.");

        Assert(
            user.LockoutRemainingSeconds(expectedEnd.AddMilliseconds(-1)) == 1,
            "Fractional remaining seconds must round up.");

        Assert(!user.IsLockedOut(expectedEnd), "User must unlock exactly at expiry.");
        Assert(user.LockoutRemainingSeconds(expectedEnd) == 0, "Expired lockout must have no wait.");

        int remainingAfterExpiry = user.RegisterFailedLogin(expectedEnd);

        Assert(
            remainingAfterExpiry == User.MaxFailedLoginAttempts - 1,
            "First failure after expiry must start a fresh count.");

        Assert(user.FailedLoginCount == 1, "Fresh failure count must be one.");
        Assert(user.LockoutEndsAt is null, "Expired lockout must be cleared.");

        user.RegisterSuccessfulLogin(expectedEnd);

        Assert(user.FailedLoginCount == 0, "Success must reset failure count.");
        Assert(user.LockoutEndsAt is null, "Success must clear lockout.");
        Assert(user.LastLoginAt == expectedEnd, "Success must record login time.");
        Assert(user.UpdatedAt == expectedEnd, "Success must record update time.");

        Console.WriteLine("PASS: User lockout behavior.");
    }

    /// <summary>Dừng bộ kiểm tra khi điều kiện không đạt.</summary>
    /// <param name="condition">Điều kiện cần đúng.</param>
    /// <param name="message">Thông báo lỗi kiểm tra.</param>
    /// <exception cref="InvalidOperationException">Điều kiện không đạt.</exception>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}