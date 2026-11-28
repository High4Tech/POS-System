using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SupermarketPos.Infrastructure.Persistence;

namespace SupermarketPos.Tests;

public sealed class RetailCompletionMigrationTests
{
    [Fact]
    public async Task UpgradePreservesHistoryAndOnlyBackfillsUnambiguousReceivingCosts()
    {
        using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        await using var db=new PosDbContext(new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options);
        var migrator=db.GetService<IMigrator>();await migrator.MigrateAsync("20261119010000_AccountsAndDevices");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Categories(Id,Name) VALUES(1,'Legacy stock');
            INSERT INTO Products(Id,Barcode,Name,UnitPrice,VatRate,Unit,StockQuantity,CategoryId,TrackSerial) VALUES(1,'LEGACY','Legacy device','100','0',0,'3',1,1);
            INSERT INTO Purchases(Id,Supplier,Reference,ReceivedAt,ReceivedBy) VALUES(1,'Supplier','ONE','2026-11-01','Owner'),(2,'Supplier','TWO','2026-11-02','Owner');
            INSERT INTO PurchaseLines(PurchaseId,ProductId,ProductName,Barcode,Unit,Quantity,UnitCost) VALUES(1,1,'Legacy device','LEGACY',0,'1','20'),(2,1,'Legacy device','LEGACY',0,'1','10'),(2,1,'Legacy device','LEGACY',0,'1','40');
            INSERT INTO SerializedUnits(ProductId,Serial,PurchaseId,ReceivedOn) VALUES(1,'ORIGINAL-A',1,'2026-11-01'),(1,'AMBIGUOUS-B',2,'2026-11-02'),(1,'OPENING-C',NULL,'2026-11-01');
            """);
        await migrator.MigrateAsync("20261128130000_DigitalCustomerHistory");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO ServiceProviders(Id,Name,AllowedTypes,OpeningFloat,FeePolicy,CommissionPolicy,IsActive,Revision) VALUES(7,'Legacy provider','LOAD','0','','',1,0);
            INSERT INTO ServiceTransactions(Id,ConfirmationId,ProviderId,Type,Status,Principal,CustomerFee,ProviderFee,Commission,ProviderCost,Collected,FloatChange,CustomerPhone,Reference,OccurredAt,Operator,Revision,ProviderName,StoreName,PackageIdentifier,PackageName,AccountType,PackageBenefits,PackageValidity,InvoiceProfileJson,ProviderCostKnown,PaymentMethod) VALUES(1,'11111111-1111-1111-1111-111111111111',7,'LOAD','Confirmed','100','0','0','0','0','100','0','0300-1234567','legacy-ref','2026-11-01','operator',0,'Legacy provider','Shop','','','','','','',0,'CASH');
            INSERT INTO ServicePostings(TransactionId,CashChange,FloatChange,Income) VALUES(1,'100','0','0');
            """);
        await db.Database.MigrateAsync();
        var units=await db.SerializedUnits.OrderBy(u=>u.Id).ToArrayAsync();
        Assert.Equal(20,units[0].PurchaseCost);Assert.Null(units[1].PurchaseCost);Assert.Null(units[2].PurchaseCost);Assert.All(units,u=>Assert.Equal(0,u.SaleCycle));
        Assert.Equal(3,(await db.Products.SingleAsync()).StockQuantity);Assert.Equal(new decimal[]{20,10,40},await db.PurchaseLines.OrderBy(l=>l.Id).Select(l=>l.UnitCost).ToArrayAsync());
        Assert.All(await db.Purchases.ToArrayAsync(),p=>Assert.Equal("",p.RequestFingerprint));
        var posting=await db.ServicePostings.SingleAsync();Assert.Equal(new DateTime(2026,11,1),posting.PostedAt);Assert.Equal(100,posting.CashChange);
        Assert.Equal("legacy-ref",(await db.ServiceTransactions.SingleAsync()).Reference);
        Assert.Empty(db.CustomerCreditMovements);Assert.Empty(db.DeviceReturnReleases);Assert.False(db.Database.HasPendingModelChanges());
    }
}
