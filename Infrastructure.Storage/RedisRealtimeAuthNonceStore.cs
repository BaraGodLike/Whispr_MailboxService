using Application;
using StackExchange.Redis;

namespace Infrastructure.Storage;

public sealed class RedisRealtimeAuthNonceStore(IDatabase database) : IRealtimeAuthNonceStore
{
    private static RedisKey RtAuthKey(string nonce) => $"rtauth:{nonce}";

    public Task StoreNonceAsync(string nonce, string user, TimeSpan ttl, CancellationToken ctn)
    {
        if (ttl <= TimeSpan.Zero)
            ttl = TimeSpan.FromSeconds(1);

        return database.StringSetAsync(RtAuthKey(nonce), user, ttl);
    }

    public async Task<string?> ConsumeNonceAsync(string nonce, CancellationToken ctn)
    {
        var value = await database.StringGetDeleteAsync(RtAuthKey(nonce));
        return value.IsNullOrEmpty ? null : (string?)value;
    }
}
