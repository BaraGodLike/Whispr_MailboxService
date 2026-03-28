using Application;
using StackExchange.Redis;

namespace Infrastructure.EF;

public static class MailboxRedis
{
    private static RedisKey MbKey(Guid mailbox) => $"mb:{mailbox:N}";
    private static RedisKey UKey(string user) => $"user:{user}";
    private static readonly RedisValue FieldUser = "u";
    private static readonly RedisValue FieldExp  = "exp";
    private static readonly RedisValue FieldMailbox = "mailbox";
    
    public static async Task SetMailboxAsync(
        IDatabase db,
        Guid mailbox,
        string user,
        DateTimeOffset expiresAt,
        CancellationToken ctn = default)
    {
        var exp = expiresAt.ToUnixTimeSeconds();

        var ttl = expiresAt - DateTime.UtcNow;
        if (ttl <= TimeSpan.Zero)
            ttl = TimeSpan.FromSeconds(1);

        var key = MbKey(mailbox);
        
        await db.HashSetAsync(key, [
            new HashEntry(FieldUser, user),
            new HashEntry(FieldExp,  exp)
        ]);

        await db.KeyExpireAsync(key, ttl + TimeSpan.FromHours(1));
    }

    public static async Task SetUserAsync(
        IDatabase db,
        Guid mailbox,
        string user,
        DateTimeOffset expiresAt,
        CancellationToken ctn = default)
    {
        var exp = expiresAt.ToUnixTimeSeconds();

        var ttl = expiresAt - DateTime.UtcNow;
        if (ttl <= TimeSpan.Zero)
            ttl = TimeSpan.FromSeconds(1);

        var key = UKey(user);

        await db.HashSetAsync(key, [
            new HashEntry(FieldMailbox, mailbox.ToString("N")),
            new HashEntry(FieldExp, exp)
        ]);
        
        await db.KeyExpireAsync(key, ttl + TimeSpan.FromHours(1));
    }

    public static async Task<MailboxOwner?> TryGetUserAsync(IDatabase db, Guid mailbox)
    {
        var key = MbKey(mailbox);

        var vals = await db.HashGetAsync(key, [FieldUser, FieldExp]);

        if (vals.Length != 2 || vals[0].IsNull)
            return null;

        var user = (string)vals[0]!;

        return !vals[1].TryParse(out long expUnix)
            ? null
            : new MailboxOwner(user, DateTimeOffset.FromUnixTimeSeconds(expUnix).DateTime);
    }

    public static async Task<MailboxMap?> TryGetMailboxAsync(IDatabase db, string user)
    {
        var key = UKey(user);
        
        var vals = await db.HashGetAsync(key, [FieldMailbox, FieldExp]);
        
        if (vals.Length != 2 || vals[0].IsNull)
            return null;

        return Guid.TryParse((string)vals[0]!, out var mb) &&
               vals[1].TryParse(out long expUnix)
               ? new MailboxMap(mb, DateTimeOffset.FromUnixTimeSeconds(expUnix).DateTime)
               : null;
    }
}