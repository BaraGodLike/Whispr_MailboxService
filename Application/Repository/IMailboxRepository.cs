namespace Application;

public interface IMailboxRepository
{
    Task<MailboxOwner> GetUserByMailboxAsync(Guid mailboxAddress, CancellationToken ctn);
    Task<MailboxMap> GetCurrentMailboxForUserAsync(string user, DateOnly expiresDay, CancellationToken ctn);
    Task<MailboxMap> CreateMailboxAsync(string user, MailboxSchedule schedule, CancellationToken ctn);
    Task RotateMailboxesAsync(MailboxSchedule schedule, CancellationToken ctn);
}
