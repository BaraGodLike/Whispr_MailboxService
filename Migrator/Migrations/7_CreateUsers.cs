using FluentMigrator;

namespace Migrator;

[Migration(7)]
public sealed class CreateUsers : Migration
{
    public override void Up()
    {
        if (!Schema.Table("Users").Exists())
        {
            Create.Table("Users")
                .WithColumn("User").AsCustom("text").PrimaryKey()
                .WithColumn("AuthAlg").AsCustom("text").NotNullable()
                .WithColumn("PublicKey").AsCustom("bytea").NotNullable();
        }
    }

    public override void Down()
    {
        if (Schema.Table("Users").Exists())
            Delete.Table("Users");
    }
}
