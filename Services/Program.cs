using Application;
using Infrastructure.Storage;
using Npgsql;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("Connection string 'Redis' is not configured.");

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
// builder.Services.AddGrpc();

builder.Services.AddSingleton(_ => new NpgsqlDataSourceBuilder(postgresConnectionString).Build());
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());

builder.Services.AddScoped<MailboxRepository>();
builder.Services.AddScoped<IMailboxRepository>(sp =>
    new CachedMailboxRepository(
        sp.GetRequiredService<MailboxRepository>(),
        sp.GetRequiredService<IDatabase>()));

var app = builder.Build();

app.UseExceptionHandler(exceptionApp =>
{
    exceptionApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";

        await Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Internal Server Error",
            detail: "An unexpected server error occurred.")
            .ExecuteAsync(context);
    });
});

app.MapControllers();

app.Run();
