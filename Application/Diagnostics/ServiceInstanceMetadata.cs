namespace Application;

public sealed class ServiceInstanceMetadata(string serviceName, string? instanceId = null) : IServiceInstanceMetadata
{
    public string ServiceName { get; } = serviceName;

    public string InstanceId { get; } = string.IsNullOrWhiteSpace(instanceId)
        ? ResolveInstanceId()
        : instanceId;

    private static string ResolveInstanceId() =>
        Environment.GetEnvironmentVariable("SERVICE_INSTANCE_ID")
        ?? Environment.GetEnvironmentVariable("HOSTNAME")
        ?? Environment.MachineName;
}
