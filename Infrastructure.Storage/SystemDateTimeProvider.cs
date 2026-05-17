using Application;

namespace Infrastructure.Storage;

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateOnly GetCurrentDate() => DateOnly.FromDateTime(DateTime.UtcNow);

    public DateTime GetCurrentDateTime() => DateTime.UtcNow;
}
