using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SupermarketPos.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PosDbContext))]
[Migration("20261117150000_TechStoreProfileAndCatalog")]
public sealed class TechStoreProfileAndCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("LogoPath", "Settings", type: "TEXT", maxLength: 500, nullable: true);
        // The original owner's one-time demo catalog replacement has already run.
        // Future installs/upgrades must never delete shop products or invent opening balances.
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Restore the pre-upgrade database backup to undo the catalog replacement.");

}
