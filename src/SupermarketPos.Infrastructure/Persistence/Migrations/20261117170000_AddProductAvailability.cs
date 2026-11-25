using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SupermarketPos.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PosDbContext))]
[Migration("20261117170000_AddProductAvailability")]
public sealed class AddProductAvailability : Migration
{
    protected override void Up(MigrationBuilder builder) =>
        builder.AddColumn<bool>("IsActive", "Products", type: "INTEGER", nullable: false, defaultValue: true);

    protected override void Down(MigrationBuilder builder) =>
        builder.DropColumn("IsActive", "Products");
}
