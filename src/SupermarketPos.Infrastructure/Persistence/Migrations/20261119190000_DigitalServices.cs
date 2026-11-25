using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace SupermarketPos.Infrastructure.Persistence.Migrations;
[DbContext(typeof(PosDbContext))][Migration("20261119190000_DigitalServices")]
public sealed class DigitalServices : Migration
{
    public const string MigrationId="20261119190000_DigitalServices";
    protected override void Up(MigrationBuilder m)=>m.Sql("""
        CREATE TABLE ServiceProviders(Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,Name TEXT NOT NULL,AllowedTypes TEXT NOT NULL,OpeningFloat TEXT NOT NULL,FeePolicy TEXT NOT NULL,CommissionPolicy TEXT NOT NULL,IsActive INTEGER NOT NULL,Revision INTEGER NOT NULL);
        CREATE UNIQUE INDEX IX_ServiceProviders_Name ON ServiceProviders(Name);
        CREATE TABLE MobileServicePackages(Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,ProviderId INTEGER NOT NULL,Name TEXT NOT NULL,Kind TEXT NOT NULL,Price TEXT NOT NULL,ProviderCost TEXT NOT NULL,Commission TEXT NOT NULL,Validity TEXT NOT NULL,IsActive INTEGER NOT NULL,FOREIGN KEY(ProviderId) REFERENCES ServiceProviders(Id) ON DELETE RESTRICT);
        CREATE TABLE ServiceTransactions(Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,ConfirmationId TEXT NOT NULL,ProviderId INTEGER NOT NULL,PackageId INTEGER NULL,ReversesId INTEGER NULL,Type TEXT NOT NULL,Status TEXT NOT NULL,Reference TEXT NOT NULL,CustomerPhone TEXT NOT NULL,Operator TEXT NOT NULL,OccurredAt TEXT NOT NULL,StoreName TEXT NOT NULL,ProviderName TEXT NOT NULL,PackageName TEXT NOT NULL,Principal TEXT NOT NULL,CustomerFee TEXT NOT NULL,ProviderFee TEXT NOT NULL,Commission TEXT NOT NULL,ProviderCost TEXT NOT NULL,Collected TEXT NOT NULL,FloatChange TEXT NOT NULL,Revision INTEGER NOT NULL,FOREIGN KEY(ProviderId) REFERENCES ServiceProviders(Id) ON DELETE RESTRICT,FOREIGN KEY(ReversesId) REFERENCES ServiceTransactions(Id) ON DELETE RESTRICT);
        CREATE UNIQUE INDEX IX_ServiceTransactions_ConfirmationId ON ServiceTransactions(ConfirmationId);
        CREATE UNIQUE INDEX IX_ServiceTransactions_ReversesId ON ServiceTransactions(ReversesId);
        CREATE INDEX IX_ServiceTransactions_ProviderId ON ServiceTransactions(ProviderId);
        CREATE TABLE ServicePostings(Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,TransactionId INTEGER NOT NULL,CashChange TEXT NOT NULL,FloatChange TEXT NOT NULL,Income TEXT NOT NULL,FOREIGN KEY(TransactionId) REFERENCES ServiceTransactions(Id) ON DELETE RESTRICT);
        CREATE UNIQUE INDEX IX_ServicePostings_TransactionId ON ServicePostings(TransactionId);
        CREATE INDEX IX_MobileServicePackages_ProviderId ON MobileServicePackages(ProviderId);
        """);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Restore the matching application and data backup.");
}
