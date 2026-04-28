using Application;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Infrastructure.Storage;

public sealed class CachedMailboxRepository(
    IMailboxRepository sqlRepository,
    IDatabase cache,
    IDateTimeProvider dateTimeProvider,
    ILogger<CachedMailboxRepository> logger)
    : IMailboxRepository
{
    public async Task<MailboxOwner> GetUserByMailboxAsync(Guid mailboxAddress, CancellationToken ctn)
    {
        var now = dateTimeProvider.GetCurrentDateTime();
        var today = DateOnly.FromDateTime(now);

        try
        {
            var cached = await MailboxRedis.TryGetUserAsync(cache, mailboxAddress, today);
            if (cached.HasValue)
                return cached.Value;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Mailbox owner cache read failed. Falling back to storage. ExceptionType: {ExceptionType}.",
                ex.GetType().FullName);
        }

        var owner = await sqlRepository.GetUserByMailboxAsync(mailboxAddress, ctn);

        if (owner == default) return owner;

        try
        {
            await MailboxRedis.SetMailboxAsync(
                cache,
                mailboxAddress,
                owner.User,
                owner.ExpiresDay,
                now,
                ctn);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Mailbox owner cache write failed. Continuing without cache. ExceptionType: {ExceptionType}.",
                ex.GetType().FullName);
        }

        return owner;
    }

    public async Task<MailboxMap> GetCurrentMailboxForUserAsync(string user, DateOnly expiresDay, CancellationToken ctn)
    {
        try
        {
            var cached = await MailboxRedis.TryGetMailboxAsync(cache, user, expiresDay);
            if (cached.HasValue)
                return cached.Value;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Current mailbox cache read failed. Falling back to storage. ExceptionType: {ExceptionType}.",
                ex.GetType().FullName);
        }

        var mailbox = await sqlRepository.GetCurrentMailboxForUserAsync(user, expiresDay, ctn);

        if (mailbox == default) return mailbox;

        var now = dateTimeProvider.GetCurrentDateTime();
        try
        {
            await MailboxRedis.SetUserAsync(
                cache,
                mailbox.Mailbox,
                user,
                mailbox.ExpiresDay,
                now,
                ctn);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Current mailbox cache write failed. Continuing without cache. ExceptionType: {ExceptionType}.",
                ex.GetType().FullName);
        }

        return mailbox;
    }

    public async Task<MailboxMap> CreateMailboxAsync(string user, MailboxSchedule schedule, CancellationToken ctn)
    {
        var mailbox = await sqlRepository.CreateMailboxAsync(user, schedule, ctn);
        var now = dateTimeProvider.GetCurrentDateTime();

        try
        {
            await MailboxRedis.SetMailboxAsync(
                cache,
                mailbox.Mailbox,
                user,
                mailbox.ExpiresDay,
                now,
                ctn);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Mailbox owner cache write failed. Continuing without cache. ExceptionType: {ExceptionType}.",
                ex.GetType().FullName);
        }

        try
        {
            await MailboxRedis.SetUserAsync(
                cache,
                mailbox.Mailbox,
                user,
                mailbox.ExpiresDay,
                now,
                ctn);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Current mailbox cache write failed. Continuing without cache. ExceptionType: {ExceptionType}.",
                ex.GetType().FullName);
        }

        return mailbox;
    }

    public Task RotateMailboxesAsync(MailboxSchedule schedule, CancellationToken ctn) =>
        sqlRepository.RotateMailboxesAsync(schedule, ctn);
}
