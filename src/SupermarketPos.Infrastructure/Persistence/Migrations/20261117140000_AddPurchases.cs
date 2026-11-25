using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SupermarketPos.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PosDbContext))]
[Migration("20261117140000_AddPurchases")]
public sealed class AddPurchases : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Purchases",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Supplier = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                Reference = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                ReceivedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                ReceivedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Purchases", x => x.Id));

        migrationBuilder.CreateTable(
            name: "PurchaseLines",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                PurchaseId = table.Column<int>(type: "INTEGER", nullable: false),
                ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                ProductName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Barcode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                Unit = table.Column<int>(type: "INTEGER", nullable: false),
                Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                UnitCost = table.Column<decimal>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PurchaseLines", x => x.Id);
                table.ForeignKey("FK_PurchaseLines_Purchases_PurchaseId", x => x.PurchaseId,
                    "Purchases", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_Purchases_ReceivedAt", "Purchases", "ReceivedAt");
        migrationBuilder.CreateIndex("IX_PurchaseLines_PurchaseId", "PurchaseLines", "PurchaseId");
        migrationBuilder.Sql("UPDATE Settings SET Language = 'en' WHERE Language = 'tr';");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("PurchaseLines");
        migrationBuilder.DropTable("Purchases");
    }
}
