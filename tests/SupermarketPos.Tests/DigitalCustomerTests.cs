using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Data.Sqlite;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Infrastructure.Persistence;

namespace SupermarketPos.Tests;
public sealed partial class DigitalLedgerTests
{
    [Theory]
    [InlineData("LOAD","0300 1234567",1,"phone:923001234567")]
    [InlineData("PACKAGE","+92-300-1234567",2,"phone:923001234567")]
    [InlineData("TRANSFER","00923001234567",3,"phone:923001234567")]
    [InlineData("TRANSFER","PK36 SCBL 0000 0011 2345 6702",1,"iban:PK36SCBL0000001123456702")]
    [InlineData("TRANSFER","00123456789012",1,"account:1:00123456789012")]
    [InlineData("CASHOUT","00123456789012",2,"account:2:00123456789012")]
    public void RecipientKeysUnifyPhonesAndKeepBankAccountsSeparate(string type,string value,int provider,string expected)
    {
        Assert.Equal(expected,DigitalRecipient.Key(type,value,provider));
        Assert.DoesNotContain(':',DigitalRecipient.Identifier(expected));
    }

    [Fact]public async Task ReturningPhoneGroupsLoadPackageAndWalletWithoutMandatoryName()
    {
        using var db=new TestDb();var(ledger,_)=await Fixture(db);var provider=(await ledger.ProvidersAsync()).Single();
        var first=CartEntry(provider,"LOAD",100);await ledger.CheckoutAsync(Guid.NewGuid(),[first],"CASH",100);
        var package=new MobileServicePackage{ProviderId=provider.Id,Name="Existing package",Price=200};await ledger.SavePackageAsync(package);
        var second=DigitalServiceLedger.PackageQuote(package);second.ConfirmationId=Guid.NewGuid();second.OccurredAt=DateTime.Now;second.Status="Confirmed";second.CustomerPhone="+923001234567";await ledger.CheckoutAsync(Guid.NewGuid(),[second],"CASH",200);
        var wallet=CartEntry(provider,"TRANSFER",500);wallet.CustomerPhone="00923001234567";await ledger.CheckoutAsync(Guid.NewGuid(),[wallet],"CASH",500);
        var customer=Assert.Single(await ledger.CustomersAsync());Assert.Equal(3,customer.Transactions);Assert.Empty(customer.Name);Assert.Equal("923001234567",customer.Identifier);
        Assert.True(customer.Matches("03001234567"));Assert.True(customer.Matches("+92 300 1234567"));Assert.False(customer.Matches("03001234568"));
        Assert.True(customer.Matches("0300"));Assert.True(customer.Matches("+92 300"));
        Assert.Equal(3,(await ledger.CustomerHistoryAsync(customer.Key)).Count);Assert.Empty(db.Context.DigitalCustomers);Assert.Empty(db.Context.Sales);
    }

    [Fact]public async Task OptionalNamesAreReusableWithoutRewritingReceiptsOrDuplicatingRetries()
    {
        using var db=new TestDb();var(ledger,_)=await Fixture(db);var provider=(await ledger.ProvidersAsync()).Single();
        var first=CartEntry(provider,"LOAD",100);first.CustomerName=" First name ";var key=Guid.NewGuid();
        await ledger.CheckoutAsync(key,[first],"CASH",100);
        var recipient=Assert.Single(await ledger.CustomersAsync());Assert.Equal("First name",recipient.Name);
        await ledger.SaveCustomerNameAsync(recipient.Key,"Updated name");
        Assert.Equal("First name",(await ledger.CustomerHistoryAsync(recipient.Key)).Single().CustomerName);
        await ledger.CheckoutAsync(key,[first],"CASH",100);Assert.Single(db.Context.ServicePostings);
        var second=CartEntry(provider,"LOAD",200);await ledger.CheckoutAsync(Guid.NewGuid(),[second],"CASH",200);
        Assert.Equal("Updated name",second.CustomerName);
        var reversal=await ledger.ReverseAsync(first.Id,"",true);Assert.Equal(recipient.Key,reversal.RecipientKey);Assert.Equal("First name",reversal.CustomerName);
        Assert.Equal(3,(await ledger.CustomerHistoryAsync(recipient.Key)).Count);Assert.Equal(2,Assert.Single(await ledger.CustomersAsync()).Transactions);
        await ledger.SaveCustomerNameAsync(recipient.Key,"");Assert.Empty(Assert.Single(await ledger.CustomersAsync()).Name);
        await Assert.ThrowsAsync<DomainException>(()=>ledger.SaveCustomerNameAsync(recipient.Key,new string('x',151)));
    }

    [Fact]public async Task InvalidTransactionRollsBackItsCustomerNameAndFinancialRows()
    {
        using var db=new TestDb();var(ledger,_)=await Fixture(db);var provider=(await ledger.ProvidersAsync()).Single();
        var row=CartEntry(provider,"TRANSFER",100);row.CustomerPhone="123";row.CustomerName="Must not save";
        await Assert.ThrowsAsync<DomainException>(()=>ledger.CheckoutAsync(Guid.NewGuid(),[row],"CASH",100));
        Assert.Empty(db.Context.DigitalCustomers);Assert.Empty(db.Context.ServiceTransactions);Assert.Empty(db.Context.ServiceCheckouts);
    }

    [Fact]public async Task ExistingHistoryGetsRecipientKeysWithoutChangingOriginalValues()
    {
        using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        await using var db=new PosDbContext(new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261127160000_ProviderChargeDefaults");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO ServiceProviders (Id,Name,AllowedTypes,OpeningFloat,FeePolicy,CommissionPolicy,IsActive,Revision) VALUES(7,\'Legacy provider\',\'LOAD\',\'0\',\'\',\'\',1,0)");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO ServiceTransactions (Id,ConfirmationId,ProviderId,Type,Status,Principal,CustomerFee,ProviderFee,Commission,ProviderCost,Collected,FloatChange,CustomerPhone,Reference,OccurredAt,Operator,Revision,ProviderName,StoreName,PackageIdentifier,PackageName,AccountType,PackageBenefits,PackageValidity,InvoiceProfileJson,ProviderCostKnown,PaymentMethod) VALUES(1,'11111111-1111-1111-1111-111111111111',7,'LOAD','Confirmed','100','0','0','0','0','100','0','0300-1234567','legacy-ref','2026-11-01','operator',0,'Legacy provider','Shop','','','','','','',0,'CASH')");
        await db.Database.MigrateAsync();
        var row=await db.ServiceTransactions.SingleAsync();Assert.Equal("phone:923001234567",row.RecipientKey);Assert.Equal("0300-1234567",row.CustomerPhone);Assert.Equal("legacy-ref",row.Reference);Assert.Equal(100,row.Principal);Assert.Empty(row.CustomerName);Assert.Empty(db.DigitalCustomers);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
