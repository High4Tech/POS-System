using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SupermarketPos.Infrastructure.Persistence.Migrations;
[DbContext(typeof(PosDbContext))]
[Migration("20261119180000_RetailMeasurements")]
public sealed class RetailMeasurements : Migration
{
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        ALTER TABLE Products ADD COLUMN BaseStockQuantity INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE Products ADD COLUMN UnitsPerPackage INTEGER NOT NULL DEFAULT 1;
        ALTER TABLE Products ADD COLUMN PurchaseUnit TEXT NOT NULL DEFAULT 'box';
        ALTER TABLE Products ADD COLUMN PackageBarcode TEXT NULL;
        ALTER TABLE Products ADD COLUMN PackagePurchaseCost TEXT NULL;
        ALTER TABLE Products ADD COLUMN PackageSellingPrice TEXT NULL;
        ALTER TABLE Products ADD COLUMN MinimumStock TEXT NULL;
        ALTER TABLE InventoryBatches ADD COLUMN BaseQuantity INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE InventoryBatches ADD COLUMN BaseUnit INTEGER NOT NULL DEFAULT 0;
        CREATE TEMP TABLE MeasureMigrationGuard (Valid INTEGER CHECK(Valid=1));
        INSERT INTO MeasureMigrationGuard
          SELECT CASE WHEN CAST(StockQuantity AS TEXT) LIKE '-%'
            OR (Unit=0 AND instr(StockQuantity,'.')>0 AND trim(substr(StockQuantity,instr(StockQuantity,'.')+1),'0')<>'')
            OR (Unit<>0 AND instr(StockQuantity,'.')>0 AND trim(substr(StockQuantity,instr(StockQuantity,'.')+4),'0')<>'')
            THEN 0 ELSE 1 END FROM Products;
        UPDATE Products SET BaseStockQuantity=CASE WHEN Unit=0 THEN CAST(StockQuantity AS INTEGER) ELSE
          CAST(StockQuantity AS INTEGER)*1000 + CASE WHEN instr(StockQuantity,'.')>0 THEN CAST(substr(substr(StockQuantity,instr(StockQuantity,'.')+1)||'000',1,3) AS INTEGER) ELSE 0 END END;
        UPDATE InventoryBatches SET BaseUnit=(SELECT Unit FROM Products WHERE Products.Id=InventoryBatches.ProductId);
        INSERT INTO MeasureMigrationGuard SELECT CASE WHEN CAST(Quantity AS TEXT) LIKE '-%'
          OR (BaseUnit=0 AND instr(Quantity,'.')>0 AND trim(substr(Quantity,instr(Quantity,'.')+1),'0')<>'')
          OR (BaseUnit<>0 AND instr(Quantity,'.')>0 AND trim(substr(Quantity,instr(Quantity,'.')+4),'0')<>'') THEN 0 ELSE 1 END FROM InventoryBatches;
        UPDATE InventoryBatches SET BaseQuantity=CASE WHEN BaseUnit=0 THEN CAST(Quantity AS INTEGER) ELSE
          CAST(Quantity AS INTEGER)*1000 + CASE WHEN instr(Quantity,'.')>0 THEN CAST(substr(substr(Quantity,instr(Quantity,'.')+1)||'000',1,3) AS INTEGER) ELSE 0 END END;
        DROP TABLE MeasureMigrationGuard;
        CREATE UNIQUE INDEX IX_Products_PackageBarcode ON Products(PackageBarcode);
        CREATE TRIGGER ProductBarcodeCollisionInsert BEFORE INSERT ON Products WHEN EXISTS
          (SELECT 1 FROM Products WHERE Barcode=NEW.PackageBarcode OR PackageBarcode=NEW.Barcode)
          BEGIN SELECT RAISE(ABORT,'Barcode already belongs to another unit'); END;
        CREATE TRIGGER ProductBarcodeCollisionUpdate BEFORE UPDATE OF Barcode,PackageBarcode ON Products WHEN EXISTS
          (SELECT 1 FROM Products WHERE Id<>NEW.Id AND (Barcode=NEW.PackageBarcode OR PackageBarcode=NEW.Barcode))
          BEGIN SELECT RAISE(ABORT,'Barcode already belongs to another unit'); END;
        ALTER TABLE SaleLines ADD COLUMN UnitCode TEXT NOT NULL DEFAULT '';
        ALTER TABLE SaleLines ADD COLUMN BaseQuantity INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE PurchaseLines ADD COLUMN UnitCode TEXT NOT NULL DEFAULT '';
        ALTER TABLE PurchaseLines ADD COLUMN PackageSize INTEGER NOT NULL DEFAULT 1;
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Restore the matching application and database backup.");
}
