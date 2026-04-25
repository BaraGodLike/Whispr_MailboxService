using Microsoft.Extensions.Logging;

namespace Application;

public sealed class MailboxService(
    IMailboxRepository repository,
    IDateTimeProvider dateTimeProvider,
    ILogger<MailboxService> logger)
    : IMailboxService
{
    public async Task<MailboxOwner?> GetUserByMailboxAsync(Guid mailboxAddress, CancellationToken ctn)
    {
        try
        {
            var owner = await repository.GetUserByMailboxAsync(mailboxAddress, ctn);
            if (owner == default)
            {
                logger.LogWarning("Owner lookup returned no mailbox owner.");
                return null;
            }

            var today = dateTimeProvider.GetCurrentDate();
            if (MailboxPolicy.IsOwnerMappingActive(today, owner.ExpiresDay))
                return owner;

            logger.LogWarning("Owner mapping is expired.");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError("Mailbox owner lookup failed. ExceptionType: {ExceptionType}.", ex.GetType().FullName);
            throw;
        }
    }

    public async Task<MailboxMap?> GetCurrentMailboxForUserAsync(string user, CancellationToken ctn)
    {
        try
        {
            var schedule = MailboxPolicy.BuildSchedule(dateTimeProvider.GetCurrentDate());
            var map = await repository.GetCurrentMailboxForUserAsync(user, schedule.CurrentExpiresDay, ctn);
            if (map != default)
                return map;

            logger.LogWarning("Current mailbox lookup returned no mailbox.");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError("Current mailbox lookup failed. ExceptionType: {ExceptionType}.", ex.GetType().FullName);
            throw;
        }
    }

    public async Task<MailboxMap> CreateMailboxAsync(string user, CancellationToken ctn)
    {
        try
        {
            var schedule = MailboxPolicy.BuildSchedule(dateTimeProvider.GetCurrentDate());
            return await repository.CreateMailboxAsync(user, schedule, ctn);
        }
        catch (Exception ex)
        {
            logger.LogError("Mailbox creation failed. ExceptionType: {ExceptionType}.", ex.GetType().FullName);
            throw;
        }
    }
}
