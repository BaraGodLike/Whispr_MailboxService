namespace Application;

public interface IMailboxService
{
    Task<MailboxOwner?> GetUserByMailboxAsync(Guid mailboxAddress, CancellationToken ctn);
    Task<MailboxMap?> GetCurrentMailboxForUserAsync(string user, CancellationToken ctn);
    Task<MailboxMap> CreateMailboxAsync(string user, CancellationToken ctn);
}
