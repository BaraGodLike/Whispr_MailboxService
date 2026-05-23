namespace Application;

public interface IRealtimeAuthNonceStore
{
    Task StoreNonceAsync(string nonce, string user, TimeSpan ttl, CancellationToken ctn);
    Task<string?> ConsumeNonceAsync(string nonce, CancellationToken ctn);
}
