using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SupermarketPos.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PosDbContext))]
[Migration(MigrationId)]
public sealed class RetailCheckout : Migration
{
    public const string MigrationId = "20261125190000_RetailCheckout";

    protected override void Up(MigrationBuilder migration)
    {
        migration.AddColumn<Guid>("CheckoutId", "Sales", type: "TEXT", nullable: true);
        migration.CreateIndex("IX_Sales_CheckoutId", "Sales", "CheckoutId", unique: true);
        migration.CreateIndex("IX_Sales_CreatedAt", "Sales", "CreatedAt");
        migration.CreateIndex("IX_Sales_CustomerAccountKey", "Sales", "CustomerAccountKey");
        migration.CreateIndex("IX_SaleLines_ProductId_SaleId", "SaleLines", new[] { "ProductId", "SaleId" });
    }

    protected override void Down(MigrationBuilder migration) => throw new NotSupportedException("Restore a matching backup instead.");
}
