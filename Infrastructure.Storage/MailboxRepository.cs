using Application;
using Dapper;
using Model;
using Npgsql;

namespace Infrastructure.Storage;

public sealed class MailboxRepository(NpgsqlDataSource dataSource) : IMailboxRepository
{
    private const string HistoryTable = @"""UserMailboxes""";
    private const string CurrentTable = @"""UserCurrentMailboxes""";

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

        var owner = await conn.QuerySingleOrDefaultAsync<MailboxOwner?>(cmd);
        return owner ?? throw new InvalidOperationException($"Mailbox {mailboxAddress} not found.");
    }

    public async Task<MailboxMap> GetCurrentMailboxForUserAsync(string user, CancellationToken ctn)
    {
        const string sql = $"""
            SELECT
                "MailboxAddress" AS "MailboxAddress",
                "ExpiresDay"     AS "ExpiresDay"
            FROM {CurrentTable}
            WHERE "User" = @User
            LIMIT 1;
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ctn);

        var cmd = new CommandDefinition(
            sql,
            new { User = user },
            cancellationToken: ctn);

        var map = await conn.QuerySingleOrDefaultAsync<MailboxMap?>(cmd);
        return map ?? throw new InvalidOperationException("Current mailbox for user not found.");
    }

    public async Task CreateMailboxAsync(UserMailbox userMailbox, CancellationToken ctn)
    {
        if (userMailbox.ExpiresDay == default)
            throw new ArgumentException("ExpiresDay must be set.", nameof(userMailbox));

        const string insertHistorySql = $"""
            INSERT INTO {HistoryTable} ("ExpiresDay", "MailboxAddress", "User")
            VALUES (@ExpiresDay, @MailboxAddress, @User);
            """;

        const string upsertCurrentSql = $"""
            INSERT INTO {CurrentTable} ("User", "MailboxAddress", "ExpiresDay")
            VALUES (@User, @MailboxAddress, @ExpiresDay)
            ON CONFLICT ("User")
            DO UPDATE SET
                "MailboxAddress" = EXCLUDED."MailboxAddress",
                "ExpiresDay"     = EXCLUDED."ExpiresDay";
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ctn);
        await using var tx = await conn.BeginTransactionAsync(ctn);

        try
        {
            var p = new
            {
                userMailbox.ExpiresDay,
                userMailbox.MailboxAddress,
                userMailbox.User
            };

            await conn.ExecuteAsync(new CommandDefinition(
                insertHistorySql, p, transaction: tx, cancellationToken: ctn));

            await conn.ExecuteAsync(new CommandDefinition(
                upsertCurrentSql, p, transaction: tx, cancellationToken: ctn));

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
            new { From = from, DaysAhead = daysAhead },
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
            new { Day = day },
            cancellationToken: ctn));
    }
}