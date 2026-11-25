using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace SupermarketPos.Infrastructure.Persistence.Migrations;
[DbContext(typeof(PosDbContext))][Migration(MigrationId)]
public sealed class PharmacyDispensing:Migration
{
    public const string MigrationId="20261120160000_PharmacyDispensing";
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<string>("SellingUnit","Products",nullable:false,defaultValue:"piece");
        m.AddColumn<int>("UnitsPerStrip","Products",nullable:false,defaultValue:1);
        m.AddColumn<decimal>("StripSellingPrice","Products",type:"TEXT",nullable:true);
        foreach(var field in new[]{"DispensingRulesReviewed","RequiresPrescription","RequiresDispensingReference"})m.AddColumn<bool>(field,"Products",nullable:false,defaultValue:false);
        m.AddColumn<string>("HoldReason","InventoryBatches",nullable:false,defaultValue:"");
        foreach(var field in new[]{"StockUntracked","PrescriptionChecked"})m.AddColumn<bool>(field,"SaleLines",nullable:false,defaultValue:false);
        m.AddColumn<string>("DispensingReference","SaleLines",nullable:true);
        m.Sql("""
            CREATE TABLE StockReconciliations(Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,ProductId INTEGER NOT NULL REFERENCES Products(Id) ON DELETE RESTRICT,SaleLineId INTEGER NOT NULL REFERENCES SaleLines(Id) ON DELETE RESTRICT,CreatedAt TEXT NOT NULL,ResolvedAt TEXT NULL,Resolution TEXT NOT NULL,ResolvedBy TEXT NOT NULL);
            CREATE UNIQUE INDEX IX_StockReconciliations_SaleLineId ON StockReconciliations(SaleLineId);
            CREATE INDEX IX_StockReconciliations_ProductId_ResolvedAt ON StockReconciliations(ProductId,ResolvedAt);
            """);
    }
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Restore the matching application and data backup.");
}
