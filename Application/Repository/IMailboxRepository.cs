namespace Application;

public interface IMailboxRepository
{
    Task<MailboxOwner> GetUserByMailboxAsync(Guid mailboxAddress, CancellationToken ctn);
    Task<MailboxMap> GetCurrentMailboxForUserAsync(string user, DateOnly expiresDay, CancellationToken ctn);
    Task<bool> RegisterUserAsync(string user, string authAlg, byte[] publicKey, MailboxSchedule schedule, CancellationToken ctn);
    Task<UserAuthInfo?> GetUserAuthInfoAsync(string user, CancellationToken ctn);
    Task<IReadOnlyList<MailboxMap>> GetActiveMailboxesForUserAsync(
        string user,
        DateOnly minExpiresDay,
        DateOnly maxExpiresDay,
        CancellationToken ctn);
    Task RotateMailboxesAsync(MailboxSchedule schedule, CancellationToken ctn);
}
