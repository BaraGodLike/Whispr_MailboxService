using Microsoft.Extensions.Logging;

namespace Application;

public sealed class MailboxMaintenanceService(
    IMailboxRepository repository,
    IDateTimeProvider dateTimeProvider,
    ILogger<MailboxMaintenanceService> logger)
    : IMailboxMaintenanceService
{
    public async Task RunDailyRotationAsync(CancellationToken ctn)
    {
        var schedule = MailboxPolicy.BuildSchedule(dateTimeProvider.GetCurrentDate());

        try
        {
            logger.LogInformation(
                "Daily mailbox rotation started. Today: {Today}, CurrentExpiresDay: {CurrentExpiresDay}, NextExpiresDay: {NextExpiresDay}, ExpiredPartitionDay: {ExpiredPartitionDay}.",
                schedule.Today,
                schedule.CurrentExpiresDay,
                schedule.NextExpiresDay,
                schedule.ExpiredPartitionDay);

            await repository.RotateMailboxesAsync(schedule, ctn);

            logger.LogInformation(
                "Daily mailbox rotation completed. Today: {Today}, CurrentExpiresDay: {CurrentExpiresDay}, NextExpiresDay: {NextExpiresDay}, ExpiredPartitionDay: {ExpiredPartitionDay}.",
                schedule.Today,
                schedule.CurrentExpiresDay,
                schedule.NextExpiresDay,
                schedule.ExpiredPartitionDay);
        }
        catch (Exception ex)
        {
            logger.LogError("Daily mailbox rotation failed. ExceptionType: {ExceptionType}.", ex.GetType().FullName);
            throw;
        }
    }
}
