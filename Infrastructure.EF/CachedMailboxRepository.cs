using Application;
using Model;
using StackExchange.Redis;


namespace Infrastructure.EF;

public class CachedMailboxRepository(IMailboxRepository sqlRepository, IDatabase cache) 
    : IMailboxRepository
{
    public async Task<MailboxOwner> GetUserByMailboxAsync(Guid mailboxAddress, CancellationToken ctn)
    {
        var cached = await MailboxRedis.TryGetUserAsync(cache, mailboxAddress);
        if (cached.HasValue) return cached.Value;
        
        var user = await sqlRepository.GetUserByMailboxAsync(mailboxAddress, ctn);
        await MailboxRedis.SetMailboxAsync(cache, mailboxAddress, user.User, user.ExpiresAt, ctn);
        return user;
    }

    public async Task<MailboxMap> GetLastMailboxForUserAsync(string user, CancellationToken ctn)
    {
        var cached = await MailboxRedis.TryGetMailboxAsync(cache, user);
        if (cached.HasValue) return cached.Value;
        
        var mailbox = await sqlRepository.GetLastMailboxForUserAsync(user, ctn);
        await MailboxRedis.SetUserAsync(cache, mailbox.Mailbox, user, mailbox.ExpiresAt, ctn);
        return mailbox;
    }

    public async Task CreateMailboxAsync(UserMailbox userMailbox, CancellationToken ctn)
    {
        await sqlRepository.CreateMailboxAsync(userMailbox, ctn);
    
        await MailboxRedis.SetUserAsync(
            cache,
            userMailbox.MailboxAddress,
            userMailbox.User, 
            userMailbox.ExpiresAt,
            ctn);
        
        await MailboxRedis.SetMailboxAsync(
            cache,
            userMailbox.MailboxAddress,
            userMailbox.User, 
            userMailbox.ExpiresAt,
            ctn);
    }

    public Task<List<string>> DeleteExpiredMailboxesAsync(CancellationToken ctn) =>
        sqlRepository.DeleteExpiredMailboxesAsync(ctn);
}