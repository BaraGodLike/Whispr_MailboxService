namespace Application;

public sealed class MailboxMaintenanceService(IMailboxRepository repository, IDateTimeProvider dateTimeProvider)
    : IMailboxMaintenanceService
{
    public Task RunDailyRotationAsync(CancellationToken ctn)
    {
        var schedule = MailboxPolicy.BuildSchedule(dateTimeProvider.GetCurrentDate());
        return repository.RotateMailboxesAsync(schedule, ctn);
    }
}
