using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SupermarketPos.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PosDbContext))]
[Migration(MigrationId)]
public sealed class AccountsAndDevices : Migration
{
    public const string MigrationId = "20261119010000_AccountsAndDevices";

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE Products ADD COLUMN TrackSerial INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE Products ADD COLUMN WarrantyMonths INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE Sales ADD COLUMN BalancePaid TEXT NOT NULL DEFAULT '0';
        ALTER TABLE Sales ADD COLUMN BalanceRevision INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE Sales ADD COLUMN CustomerAccountKey TEXT NULL;
        ALTER TABLE Purchases ADD COLUMN InitialAmountPaid TEXT NULL;
        ALTER TABLE Purchases ADD COLUMN InitialPaymentMethod TEXT NOT NULL DEFAULT 'CASH';
        ALTER TABLE Purchases ADD COLUMN BalancePaid TEXT NOT NULL DEFAULT '0';
        ALTER TABLE Purchases ADD COLUMN BalanceRevision INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE SaleLines ADD COLUMN Imei1 TEXT NULL;
        ALTER TABLE SaleLines ADD COLUMN Imei2 TEXT NULL;
        ALTER TABLE SaleLines ADD COLUMN WarrantyMonths INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE SaleLines ADD COLUMN WarrantyUntil TEXT NULL;
        CREATE TABLE AccountPayments (
            Id INTEGER NOT NULL CONSTRAINT PK_AccountPayments PRIMARY KEY AUTOINCREMENT,
            ConfirmationId TEXT NOT NULL, SaleId INTEGER NULL, PurchaseId INTEGER NULL,
            Amount TEXT NOT NULL, Method TEXT NOT NULL, PaidOn TEXT NOT NULL, RecordedAt TEXT NOT NULL,
            Reference TEXT NOT NULL, Operator TEXT NOT NULL,
            FOREIGN KEY (SaleId) REFERENCES Sales(Id) ON DELETE RESTRICT,
            FOREIGN KEY (PurchaseId) REFERENCES Purchases(Id) ON DELETE RESTRICT,
            CHECK ((SaleId IS NULL) <> (PurchaseId IS NULL)));
        CREATE UNIQUE INDEX IX_AccountPayments_ConfirmationId ON AccountPayments(ConfirmationId);
        CREATE INDEX IX_AccountPayments_SaleId ON AccountPayments(SaleId);
        CREATE INDEX IX_AccountPayments_PurchaseId ON AccountPayments(PurchaseId);
        CREATE TABLE SerializedUnits (
            Id INTEGER NOT NULL CONSTRAINT PK_SerializedUnits PRIMARY KEY AUTOINCREMENT,
            ProductId INTEGER NOT NULL, Serial TEXT NOT NULL, Imei1 TEXT NULL, Imei2 TEXT NULL,
            PurchaseId INTEGER NULL, SaleId INTEGER NULL, ReceivedOn TEXT NOT NULL, SoldOn TEXT NULL, WarrantyUntil TEXT NULL,
            FOREIGN KEY (ProductId) REFERENCES Products(Id) ON DELETE RESTRICT,
            FOREIGN KEY (PurchaseId) REFERENCES Purchases(Id) ON DELETE RESTRICT,
            FOREIGN KEY (SaleId) REFERENCES Sales(Id) ON DELETE RESTRICT);
        CREATE UNIQUE INDEX IX_SerializedUnits_Serial ON SerializedUnits(Serial);
        CREATE UNIQUE INDEX IX_SerializedUnits_Imei1 ON SerializedUnits(Imei1);
        CREATE UNIQUE INDEX IX_SerializedUnits_Imei2 ON SerializedUnits(Imei2);
        CREATE INDEX IX_SerializedUnits_ProductId ON SerializedUnits(ProductId);
        CREATE INDEX IX_SerializedUnits_PurchaseId ON SerializedUnits(PurchaseId);
        CREATE INDEX IX_SerializedUnits_SaleId ON SerializedUnits(SaleId);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Restore the pre-upgrade backup to undo accounts and device tracking.");
}
