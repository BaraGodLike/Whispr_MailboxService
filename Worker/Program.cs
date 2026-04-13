using Application;
using Infrastructure.Storage;
using Npgsql;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);

var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")
                               ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
                            ?? throw new InvalidOperationException("Connection string 'Redis' is not configured.");

builder.Services.AddSingleton(_ => new NpgsqlDataSourceBuilder(postgresConnectionString).Build());
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());
builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

builder.Services.AddScoped<MailboxRepository>();
builder.Services.AddScoped<IMailboxRepository>(sp =>
    new CachedMailboxRepository(
        sp.GetRequiredService<MailboxRepository>(),
        sp.GetRequiredService<IDatabase>(),
        sp.GetRequiredService<IDateTimeProvider>()));
builder.Services.AddScoped<IMailboxMaintenanceService, MailboxMaintenanceService>();
builder.Services.AddScoped<IBackgroundTask, MailboxRatchetTask>();

var host = builder.Build();

using var scope = host.Services.CreateScope();
var task = scope.ServiceProvider.GetRequiredService<IBackgroundTask>();
await task.Run(CancellationToken.None);
