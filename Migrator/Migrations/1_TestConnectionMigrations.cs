using FluentMigrator;

namespace Migrator;

[Migration(1)]
public sealed class TestConnectionMigrations : Migration
{
    public override void Up()
    {
        Execute.Sql("SELECT 1;");
    }

    public override void Down()
    {
    }
}
