using Application;
using Model;
using StackExchange.Redis;

namespace Infrastructure.Storage;

public sealed class CachedMailboxRepository(IMailboxRepository sqlRepository, IDatabase cache)
    : IMailboxRepository
{
    public async Task<MailboxOwner> GetUserByMailboxAsync(Guid mailboxAddress, CancellationToken ctn)
    {
        var cached = await MailboxRedis.TryGetUserAsync(cache, mailboxAddress);
        if (cached.HasValue) return cached.Value;

        var owner = await sqlRepository.GetUserByMailboxAsync(mailboxAddress, ctn);
        
        if (owner != default)
            await MailboxRedis.SetMailboxAsync(
                cache,
                mailboxAddress,
                owner.User,
                owner.ExpiresDay,
                ctn);
        return owner;
    }

    public async Task<MailboxMap> GetCurrentMailboxForUserAsync(string user, CancellationToken ctn)
    {
        var cached = await MailboxRedis.TryGetMailboxAsync(cache, user);
        if (cached.HasValue) return cached.Value;

        var mailbox = await sqlRepository.GetCurrentMailboxForUserAsync(user, ctn);
        
        if (mailbox != default)
            await MailboxRedis.SetUserAsync(cache, mailbox.Mailbox, user, mailbox.ExpiresDay, ctn);
        return mailbox;
    }

    public async Task CreateMailboxAsync(UserMailbox userMailbox, CancellationToken ctn)
    {
        await sqlRepository.CreateMailboxAsync(userMailbox, ctn);

        await MailboxRedis.SetUserAsync(
            cache,
            userMailbox.MailboxAddress,
            userMailbox.User,
            userMailbox.ExpiresDay,
            ctn);

        await MailboxRedis.SetMailboxAsync(
            cache,
            userMailbox.MailboxAddress,
            userMailbox.User,
            userMailbox.ExpiresDay,
            ctn);
    }

    public Task EnsurePartitionsAsync(DateOnly from, int daysAhead, CancellationToken ctn)
        => sqlRepository.EnsurePartitionsAsync(from, daysAhead, ctn);

    public Task DropPartitionAsync(DateOnly day, CancellationToken ctn)
        => sqlRepository.DropPartitionAsync(day, ctn);
}