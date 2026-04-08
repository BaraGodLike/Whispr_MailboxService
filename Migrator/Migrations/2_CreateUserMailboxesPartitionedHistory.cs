using FluentMigrator;

namespace Migrator.Migrations;

[Migration(2)]
public sealed class CreateUserMailboxesPartitionedHistory : Migration
{
    public override void Up()
    {
        Execute.Sql("""
            CREATE TABLE IF NOT EXISTS public."UserMailboxes"
            (
                "ExpiresDay" date NOT NULL,
                "MailboxAddress" uuid NOT NULL,
                "User" text NOT NULL
            )
            PARTITION BY RANGE ("ExpiresDay");
            """);

        Execute.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_UserMailboxes_MailboxAddress"
            ON public."UserMailboxes" ("MailboxAddress");
            """);

        Execute.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_UserMailboxes_User"
            ON public."UserMailboxes" ("User");
            """);

        Execute.Sql("""
            CREATE OR REPLACE FUNCTION public.ensure_user_mailboxes_partition(day date)
            RETURNS void
            LANGUAGE plpgsql
            AS $$
            DECLARE
                partition_name text := format('user_mailboxes_%s', to_char(day, 'YYYYMMDD'));
                range_end date := day + 1;
            BEGIN
                EXECUTE format(
                    'CREATE TABLE IF NOT EXISTS public.%I PARTITION OF public."UserMailboxes" FOR VALUES FROM (%L) TO (%L)',
                    partition_name,
                    day,
                    range_end);
            END;
            $$;
            """);

        Execute.Sql("""
            CREATE OR REPLACE FUNCTION public.drop_user_mailboxes_partition(day date)
            RETURNS void
            LANGUAGE plpgsql
            AS $$
            DECLARE
                partition_name text := format('user_mailboxes_%s', to_char(day, 'YYYYMMDD'));
            BEGIN
                EXECUTE format('DROP TABLE IF EXISTS public.%I', partition_name);
            END;
            $$;
            """);
    }

    public override void Down()
    {
        Execute.Sql("""DROP FUNCTION IF EXISTS public.drop_user_mailboxes_partition(date);""");
        Execute.Sql("""DROP FUNCTION IF EXISTS public.ensure_user_mailboxes_partition(date);""");
        Execute.Sql("""DROP TABLE IF EXISTS public."UserMailboxes" CASCADE;""");
    }
}
