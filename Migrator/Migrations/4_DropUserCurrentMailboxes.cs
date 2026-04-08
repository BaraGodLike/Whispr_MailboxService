using FluentMigrator;

namespace Migrator;

[Migration(4)]
public sealed class DropUserCurrentMailboxes : Migration
{
    public override void Up()
    {
        if (Schema.Table("UserCurrentMailboxes").Index("IX_UserCurrentMailboxes_MailboxAddress").Exists())
            Delete.Index("IX_UserCurrentMailboxes_MailboxAddress").OnTable("UserCurrentMailboxes");

        if (Schema.Table("UserCurrentMailboxes").Exists())
            Delete.Table("UserCurrentMailboxes");
    }

    public override void Down()
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
}
