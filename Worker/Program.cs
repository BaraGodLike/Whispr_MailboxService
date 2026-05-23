using Application;
using Infrastructure.Storage;
using Npgsql;
using StackExchange.Redis;
using System.Text.Json;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = false;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    options.UseUtcTimestamp = true;
    options.JsonWriterOptions = new JsonWriterOptions
    {
        Indented = false
    };
});

var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")
                               ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
                            ?? throw new InvalidOperationException("Connection string 'Redis' is not configured.");

builder.Services.AddSingleton(_ => new NpgsqlDataSourceBuilder(postgresConnectionString).Build());
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());
builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
builder.Services.AddSingleton<IServiceInstanceMetadata>(_ => new ServiceInstanceMetadata("MailboxService"));

builder.Services.AddScoped<MailboxRepository>();
builder.Services.AddScoped<IMailboxRepository>(sp =>
    new CachedMailboxRepository(
        sp.GetRequiredService<MailboxRepository>(),
        sp.GetRequiredService<IDatabase>(),
        sp.GetRequiredService<IDateTimeProvider>(),
        sp.GetRequiredService<IServiceInstanceMetadata>(),
        sp.GetRequiredService<ILogger<CachedMailboxRepository>>()));
builder.Services.AddScoped<IMailboxMaintenanceService, MailboxMaintenanceService>();
builder.Services.AddScoped<IBackgroundTask, MailboxRatchetTask>();

var host = builder.Build();
var serviceInstanceMetadata = host.Services.GetRequiredService<IServiceInstanceMetadata>();
var startupLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

startupLogger.LogInformation(
    "Background worker starting. Service: {Service}, Instance: {Instance}.",
    serviceInstanceMetadata.ServiceName,
    serviceInstanceMetadata.InstanceId);

using var scope = host.Services.CreateScope();
var task = scope.ServiceProvider.GetRequiredService<IBackgroundTask>();
await task.Run(CancellationToken.None);

startupLogger.LogInformation(
    "Background worker completed. Service: {Service}, Instance: {Instance}.",
    serviceInstanceMetadata.ServiceName,
    serviceInstanceMetadata.InstanceId);
