using FluentMigrator;

namespace Migrator.Migrations;

[Migration(1)]
public sealed class CreateUserCurrentMailboxes : Migration
{
    public override void Up()
    {
        if (!Schema.Table("UserCurrentMailboxes").Exists())
        {
            Create.Table("UserCurrentMailboxes")
                .WithColumn("User").AsCustom("text").PrimaryKey()
                .WithColumn("MailboxAddress").AsGuid().NotNullable()
                .WithColumn("ExpiresDay").AsDate().NotNullable();
        }

        if (!Schema.Table("UserCurrentMailboxes").Index("IX_UserCurrentMailboxes_MailboxAddress").Exists())
        {
            Create.Index("IX_UserCurrentMailboxes_MailboxAddress")
                .OnTable("UserCurrentMailboxes")
                .OnColumn("MailboxAddress").Ascending()
                .WithOptions().Unique();
        }
    }

    public override void Down()
    {
        if (Schema.Table("UserCurrentMailboxes").Index("IX_UserCurrentMailboxes_MailboxAddress").Exists())
            Delete.Index("IX_UserCurrentMailboxes_MailboxAddress").OnTable("UserCurrentMailboxes");

        if (Schema.Table("UserCurrentMailboxes").Exists())
            Delete.Table("UserCurrentMailboxes");
    }
}
