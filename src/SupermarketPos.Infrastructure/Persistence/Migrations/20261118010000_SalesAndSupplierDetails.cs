using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SupermarketPos.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PosDbContext))]
[Migration("20261118010000_SalesAndSupplierDetails")]
public sealed class SalesAndSupplierDetails : Migration
{
    protected override void Up(MigrationBuilder b) => b.Sql("""
        ALTER TABLE Sales ADD COLUMN CustomerName TEXT NULL;
        ALTER TABLE Sales ADD COLUMN CustomerPhone TEXT NULL;
        ALTER TABLE Sales ADD COLUMN CustomerEmail TEXT NULL;
        ALTER TABLE Sales ADD COLUMN CustomerAddress TEXT NULL;
        ALTER TABLE Sales ADD COLUMN SourceReceiptNo TEXT COLLATE NOCASE NULL;
        CREATE UNIQUE INDEX IX_Sales_SourceReceiptNo ON Sales(SourceReceiptNo);
        ALTER TABLE SaleLines ADD COLUMN UnitCost TEXT NULL;
        ALTER TABLE Suppliers ADD COLUMN Kind TEXT NULL;
        ALTER TABLE Suppliers ADD COLUMN ContactName TEXT NULL;
        ALTER TABLE Suppliers ADD COLUMN Phone TEXT NULL;
        ALTER TABLE Suppliers ADD COLUMN Email TEXT NULL;
        ALTER TABLE Suppliers ADD COLUMN Address TEXT NULL;
        ALTER TABLE Products ADD COLUMN SupplierId INTEGER NULL REFERENCES Suppliers(Id) ON DELETE SET NULL;
        CREATE INDEX IX_Products_SupplierId ON Products(SupplierId);
        """);

    protected override void Down(MigrationBuilder b) => b.Sql("""
        DROP INDEX IX_Products_SupplierId;
        ALTER TABLE Products DROP COLUMN SupplierId;
        ALTER TABLE Suppliers DROP COLUMN Address;
        ALTER TABLE Suppliers DROP COLUMN Email;
        ALTER TABLE Suppliers DROP COLUMN Phone;
        ALTER TABLE Suppliers DROP COLUMN ContactName;
        ALTER TABLE Suppliers DROP COLUMN Kind;
        ALTER TABLE SaleLines DROP COLUMN UnitCost;
        DROP INDEX IX_Sales_SourceReceiptNo;
        ALTER TABLE Sales DROP COLUMN SourceReceiptNo;
        ALTER TABLE Sales DROP COLUMN CustomerAddress;
        ALTER TABLE Sales DROP COLUMN CustomerEmail;
        ALTER TABLE Sales DROP COLUMN CustomerPhone;
        ALTER TABLE Sales DROP COLUMN CustomerName;
        """);
}
