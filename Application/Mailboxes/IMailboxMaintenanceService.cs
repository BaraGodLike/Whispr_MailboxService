namespace Application;

public interface IMailboxMaintenanceService
{
    Task RunDailyRotationAsync(CancellationToken ctn);
}
