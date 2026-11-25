using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace SupermarketPos.Infrastructure.Persistence.Migrations;
[DbContext(typeof(PosDbContext))][Migration(MigrationId)]
public sealed class PharmacyLocations:Migration
{
    public const string MigrationId="20261120170000_PharmacyLocations";
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<string>("StockLocation","SaleLines",nullable:true);m.AddColumn<string>("StockLocation","PurchaseLines",nullable:true);
        m.Sql("""
            CREATE TABLE StockLocations(Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,ParentId INTEGER NULL REFERENCES StockLocations(Id) ON DELETE RESTRICT,Name TEXT NOT NULL,Barcode TEXT NOT NULL,IsActive INTEGER NOT NULL);
            CREATE UNIQUE INDEX IX_StockLocations_Barcode ON StockLocations(Barcode);
            CREATE INDEX IX_StockLocations_ParentId ON StockLocations(ParentId);
            CREATE TABLE StockPlacements(Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,ProductId INTEGER NOT NULL REFERENCES Products(Id) ON DELETE RESTRICT,BatchId INTEGER NULL REFERENCES InventoryBatches(Id) ON DELETE RESTRICT,LocationId INTEGER NOT NULL REFERENCES StockLocations(Id) ON DELETE RESTRICT,AllocationKey TEXT NOT NULL,BaseQuantity INTEGER NOT NULL,Revision INTEGER NOT NULL);
            CREATE UNIQUE INDEX IX_StockPlacements_AllocationKey ON StockPlacements(AllocationKey);
            CREATE INDEX IX_StockPlacements_ProductId_BatchId ON StockPlacements(ProductId,BatchId);
            CREATE INDEX IX_StockPlacements_BatchId ON StockPlacements(BatchId);
            CREATE INDEX IX_StockPlacements_LocationId ON StockPlacements(LocationId);
            """);
    }
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Restore the matching application and data backup.");
}
