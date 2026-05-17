using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;

var connectionString = ResolvePostgresConnectionString(args);

var services = new ServiceCollection()
    .AddFluentMigratorCore()
    .ConfigureRunner(runner => runner
        .AddPostgres()
        .WithGlobalConnectionString(connectionString)
        .ScanIn(typeof(Program).Assembly).For.Migrations())
    .AddLogging(logging => logging.AddFluentMigratorConsole())
    .BuildServiceProvider(validateScopes: false);

using var scope = services.CreateScope();

var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
runner.MigrateUp();
return;

static string ResolvePostgresConnectionString(string[] args)
{
    var fromArgs = TryGetArg(args, "--connection-string");
    if (!string.IsNullOrWhiteSpace(fromArgs))
        return fromArgs;

    LoadDotEnvIfPresent();

    var direct = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
    if (!string.IsNullOrWhiteSpace(direct))
        return direct;

    var host = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost";
    var port = Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432";
    var database = Environment.GetEnvironmentVariable("POSTGRES_DB");
    var user = Environment.GetEnvironmentVariable("POSTGRES_USER");
    var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");

    if (string.IsNullOrWhiteSpace(database) ||
        string.IsNullOrWhiteSpace(user) ||
        string.IsNullOrWhiteSpace(password))
    {
        throw new InvalidOperationException(
            "Postgres connection is not configured. " +
            "Set ConnectionStrings__Postgres or " +
            "POSTGRES_DB/POSTGRES_USER/POSTGRES_PASSWORD in the environment or .env.");
    }

    return $"Host={host};Port={port};Database={database};Username={user};Password={password}";
}

static string? TryGetArg(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    }

    return null;
}

static void LoadDotEnvIfPresent()
{
    var root = FindRepoRoot(AppContext.BaseDirectory);
    if (root is null)
        return;

    var envPath = Path.Combine(root, ".env");
    if (!File.Exists(envPath))
        return;

    foreach (var rawLine in File.ReadAllLines(envPath))
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
            continue;

        var separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0)
            continue;

        var key = line[..separatorIndex].Trim();
        var value = line[(separatorIndex + 1)..].Trim().Trim('"');

        if (string.IsNullOrWhiteSpace(key))
            continue;

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
            Environment.SetEnvironmentVariable(key, value);
    }
}

static string? FindRepoRoot(string startPath)
{
    var directory = new DirectoryInfo(startPath);

    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Whispr_MailboxService.sln")))
            return directory.FullName;

        directory = directory.Parent;
    }

    return null;
}
