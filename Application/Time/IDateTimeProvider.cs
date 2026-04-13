namespace Application;

public interface IDateTimeProvider
{
    DateOnly GetCurrentDate();
    DateTime GetCurrentDateTime();
}
