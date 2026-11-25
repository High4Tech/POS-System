using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SupermarketPos.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PosDbContext))]
[Migration("20261117160000_AddCategoryColor")]
public sealed class AddCategoryColor : Migration
{
    protected override void Up(MigrationBuilder builder) =>
        builder.AddColumn<string>("ColorHex", "Categories", type: "TEXT", maxLength: 7, nullable: true);

    protected override void Down(MigrationBuilder builder) =>
        builder.DropColumn("ColorHex", "Categories");
}
