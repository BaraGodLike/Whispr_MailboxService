namespace Application;

public interface IBackgroundTask
{
    Task Run(CancellationToken ctn);
}