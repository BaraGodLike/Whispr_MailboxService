using Application;
using StackExchange.Redis;

namespace Infrastructure.Storage;

public static class MailboxRedis
{
    private static RedisKey MbKey(Guid mailbox) => $"mb:{mailbox:N}";
    private static RedisKey UKey(string user) => $"user:{user}";

    private static readonly RedisValue FieldUser = "u";
    private static readonly RedisValue FieldExpDay = "expd"; // int yyyymmdd
    private static readonly RedisValue FieldMailbox = "mb";
    private static DateOnly CurrentMailboxExpiresDay() => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(6);

    public static async Task SetMailboxAsync(
        IDatabase db,
        Guid mailbox,
        string user,
        DateOnly expiresDay,
        CancellationToken ctn = default)
    {
        var key = MbKey(mailbox);
        var expd = ToYyyyMmDd(expiresDay);
        var ttl = TtlToRotationUtc(expiresDay.AddDays(1));

        await db.HashSetAsync(key, [
            new HashEntry(FieldUser, user),
            new HashEntry(FieldExpDay, expd)
        ]);

        await db.KeyExpireAsync(key, ttl);
    }

    public static async Task SetUserAsync(
        IDatabase db,
        Guid mailbox,
        string user,
        DateOnly expiresDay,
        CancellationToken ctn = default)
    {
        var key = UKey(user);
        var expd = ToYyyyMmDd(expiresDay);
        var ttl = TtlToRotationUtc(expiresDay.AddDays(-5));

        await db.HashSetAsync(key, [
            new HashEntry(FieldMailbox, mailbox.ToString("N")),
            new HashEntry(FieldExpDay, expd)
        ]);

        await db.KeyExpireAsync(key, ttl);
    }

    public static async Task<MailboxOwner?> TryGetUserAsync(IDatabase db, Guid mailbox)
    {
        var key = MbKey(mailbox);
        var vals = await db.HashGetAsync(key, [FieldUser, FieldExpDay]);

        if (vals.Length != 2 || vals[0].IsNullOrEmpty || vals[1].IsNullOrEmpty)
            return null;

        var user = (string)vals[0]!;
        if (!vals[1].TryParse(out int expd))
            return null;

        var expiresDay = FromYyyyMmDd(expd);

        if (DateOnly.FromDateTime(DateTime.UtcNow) >= expiresDay)
            return null;

        return new MailboxOwner(user, expiresDay);
    }

    public static async Task<MailboxMap?> TryGetMailboxAsync(IDatabase db, string user)
    {
        var key = UKey(user);
        var vals = await db.HashGetAsync(key, [FieldMailbox, FieldExpDay]);

        if (vals.Length != 2 || vals[0].IsNullOrEmpty || vals[1].IsNullOrEmpty)
            return null;

        if (!Guid.TryParse((string)vals[0]!, out var mb))
            return null;

        if (!vals[1].TryParse(out int expd))
            return null;

        var expiresDay = FromYyyyMmDd(expd);

        if (expiresDay != CurrentMailboxExpiresDay())
            return null;

        return new MailboxMap(mb, expiresDay);
    }

    private static int ToYyyyMmDd(DateOnly d) => d.Year * 10000 + d.Month * 100 + d.Day;

    private static DateOnly FromYyyyMmDd(int x)
    {
        var year = x / 10000;
        var month = (x / 100) % 100;
        var day = x % 100;
        return new DateOnly(year, month, day);
    }

    private static TimeSpan TtlToRotationUtc(DateOnly expiresDay)
    {
        var until = expiresDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var ttl = until - DateTime.UtcNow;
        if (ttl <= TimeSpan.Zero) ttl = TimeSpan.FromSeconds(1);

        return ttl + TimeSpan.FromMinutes(10);
    }
}
