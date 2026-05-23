namespace Application;

public interface IMailboxService
{
    Task<MailboxOwner?> GetUserByMailboxAsync(Guid mailboxAddress, CancellationToken ctn);
    Task<MailboxMap?> GetCurrentMailboxForUserAsync(string user, CancellationToken ctn);
    Task<RegisterUserResult> RegisterUserAsync(string user, string authAlg, byte[] publicKey, CancellationToken ctn);
    Task<RealtimeAuthChallenge?> BeginRealtimeAuthAsync(string user, CancellationToken ctn);
    Task<CompleteRealtimeAuthResult> CompleteRealtimeAuthAsync(
        string user,
        string nonce,
        byte[] nonceBytes,
        byte[] signature,
        CancellationToken ctn);
}
