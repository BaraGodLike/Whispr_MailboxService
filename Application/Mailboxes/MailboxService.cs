namespace Application;

public sealed class MailboxService(IMailboxRepository repository, IDateTimeProvider dateTimeProvider) : IMailboxService
{
    public async Task<MailboxOwner?> GetUserByMailboxAsync(Guid mailboxAddress, CancellationToken ctn)
    {
        var owner = await repository.GetUserByMailboxAsync(mailboxAddress, ctn);
        if (owner == default)
            return null;

        var today = dateTimeProvider.GetCurrentDate();
        return MailboxPolicy.IsOwnerMappingActive(today, owner.ExpiresDay)
            ? owner
            : null;
    }

    public async Task<MailboxMap?> GetCurrentMailboxForUserAsync(string user, CancellationToken ctn)
    {
        var schedule = MailboxPolicy.BuildSchedule(dateTimeProvider.GetCurrentDate());
        var map = await repository.GetCurrentMailboxForUserAsync(user, schedule.CurrentExpiresDay, ctn);
        return map == default ? null : map;
    }

    public Task<MailboxMap> CreateMailboxAsync(string user, CancellationToken ctn)
    {
        var schedule = MailboxPolicy.BuildSchedule(dateTimeProvider.GetCurrentDate());
        return repository.CreateMailboxAsync(user, schedule, ctn);
    }
}
