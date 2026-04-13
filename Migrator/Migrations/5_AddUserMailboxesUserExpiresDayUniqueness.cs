using FluentMigrator;

namespace Migrator;

[Migration(5)]
public sealed class AddUserMailboxesUserExpiresDayUniqueness : Migration
{
    public override void Up()
    {
        Execute.Sql("""
            CREATE UNIQUE INDEX IF NOT EXISTS "UX_UserMailboxes_User_ExpiresDay"
            ON public."UserMailboxes" ("User", "ExpiresDay");
            """);
    }

    public override void Down()
    {
        Execute.Sql("""
            DROP INDEX IF EXISTS public."UX_UserMailboxes_User_ExpiresDay";
            """);
    }
}
