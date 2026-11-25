using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SupermarketPos.Infrastructure.Persistence.Migrations;
[DbContext(typeof(PosDbContext))]
[Migration(MigrationId)]
public sealed class BusinessOnboarding : Migration
{
    public const string MigrationId = "20261119140000_BusinessOnboarding";
    protected override void Up(MigrationBuilder m) => m.Sql("""
        CREATE TABLE BusinessConfigurations (Id INTEGER NOT NULL PRIMARY KEY, SchemaVersion INTEGER NOT NULL,
          ProfileJson TEXT NOT NULL, Step INTEGER NOT NULL, Completed INTEGER NOT NULL, ImportedExisting INTEGER NOT NULL,
          Revision INTEGER NOT NULL, RecoveryHash TEXT NULL, RecoveryUsername TEXT NULL, RecoveryFailures INTEGER NOT NULL, RecoveryLockedUntil TEXT NULL);
        CREATE TABLE BusinessAudits (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, At TEXT NOT NULL,
          Actor TEXT NOT NULL, Action TEXT NOT NULL, Summary TEXT NOT NULL);
        CREATE TABLE ProductTaxRules (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, ProductId INTEGER NOT NULL,
          HsCode TEXT NOT NULL, SaleType TEXT NOT NULL, Classification TEXT NOT NULL, Sro TEXT NOT NULL, AuthorityReference TEXT NOT NULL,
          Rate TEXT NOT NULL, EffectiveFrom TEXT NOT NULL, EffectiveTo TEXT NULL,
          FOREIGN KEY(ProductId) REFERENCES Products(Id) ON DELETE RESTRICT);
        CREATE UNIQUE INDEX IX_ProductTaxRules_ProductId_EffectiveFrom ON ProductTaxRules(ProductId,EffectiveFrom);
        CREATE TABLE FiscalSubmissions (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, SaleId INTEGER NOT NULL,
          Route TEXT NOT NULL, Environment TEXT NOT NULL, Status TEXT NOT NULL, InvoiceJson TEXT NOT NULL,
          AuthorityInvoiceNumber TEXT NULL, ValidationCodes TEXT NULL,
          FOREIGN KEY(SaleId) REFERENCES Sales(Id) ON DELETE RESTRICT);
        CREATE UNIQUE INDEX IX_FiscalSubmissions_SaleId ON FiscalSubmissions(SaleId);
        """);
    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("Restore the matching application and pre-onboarding database backup.");
}
