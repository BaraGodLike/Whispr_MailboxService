using Application;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Infrastructure.Storage;

public sealed class CachedMailboxRepository(
    IMailboxRepository sqlRepository,
    IDatabase cache,
    IDateTimeProvider dateTimeProvider,
    IServiceInstanceMetadata serviceInstanceMetadata,
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
                "Mailbox owner cache read failed. Service: {Service}, Instance: {Instance}, Method: {Method}, ExceptionType: {ExceptionType}.",
                serviceInstanceMetadata.ServiceName,
                serviceInstanceMetadata.InstanceId,
                nameof(GetUserByMailboxAsync),
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
                "Mailbox owner cache write failed. Service: {Service}, Instance: {Instance}, Method: {Method}, ExceptionType: {ExceptionType}.",
                serviceInstanceMetadata.ServiceName,
                serviceInstanceMetadata.InstanceId,
                nameof(GetUserByMailboxAsync),
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
                "Current mailbox cache read failed. Service: {Service}, Instance: {Instance}, Method: {Method}, ExpectedExpiresDay: {ExpectedExpiresDay}, ExceptionType: {ExceptionType}.",
                serviceInstanceMetadata.ServiceName,
                serviceInstanceMetadata.InstanceId,
                nameof(GetCurrentMailboxForUserAsync),
                expiresDay,
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
                "Current mailbox cache write failed. Service: {Service}, Instance: {Instance}, Method: {Method}, ExceptionType: {ExceptionType}.",
                serviceInstanceMetadata.ServiceName,
                serviceInstanceMetadata.InstanceId,
                nameof(GetCurrentMailboxForUserAsync),
                ex.GetType().FullName);
        }

        return mailbox;
    }

    public Task<bool> RegisterUserAsync(
        string user,
        string authAlg,
        byte[] publicKey,
        MailboxSchedule schedule,
        CancellationToken ctn) =>
        sqlRepository.RegisterUserAsync(user, authAlg, publicKey, schedule, ctn);

    public Task<UserAuthInfo?> GetUserAuthInfoAsync(string user, CancellationToken ctn) =>
        sqlRepository.GetUserAuthInfoAsync(user, ctn);

    public Task<IReadOnlyList<MailboxMap>> GetActiveMailboxesForUserAsync(
        string user,
        DateOnly minExpiresDay,
        DateOnly maxExpiresDay,
        CancellationToken ctn) =>
        sqlRepository.GetActiveMailboxesForUserAsync(user, minExpiresDay, maxExpiresDay, ctn);

    public Task RotateMailboxesAsync(MailboxSchedule schedule, CancellationToken ctn) =>
        sqlRepository.RotateMailboxesAsync(schedule, ctn);
}
