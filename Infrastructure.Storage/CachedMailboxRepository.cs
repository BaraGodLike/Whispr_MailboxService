using Application;
using StackExchange.Redis;

namespace Infrastructure.Storage;

public sealed class CachedMailboxRepository(
    IMailboxRepository sqlRepository,
    IDatabase cache,
    IDateTimeProvider dateTimeProvider)
    : IMailboxRepository
{
    public async Task<MailboxOwner> GetUserByMailboxAsync(Guid mailboxAddress, CancellationToken ctn)
    {
        var now = dateTimeProvider.GetCurrentDateTime();
        var today = DateOnly.FromDateTime(now);
        var cached = await MailboxRedis.TryGetUserAsync(cache, mailboxAddress, today);
        if (cached.HasValue)
            return cached.Value;

        var owner = await sqlRepository.GetUserByMailboxAsync(mailboxAddress, ctn);

        if (owner == default) return owner;

        await MailboxRedis.SetMailboxAsync(
            cache,
            mailboxAddress,
            owner.User,
            owner.ExpiresDay,
            now,
            ctn);

        return owner;
    }

    public async Task<MailboxMap> GetCurrentMailboxForUserAsync(string user, DateOnly expiresDay, CancellationToken ctn)
    {
        var cached = await MailboxRedis.TryGetMailboxAsync(cache, user, expiresDay);
        if (cached.HasValue)
            return cached.Value;

        var mailbox = await sqlRepository.GetCurrentMailboxForUserAsync(user, expiresDay, ctn);

        if (mailbox == default) return mailbox;
        
        var now = dateTimeProvider.GetCurrentDateTime();
        await MailboxRedis.SetUserAsync(
            cache,
            mailbox.Mailbox,
            user,
            mailbox.ExpiresDay,
            now,
            ctn);

        return mailbox;
    }

    public async Task<MailboxMap> CreateMailboxAsync(string user, MailboxSchedule schedule, CancellationToken ctn)
    {
        var mailbox = await sqlRepository.CreateMailboxAsync(user, schedule, ctn);
        var now = dateTimeProvider.GetCurrentDateTime();

        await MailboxRedis.SetMailboxAsync(
            cache,
            mailbox.Mailbox,
            user,
            mailbox.ExpiresDay,
            now,
            ctn);

        await MailboxRedis.SetUserAsync(
            cache,
            mailbox.Mailbox,
            user,
            mailbox.ExpiresDay,
            now,
            ctn);

        return mailbox;
    }

    public Task RotateMailboxesAsync(MailboxSchedule schedule, CancellationToken ctn) =>
        sqlRepository.RotateMailboxesAsync(schedule, ctn);
}
