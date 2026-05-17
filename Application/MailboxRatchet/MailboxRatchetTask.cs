namespace Application;

public class MailboxRatchetTask(IMailboxMaintenanceService maintenanceService) : IBackgroundTask
{
    public Task Run(CancellationToken ctn) => maintenanceService.RunDailyRotationAsync(ctn);
}
