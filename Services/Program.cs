using Application;
using Grpc.AspNetCore.HealthChecks;
using Infrastructure.Storage;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Services;
using StackExchange.Redis;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
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
var grpcPort = builder.Configuration.GetValue<int?>("Ports:Grpc") ?? 8443;
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(grpcPort, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
    });
});

var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("Connection string 'Redis' is not configured.");

builder.Services.AddGrpc();
builder.Services
    .AddGrpcHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy());

builder.Services.AddSingleton(_ => new NpgsqlDataSourceBuilder(postgresConnectionString).Build());
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());
builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
builder.Services.AddSingleton<IRealtimeAuthNonceStore, RedisRealtimeAuthNonceStore>();
builder.Services.AddSingleton<IRealtimeAuthSignatureVerifier, Ed25519RealtimeAuthSignatureVerifier>();
builder.Services.AddSingleton<IServiceInstanceMetadata>(_ => new ServiceInstanceMetadata("MailboxService"));

builder.Services.AddScoped<MailboxRepository>();
builder.Services.AddScoped<IMailboxRepository>(sp =>
    new CachedMailboxRepository(
        sp.GetRequiredService<MailboxRepository>(),
        sp.GetRequiredService<IDatabase>(),
        sp.GetRequiredService<IDateTimeProvider>(),
        sp.GetRequiredService<IServiceInstanceMetadata>(),
        sp.GetRequiredService<ILogger<CachedMailboxRepository>>()));
builder.Services.AddScoped<IMailboxService, MailboxService>();

var app = builder.Build();
var serviceInstanceMetadata = app.Services.GetRequiredService<IServiceInstanceMetadata>();
var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

startupLogger.LogInformation(
    "gRPC host starting. Service: {Service}, Instance: {Instance}, Endpoint: {Endpoint}.",
    serviceInstanceMetadata.ServiceName,
    serviceInstanceMetadata.InstanceId,
    $"http://0.0.0.0:{grpcPort}");

app.Use(async (context, next) =>
{
    var instanceMetadata = context.RequestServices.GetRequiredService<IServiceInstanceMetadata>();

    try
    {
        await next();
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(
            "Unhandled API exception. Service: {Service}, Instance: {Instance}, RequestId: {RequestId}, ExceptionType: {ExceptionType}.",
            instanceMetadata.ServiceName,
            instanceMetadata.InstanceId,
            context.TraceIdentifier,
            exception.GetType().FullName);
        throw;
    }
});

app.MapGrpcService<MailboxGrpcService>();
app.MapGrpcHealthChecksService();

app.Run();
