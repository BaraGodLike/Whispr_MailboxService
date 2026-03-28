using Model;

namespace Application;

public interface IMailboxRepository
{
    public Task<MailboxOwner> GetUserByMailboxAsync(Guid mailboxAddress, CancellationToken ctn);
    public Task<MailboxMap> GetLastMailboxForUserAsync(string user, CancellationToken ctn);
    public Task CreateMailboxAsync(UserMailbox userMailbox, CancellationToken ctn);
    public Task<List<string>> DeleteExpiredMailboxesAsync(CancellationToken ctn);
    
}