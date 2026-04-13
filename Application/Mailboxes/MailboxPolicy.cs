namespace Application;

public static class MailboxPolicy
{
    public static MailboxSchedule BuildSchedule(DateOnly today) =>
        new(
            Today: today,
            CurrentExpiresDay: today.AddDays(6),
            NextExpiresDay: today.AddDays(7),
            ExpiredPartitionDay: today.AddDays(-1));

    public static DateTime GetClientRefreshAfterUtc(DateOnly expiresDay) =>
        expiresDay.AddDays(-6).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    public static bool IsOwnerMappingActive(DateOnly today, DateOnly expiresDay) =>
        today < expiresDay;
}
