namespace Application;

public readonly record struct MailboxSchedule(
    DateOnly Today,
    DateOnly CurrentExpiresDay,
    DateOnly NextExpiresDay,
    DateOnly ExpiredPartitionDay);
