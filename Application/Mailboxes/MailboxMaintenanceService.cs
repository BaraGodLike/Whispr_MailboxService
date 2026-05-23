using Microsoft.Extensions.Logging;

namespace Application;

public sealed class MailboxMaintenanceService(
    IMailboxRepository repository,
    IDateTimeProvider dateTimeProvider,
    IServiceInstanceMetadata serviceInstanceMetadata,
    ILogger<MailboxMaintenanceService> logger)
    : IMailboxMaintenanceService
{
    public async Task RunDailyRotationAsync(CancellationToken ctn)
    {
        var schedule = MailboxPolicy.BuildSchedule(dateTimeProvider.GetCurrentDate());

        try
        {
            logger.LogInformation(
                "Mailbox rotation started. Service: {Service}, Instance: {Instance}, Today: {Today}, CurrentExpiresDay: {CurrentExpiresDay}, NextExpiresDay: {NextExpiresDay}, ExpiredPartitionDay: {ExpiredPartitionDay}.",
                serviceInstanceMetadata.ServiceName,
                serviceInstanceMetadata.InstanceId,
                schedule.Today,
                schedule.CurrentExpiresDay,
                schedule.NextExpiresDay,
                schedule.ExpiredPartitionDay);

            await repository.RotateMailboxesAsync(schedule, ctn);

            logger.LogInformation(
                "Mailbox rotation completed. Service: {Service}, Instance: {Instance}, Today: {Today}, CurrentExpiresDay: {CurrentExpiresDay}, NextExpiresDay: {NextExpiresDay}, ExpiredPartitionDay: {ExpiredPartitionDay}.",
                serviceInstanceMetadata.ServiceName,
                serviceInstanceMetadata.InstanceId,
                schedule.Today,
                schedule.CurrentExpiresDay,
                schedule.NextExpiresDay,
                schedule.ExpiredPartitionDay);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Mailbox rotation failed. Service: {Service}, Instance: {Instance}, ExceptionType: {ExceptionType}.",
                serviceInstanceMetadata.ServiceName,
                serviceInstanceMetadata.InstanceId,
                ex.GetType().FullName);
            throw;
        }
    }
}
