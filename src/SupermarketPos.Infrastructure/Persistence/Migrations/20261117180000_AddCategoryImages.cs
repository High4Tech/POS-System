using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SupermarketPos.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PosDbContext))]
[Migration("20261117180000_AddCategoryImages")]
public sealed class AddCategoryImages : Migration
{
    protected override void Up(MigrationBuilder builder) =>
        builder.AddColumn<string>("ImageFileName", "Categories", type: "TEXT", maxLength: 36, nullable: true);

    protected override void Down(MigrationBuilder builder) =>
        builder.DropColumn("ImageFileName", "Categories");
}
