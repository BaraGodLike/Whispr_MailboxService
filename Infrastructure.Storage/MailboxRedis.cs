using Application;
using StackExchange.Redis;

namespace Infrastructure.Storage;

public static class MailboxRedis
{
    private static RedisKey MbKey(Guid mailbox) => $"mb:{mailbox:N}";
    private static RedisKey UKey(string user) => $"user:{user}";

    private static readonly RedisValue FieldUser = "u";
    private static readonly RedisValue FieldExpDay = "expd";
    private static readonly RedisValue FieldMailbox = "mb";

    public static async Task SetMailboxAsync(
        IDatabase db,
        Guid mailbox,
        string user,
        DateOnly expiresDay,
        DateTime nowUtc,
        CancellationToken ctn = default)
    {
        var key = MbKey(mailbox);
        var ttl = TtlToRotationUtc(
            expiresDay.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            nowUtc);

        await db.HashSetAsync(key, [
            new HashEntry(FieldUser, user),
            new HashEntry(FieldExpDay, ToYyyyMmDd(expiresDay))
        ]);

        await db.KeyExpireAsync(key, ttl);
    }

    public static async Task SetUserAsync(
        IDatabase db,
        Guid mailbox,
        string user,
        DateOnly expiresDay,
        DateTime nowUtc,
        CancellationToken ctn = default)
    {
        var key = UKey(user);
        var ttl = TtlToRotationUtc(MailboxPolicy.GetClientRefreshAfterUtc(expiresDay).AddDays(1).Date, nowUtc);

        await db.HashSetAsync(key, [
            new HashEntry(FieldMailbox, mailbox.ToString("N")),
            new HashEntry(FieldExpDay, ToYyyyMmDd(expiresDay))
        ]);

        await db.KeyExpireAsync(key, ttl);
    }

    public static async Task<MailboxOwner?> TryGetUserAsync(IDatabase db, Guid mailbox, DateOnly today)
    {
        var key = MbKey(mailbox);
        var values = await db.HashGetAsync(key, [FieldUser, FieldExpDay]);

        if (values.Length != 2 || values[0].IsNullOrEmpty || values[1].IsNullOrEmpty)
            return null;

        if (!values[1].TryParse(out int expiresDayRaw))
            return null;

        var expiresDay = FromYyyyMmDd(expiresDayRaw);
        if (!MailboxPolicy.IsOwnerMappingActive(today, expiresDay))
            return null;

        return new MailboxOwner((string)values[0]!, expiresDay);
    }

    public static async Task<MailboxMap?> TryGetMailboxAsync(IDatabase db, string user, DateOnly expectedExpiresDay)
    {
        var key = UKey(user);
        var values = await db.HashGetAsync(key, [FieldMailbox, FieldExpDay]);

        if (values.Length != 2 || values[0].IsNullOrEmpty || values[1].IsNullOrEmpty)
            return null;

        if (!Guid.TryParse((string)values[0]!, out var mailbox))
            return null;

        if (!values[1].TryParse(out int expiresDayRaw))
            return null;

        var expiresDay = FromYyyyMmDd(expiresDayRaw);
        if (expiresDay != expectedExpiresDay)
            return null;

        return new MailboxMap(mailbox, expiresDay);
    }

    private static int ToYyyyMmDd(DateOnly date) => date.Year * 10000 + date.Month * 100 + date.Day;

    private static DateOnly FromYyyyMmDd(int value)
    {
        var year = value / 10000;
        var month = (value / 100) % 100;
        var day = value % 100;
        return new DateOnly(year, month, day);
    }

    private static TimeSpan TtlToRotationUtc(DateTime untilUtc, DateTime nowUtc)
    {
        var ttl = untilUtc - nowUtc;
        if (ttl <= TimeSpan.Zero)
            ttl = TimeSpan.FromSeconds(1);

        return ttl + TimeSpan.FromMinutes(10);
    }
}
