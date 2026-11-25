using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SupermarketPos.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PosDbContext))]
[Migration("20261117190000_SupplierReceipts")]
public sealed class SupplierReceipts : Migration
{
    protected override void Up(MigrationBuilder b)
    {
        b.Sql("""
            CREATE TABLE Suppliers (
                Id INTEGER NOT NULL CONSTRAINT PK_Suppliers PRIMARY KEY AUTOINCREMENT,
                Name TEXT COLLATE NOCASE NOT NULL
            );
            CREATE UNIQUE INDEX IX_Suppliers_Name ON Suppliers(Name);
            INSERT INTO Suppliers(Name)
                SELECT MIN(trim(Supplier)) FROM Purchases
                WHERE length(trim(Supplier)) > 0 GROUP BY trim(Supplier) COLLATE NOCASE;
            ALTER TABLE Purchases ADD COLUMN SupplierId INTEGER NULL REFERENCES Suppliers(Id) ON DELETE RESTRICT;
            ALTER TABLE Purchases ADD COLUMN ConfirmationId TEXT NULL;
            ALTER TABLE Purchases ADD COLUMN PurchasedOn TEXT NULL;
            ALTER TABLE Purchases ADD COLUMN Notes TEXT NOT NULL DEFAULT '';
            UPDATE Purchases SET SupplierId = (SELECT Id FROM Suppliers WHERE Name = trim(Purchases.Supplier));
            CREATE INDEX IX_Purchases_SupplierId ON Purchases(SupplierId);
            CREATE UNIQUE INDEX IX_Purchases_ConfirmationId ON Purchases(ConfirmationId);
            ALTER TABLE Products ADD COLUMN PurchaseCost TEXT NULL;
            UPDATE Products SET PurchaseCost = (
                SELECT l.UnitCost FROM PurchaseLines l JOIN Purchases p ON p.Id = l.PurchaseId
                WHERE l.ProductId = Products.Id ORDER BY p.ReceivedAt DESC, l.Id DESC LIMIT 1
            );
            CREATE TABLE StockMovements (
                Id INTEGER NOT NULL CONSTRAINT PK_StockMovements PRIMARY KEY AUTOINCREMENT,
                ProductId INTEGER NOT NULL,
                ProductName TEXT NOT NULL,
                QuantityChange TEXT NOT NULL,
                Kind INTEGER NOT NULL,
                OccurredAt TEXT NOT NULL,
                Reference TEXT NULL
            );
            CREATE INDEX IX_StockMovements_ProductId_OccurredAt ON StockMovements(ProductId, OccurredAt);
            INSERT INTO StockMovements(ProductId, ProductName, QuantityChange, Kind, OccurredAt, Reference)
                SELECT l.ProductId, l.ProductName, l.Quantity, 1, p.ReceivedAt, CAST(p.Id AS TEXT)
                FROM PurchaseLines l JOIN Purchases p ON p.Id = l.PurchaseId;
            INSERT INTO StockMovements(ProductId, ProductName, QuantityChange, Kind, OccurredAt, Reference)
                SELECT l.ProductId, l.Name, '-' || l.Quantity, 2, s.CreatedAt, s.ReceiptNo
                FROM SaleLines l JOIN Sales s ON s.Id = l.SaleId;
            """);
    }

    protected override void Down(MigrationBuilder b) => b.Sql("""
        DROP TABLE StockMovements;
        ALTER TABLE Products DROP COLUMN PurchaseCost;
        DROP INDEX IX_Purchases_SupplierId;
        DROP INDEX IX_Purchases_ConfirmationId;
        ALTER TABLE Purchases DROP COLUMN Notes;
        ALTER TABLE Purchases DROP COLUMN PurchasedOn;
        ALTER TABLE Purchases DROP COLUMN ConfirmationId;
        ALTER TABLE Purchases DROP COLUMN SupplierId;
        DROP TABLE Suppliers;
        """);
}
