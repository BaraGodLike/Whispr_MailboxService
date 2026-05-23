namespace Application;

public static class MailboxPolicy
{
    public const int ActiveMailboxCount = 6;

    public static MailboxSchedule BuildSchedule(DateOnly today) =>
        new(
            Today: today,
            CurrentExpiresDay: today.AddDays(ActiveMailboxCount),
            NextExpiresDay: today.AddDays(ActiveMailboxCount + 1),
            ExpiredPartitionDay: today.AddDays(-1));

    public static (DateOnly MinExpiresDay, DateOnly MaxExpiresDay) BuildActiveMailboxWindow(DateOnly today) =>
        (today.AddDays(1), today.AddDays(ActiveMailboxCount));

    public static DateTime GetClientRefreshAfterUtc(DateOnly expiresDay) =>
        expiresDay.AddDays(-ActiveMailboxCount).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    public static bool IsOwnerMappingActive(DateOnly today, DateOnly expiresDay) =>
        today < expiresDay;
}
