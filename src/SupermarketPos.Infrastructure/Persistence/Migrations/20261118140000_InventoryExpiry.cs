using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SupermarketPos.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PosDbContext))]
[Migration(MigrationId)]
public sealed class InventoryExpiry : Migration
{
    public const string MigrationId = "20261118140000_InventoryExpiry";

    protected override void Up(MigrationBuilder b) => b.Sql("""
        ALTER TABLE Products ADD COLUMN TrackExpiry INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE Products ADD COLUMN StockRevision INTEGER NOT NULL DEFAULT 0;
        CREATE TABLE InventoryBatches (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ProductId INTEGER NOT NULL REFERENCES Products(Id) ON DELETE CASCADE,
            Lot TEXT NOT NULL,
            ExpiryDate TEXT NOT NULL,
            Quantity TEXT NOT NULL,
            SupplierId INTEGER NULL REFERENCES Suppliers(Id) ON DELETE SET NULL,
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL
        );
        CREATE UNIQUE INDEX IX_InventoryBatches_ProductId_Lot ON InventoryBatches(ProductId, Lot);
        CREATE INDEX IX_InventoryBatches_SupplierId ON InventoryBatches(SupplierId);
        ALTER TABLE SaleLines ADD COLUMN BatchId INTEGER NULL;
        ALTER TABLE SaleLines ADD COLUMN BatchLot TEXT NULL;
        ALTER TABLE SaleLines ADD COLUMN BatchExpiry TEXT NULL;
        ALTER TABLE SaleLines ADD COLUMN SerialNumber TEXT NULL;
        CREATE UNIQUE INDEX IX_SaleLines_ProductId_SerialNumber ON SaleLines(ProductId, SerialNumber) WHERE SerialNumber IS NOT NULL;
        ALTER TABLE PurchaseLines ADD COLUMN BatchId INTEGER NULL;
        ALTER TABLE PurchaseLines ADD COLUMN BatchLot TEXT NULL;
        ALTER TABLE PurchaseLines ADD COLUMN BatchExpiry TEXT NULL;
        ALTER TABLE StockMovements ADD COLUMN BatchId INTEGER NULL;
        ALTER TABLE StockMovements ADD COLUMN BatchLot TEXT NULL;
        """);

    protected override void Down(MigrationBuilder b) => b.Sql("""
        DROP INDEX IX_SaleLines_ProductId_SerialNumber;
        ALTER TABLE SaleLines DROP COLUMN SerialNumber;
        ALTER TABLE SaleLines DROP COLUMN BatchExpiry;
        ALTER TABLE SaleLines DROP COLUMN BatchLot;
        ALTER TABLE SaleLines DROP COLUMN BatchId;
        ALTER TABLE PurchaseLines DROP COLUMN BatchExpiry;
        ALTER TABLE PurchaseLines DROP COLUMN BatchLot;
        ALTER TABLE PurchaseLines DROP COLUMN BatchId;
        ALTER TABLE StockMovements DROP COLUMN BatchLot;
        ALTER TABLE StockMovements DROP COLUMN BatchId;
        DROP TABLE InventoryBatches;
        ALTER TABLE Products DROP COLUMN StockRevision;
        ALTER TABLE Products DROP COLUMN TrackExpiry;
        """);
}
