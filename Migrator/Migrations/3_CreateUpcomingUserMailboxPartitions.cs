using FluentMigrator;

namespace Migrator;

[Migration(3)]
public sealed class CreateUpcomingUserMailboxPartitions : Migration
{
    public override void Up()
    {
        Execute.Sql("""
            SELECT public.ensure_user_mailboxes_partition(d::date)
            FROM generate_series(
                current_date,
                current_date + interval '1 days',
                interval '1 day'
            ) AS d;
            """);
    }

    public override void Down()
    {
        Execute.Sql("""
            DO $$
            DECLARE
                partition_name text;
            BEGIN
                FOR partition_name IN
                    SELECT child.relname
                    FROM pg_inherits
                    JOIN pg_class parent ON parent.oid = pg_inherits.inhparent
                    JOIN pg_class child ON child.oid = pg_inherits.inhrelid
                    JOIN pg_namespace ns ON ns.oid = child.relnamespace
                    WHERE parent.relname = 'UserMailboxes'
                      AND ns.nspname = 'public'
                LOOP
                    EXECUTE format('DROP TABLE IF EXISTS public.%I', partition_name);
                END LOOP;
            END;
            $$;
            """);
    }
}
