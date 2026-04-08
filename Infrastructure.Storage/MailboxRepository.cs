using Application;
using Dapper;
using Model;
using Npgsql;

namespace Infrastructure.Storage;

public sealed class MailboxRepository(NpgsqlDataSource dataSource) : IMailboxRepository
{
    private const string HistoryTable = @"""UserMailboxes""";
    private static DateTime ToDbDate(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    public async Task<MailboxOwner> GetUserByMailboxAsync(Guid mailboxAddress, CancellationToken ctn)
    {
        const string sql = $"""
            SELECT
                "User"       AS "User",
                "ExpiresDay" AS "ExpiresDay"
            FROM {HistoryTable}
            WHERE "MailboxAddress" = @MailboxAddress
            LIMIT 1;
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ctn);

        var cmd = new CommandDefinition(
            sql,
            new { MailboxAddress = mailboxAddress },
            cancellationToken: ctn);

        return await conn.QuerySingleOrDefaultAsync<MailboxOwner>(cmd);
    }

    public async Task<MailboxMap> GetCurrentMailboxForUserAsync(string user, CancellationToken ctn)
    {
        const string sql = $"""
            SELECT
                "MailboxAddress" AS "Mailbox",
                "ExpiresDay"     AS "ExpiresDay"
            FROM {HistoryTable}
            WHERE "User" = @User
              AND "ExpiresDay" = (current_date + interval '6 days')::date
            LIMIT 1;
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ctn);

        var cmd = new CommandDefinition(
            sql,
            new { User = user },
            cancellationToken: ctn);

        return await conn.QuerySingleOrDefaultAsync<MailboxMap>(cmd);
    }

    public async Task CreateMailboxAsync(UserMailbox userMailbox, CancellationToken ctn)
    {
        if (userMailbox.ExpiresDay == default)
            throw new ArgumentException("ExpiresDay must be set.", nameof(userMailbox));

        const string insertHistorySql = $"""
            INSERT INTO {HistoryTable} ("ExpiresDay", "MailboxAddress", "User")
            VALUES (@ExpiresDay, @MailboxAddress, @User);
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ctn);
        await using var tx = await conn.BeginTransactionAsync(ctn);

        try
        {
            var p = new
            {
                ExpiresDay = ToDbDate(userMailbox.ExpiresDay),
                MailboxAddress = userMailbox.MailboxAddress,
                User = userMailbox.User
            };

            await conn.ExecuteAsync(new CommandDefinition(
                insertHistorySql, p, transaction: tx, cancellationToken: ctn));

            await tx.CommitAsync(ctn);
        }
        catch
        {
            await tx.RollbackAsync(ctn);
            throw;
        }
    }

    public async Task EnsurePartitionsAsync(DateOnly from, int daysAhead, CancellationToken ctn)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(daysAhead);

        const string sql = """
            SELECT ensure_user_mailboxes_partition(d::date)
            FROM generate_series(
                    @From::date,
                    (@From::date + @DaysAhead)::date,
                    interval '1 day'
                 ) AS d;
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ctn);

        await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new { From = ToDbDate(from), DaysAhead = daysAhead },
            cancellationToken: ctn));
    }

    public async Task DropPartitionAsync(DateOnly day, CancellationToken ctn)
    {
        const string sql = """
            SELECT drop_user_mailboxes_partition(@Day::date);
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ctn);

        await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new { Day = ToDbDate(day) },
            cancellationToken: ctn));
    }
}
