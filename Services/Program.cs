using Application;
using Infrastructure.Storage;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Npgsql;
using Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(8080, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1;
    });

    options.ListenAnyIP(8443, listenOptions =>
    {
        listenOptions.UseHttps();
        listenOptions.Protocols = HttpProtocols.Http2;
    });
});

var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("Connection string 'Redis' is not configured.");

builder.Services.AddControllers();
builder.Services.AddGrpc();
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();

builder.Services.AddSingleton(_ => new NpgsqlDataSourceBuilder(postgresConnectionString).Build());
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());
builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

builder.Services.AddScoped<MailboxRepository>();
builder.Services.AddScoped<IMailboxRepository>(sp =>
    new CachedMailboxRepository(
        sp.GetRequiredService<MailboxRepository>(),
        sp.GetRequiredService<IDatabase>(),
        sp.GetRequiredService<IDateTimeProvider>(),
        sp.GetRequiredService<ILogger<CachedMailboxRepository>>()));
builder.Services.AddScoped<IMailboxService, MailboxService>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError("Unhandled API exception. ExceptionType: {ExceptionType}.", exception.GetType().FullName);

        if (IsGrpcRequest(context))
        {
            throw;
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";

        await Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected server error occurred.")
            .ExecuteAsync(context);
    }
});

app.MapControllers();
app.MapGrpcService<MailboxGrpcService>();
app.MapHealthChecks("/health");

app.Run();

static bool IsGrpcRequest(HttpContext context) =>
    context.Request.ContentType?.StartsWith("application/grpc", StringComparison.OrdinalIgnoreCase) == true;
