using FluentMigrator;

namespace Migrator;

[Migration(6)]
public sealed class CreateMailboxRegistry : Migration
{
    public override void Up()
    {
        if (!Schema.Table("MailboxRegistry").Exists())
        {
            Create.Table("MailboxRegistry")
                .WithColumn("MailboxAddress").AsGuid().PrimaryKey();
        }

        Execute.Sql("""
            INSERT INTO public."MailboxRegistry" ("MailboxAddress")
            SELECT DISTINCT "MailboxAddress"
            FROM public."UserMailboxes"
            ON CONFLICT ("MailboxAddress") DO NOTHING;
            """);
    }

    public override void Down()
    {
        if (Schema.Table("MailboxRegistry").Exists())
            Delete.Table("MailboxRegistry");
    }
}
