using Application;
using Dapper;
using Npgsql;

namespace Infrastructure.Storage;

public sealed class MailboxRepository(NpgsqlDataSource dataSource) : IMailboxRepository
{
    private const string HistoryTable = @"""UserMailboxes""";
    private const string RegistryTable = @"""MailboxRegistry""";
    private const string UsersTable = @"""Users""";
    private static DateTime ToDbDate(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    private const string EnsurePartitionsSql = """
        SELECT ensure_user_mailboxes_partition(@CurrentExpiresDay::date);
        SELECT ensure_user_mailboxes_partition(@NextExpiresDay::date);
        """;

    private static string UpsertMailboxSql => $"""
        INSERT INTO {HistoryTable} ("ExpiresDay", "MailboxAddress", "User")
        VALUES (@ExpiresDay::date, gen_random_uuid(), @User)
        ON CONFLICT ("User", "ExpiresDay") DO UPDATE
        SET "User" = EXCLUDED."User"
        RETURNING
            "MailboxAddress" AS "Mailbox",
            "ExpiresDay"     AS "ExpiresDay";
        """;

    private static string EnsureRegistrySql => $"""
        INSERT INTO {RegistryTable} ("MailboxAddress")
        VALUES (@MailboxAddress)
        ON CONFLICT ("MailboxAddress") DO NOTHING;
        """;

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

        return await conn.QuerySingleOrDefaultAsync<MailboxOwner>(new CommandDefinition(
            sql,
            new { MailboxAddress = mailboxAddress },
            cancellationToken: ctn));
    }

    public async Task<MailboxMap> GetCurrentMailboxForUserAsync(string user, DateOnly expiresDay, CancellationToken ctn)
    {
        const string sql = $"""
            SELECT
                "MailboxAddress" AS "Mailbox",
                "ExpiresDay"     AS "ExpiresDay"
            FROM {HistoryTable}
            WHERE "User" = @User
              AND "ExpiresDay" = @ExpiresDay::date
            LIMIT 1;
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ctn);

        return await conn.QuerySingleOrDefaultAsync<MailboxMap>(new CommandDefinition(
            sql,
            new
            {
                User = user,
                ExpiresDay = ToDbDate(expiresDay)
            },
            cancellationToken: ctn));
    }

    public async Task<bool> RegisterUserAsync(
        string user,
        string authAlg,
        byte[] publicKey,
        MailboxSchedule schedule,
        CancellationToken ctn)
    {
        var sql = $"""
            INSERT INTO {UsersTable} ("User", "AuthAlg", "PublicKey")
            VALUES (@User, @AuthAlg, @PublicKey)
            ON CONFLICT ("User") DO NOTHING;
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ctn);
        await using var tx = await conn.BeginTransactionAsync(ctn);

        try
        {
            var inserted = await conn.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    User = user,
                    AuthAlg = authAlg,
                    PublicKey = publicKey
                },
                transaction: tx,
                cancellationToken: ctn));

            if (inserted == 0)
            {
                await tx.RollbackAsync(ctn);
                return false;
            }

            await CreateMailboxCoreAsync(conn, tx, user, schedule, ctn);

            await tx.CommitAsync(ctn);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await tx.RollbackAsync(ctn);
            throw;
        }
    }

    public async Task<UserAuthInfo?> GetUserAuthInfoAsync(string user, CancellationToken ctn)
    {
        var sql = $"""
            SELECT
                "User"      AS "User",
                "AuthAlg"   AS "AuthAlg",
                "PublicKey" AS "PublicKey"
            FROM {UsersTable}
            WHERE "User" = @User
            LIMIT 1;
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ctn);

        return await conn.QuerySingleOrDefaultAsync<UserAuthInfo>(new CommandDefinition(
            sql,
            new { User = user },
            cancellationToken: ctn));
    }

    public async Task<IReadOnlyList<MailboxMap>> GetActiveMailboxesForUserAsync(
        string user,
        DateOnly minExpiresDay,
        DateOnly maxExpiresDay,
        CancellationToken ctn)
    {
        var sql = $"""
            SELECT
                "MailboxAddress" AS "Mailbox",
                "ExpiresDay"     AS "ExpiresDay"
            FROM {HistoryTable}
            WHERE "User" = @User
              AND "ExpiresDay" BETWEEN @MinExpiresDay::date AND @MaxExpiresDay::date
            ORDER BY "ExpiresDay" DESC
            LIMIT {MailboxPolicy.ActiveMailboxCount};
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ctn);

        var results = await conn.QueryAsync<MailboxMap>(new CommandDefinition(
            sql,
            new
            {
                User = user,
                MinExpiresDay = ToDbDate(minExpiresDay),
                MaxExpiresDay = ToDbDate(maxExpiresDay)
            },
            cancellationToken: ctn));

        return results.AsList();
    }

    public async Task RotateMailboxesAsync(MailboxSchedule schedule, CancellationToken ctn)
    {
        var sql = $"""
            SELECT ensure_user_mailboxes_partition(@CurrentExpiresDay::date);
            SELECT ensure_user_mailboxes_partition(@NextExpiresDay::date);

            WITH missing_users AS (
                SELECT src."User"
                FROM user_mailboxes_{ToYyyyMmDd(schedule.CurrentExpiresDay)} AS src
                LEFT JOIN user_mailboxes_{ToYyyyMmDd(schedule.NextExpiresDay)} AS dst
                  ON dst."User" = src."User"
                 AND dst."ExpiresDay" = @NextExpiresDay::date
                WHERE dst."User" IS NULL
            ),
            inserted_history AS (
                INSERT INTO user_mailboxes_{ToYyyyMmDd(schedule.NextExpiresDay)} ("ExpiresDay", "MailboxAddress", "User")
                SELECT @NextExpiresDay::date, gen_random_uuid(), mu."User"
                FROM missing_users AS mu
                ON CONFLICT ("User", "ExpiresDay") DO NOTHING
                RETURNING "MailboxAddress"
            ),
            inserted_registry AS (
                INSERT INTO {RegistryTable} ("MailboxAddress")
                SELECT ih."MailboxAddress"
                FROM inserted_history AS ih
                ON CONFLICT ("MailboxAddress") DO NOTHING
            )
            SELECT 1;

            DO $cleanup$
            BEGIN
                IF to_regclass('public.user_mailboxes_{ToYyyyMmDd(schedule.ExpiredPartitionDay)}') IS NOT NULL THEN
                    EXECUTE $sql$
                        DELETE FROM {RegistryTable} AS registry
                        WHERE EXISTS (
                            SELECT 1
                            FROM user_mailboxes_{ToYyyyMmDd(schedule.ExpiredPartitionDay)} AS old_partition
                            WHERE old_partition."MailboxAddress" = registry."MailboxAddress"
                        );
                    $sql$;
                END IF;
            END
            $cleanup$;

            SELECT drop_user_mailboxes_partition(@ExpiredPartitionDay::date);
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ctn);
        await using var tx = await conn.BeginTransactionAsync(ctn);

        try
        {
            await conn.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    CurrentExpiresDay = ToDbDate(schedule.CurrentExpiresDay),
                    NextExpiresDay = ToDbDate(schedule.NextExpiresDay),
                    ExpiredPartitionDay = ToDbDate(schedule.ExpiredPartitionDay)
                },
                transaction: tx,
                cancellationToken: ctn));

            await tx.CommitAsync(ctn);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await tx.RollbackAsync(ctn);
            throw;
        }
    }

    private static async Task<MailboxMap> CreateMailboxCoreAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string user,
        MailboxSchedule schedule,
        CancellationToken ctn)
    {
        var currentExpiresDay = ToDbDate(schedule.CurrentExpiresDay);
        var nextExpiresDay = ToDbDate(schedule.NextExpiresDay);

        await conn.ExecuteAsync(new CommandDefinition(
            EnsurePartitionsSql,
            new
            {
                CurrentExpiresDay = currentExpiresDay,
                NextExpiresDay = nextExpiresDay
            },
            transaction: tx,
            cancellationToken: ctn));

        var currentMailbox = await conn.QuerySingleAsync<MailboxMap>(new CommandDefinition(
            UpsertMailboxSql,
            new
            {
                User = user,
                ExpiresDay = currentExpiresDay
            },
            transaction: tx,
            cancellationToken: ctn));

        var nextMailbox = await conn.QuerySingleAsync<MailboxMap>(new CommandDefinition(
            UpsertMailboxSql,
            new
            {
                User = user,
                ExpiresDay = nextExpiresDay
            },
            transaction: tx,
            cancellationToken: ctn));

        await conn.ExecuteAsync(new CommandDefinition(
            EnsureRegistrySql,
            new[]
            {
                new { MailboxAddress = currentMailbox.Mailbox },
                new { MailboxAddress = nextMailbox.Mailbox }
            },
            transaction: tx,
            cancellationToken: ctn));

        return currentMailbox;
    }

    private static int ToYyyyMmDd(DateOnly d) => d.Year * 10000 + d.Month * 100 + d.Day;
}
