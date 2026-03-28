using Model;

namespace Application;

public interface IMailboxRepository
{
    Task<MailboxOwner> GetUserByMailboxAsync(Guid mailboxAddress, CancellationToken ctn);
    Task<MailboxMap> GetCurrentMailboxForUserAsync(string user, CancellationToken ctn);
    Task CreateMailboxAsync(UserMailbox userMailbox, CancellationToken ctn);
    Task EnsurePartitionsAsync(DateOnly from, int daysAhead, CancellationToken ctn);
    Task DropPartitionAsync(DateOnly day, CancellationToken ctn);
}