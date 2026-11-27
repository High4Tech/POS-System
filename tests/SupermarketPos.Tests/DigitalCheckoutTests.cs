using Microsoft.EntityFrameworkCore;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Business;

namespace SupermarketPos.Tests;
public sealed partial class DigitalLedgerTests
{
    private static ServiceTransaction CartEntry(ServiceProviderAccount account,string type,decimal amount,decimal fee=0)
    {
        var entry=DigitalServiceLedger.ProviderQuote(account,type,amount);entry.ConfirmationId=Guid.NewGuid();entry.Status="Confirmed";entry.OccurredAt=DateTime.Now;entry.CustomerPhone="03001234567";entry.CustomerFee=fee;if(type!="CASHOUT")entry.Collected=amount+fee;return entry;
    }
    [Fact]public async Task CashCheckoutGroupsItemsAndPostsOnceWithoutFloatFundingOrReferences()
    {
        using var db=new TestDb();var(ledger,id)=await Fixture(db);var account=(await ledger.ProvidersAsync()).Single();account.OpeningFloat=0;await ledger.SaveProviderAsync(account);
        var load=CartEntry(account,"LOAD",1000,10);load.ProviderCost=980;
        var package=new MobileServicePackage{ProviderId=id,Name="Catalog package",Price=300,ProviderCostKnown=false};await ledger.SavePackageAsync(package);
        var pack=DigitalServiceLedger.PackageQuote(package);pack.ConfirmationId=Guid.NewGuid();pack.Status="Confirmed";pack.OccurredAt=DateTime.Now;pack.CustomerPhone="03001234567";
        var cashout=CartEntry(account,"CASHOUT",2000,50);var lines=new[]{load,pack,cashout};var key=Guid.NewGuid();
        var checkout=await ledger.CheckoutAsync(key,lines,"CASH",0);
        Assert.Equal(-640,checkout.Total);Assert.Equal(3,checkout.Lines.Count);Assert.StartsWith("DS-",checkout.ReceiptNumber);Assert.All(checkout.Lines,t=>Assert.Equal(checkout.ReceiptNumber,t.ReceiptNumber));
        Assert.Equal(-640,(await ledger.PostingsAsync()).Sum(p=>p.CashChange));Assert.All(await ledger.PostingsAsync(),p=>Assert.Equal(0,p.FloatChange));Assert.Equal(80,(await ledger.PostingsAsync()).Sum(p=>p.Income));
        var retry=await ledger.CheckoutAsync(key,lines,"CASH",0);Assert.Equal(checkout.Id,retry.Id);Assert.Equal(3,await db.Context.ServicePostings.CountAsync());Assert.Single(db.Context.ServiceCheckouts);Assert.Empty(db.Context.Sales);Assert.Empty(db.Context.Products);
        var saved=await ledger.CheckoutByIdAsync(checkout.Id);Assert.Equal(3,saved!.Lines.Count);Assert.All(saved.Lines,t=>Assert.Empty(t.Reference));
    }
    [Fact]public async Task FailedSecondRowRollsBackTheEntireCheckout()
    {
        using var db=new TestDb();var(ledger,_)=await Fixture(db);var account=(await ledger.ProvidersAsync()).Single();var first=CartEntry(account,"LOAD",100);var second=CartEntry(account,"LOAD",200);second.ProviderId=int.MaxValue;
        await Assert.ThrowsAsync<DomainException>(()=>ledger.CheckoutAsync(Guid.NewGuid(),[first,second],"CASH",300));
        Assert.Empty(db.Context.ServiceCheckouts);Assert.Empty(db.Context.ServiceTransactions);Assert.Empty(db.Context.ServicePostings);
    }
    [Fact]public async Task CardCheckoutKeepsCardReceiptsOutOfPhysicalCashAndRejectsCashOut()
    {
        using var db=new TestDb();var(ledger,_)=await Fixture(db);var account=(await ledger.ProvidersAsync()).Single();var line=CartEntry(account,"LOAD",500,10);line.ProviderCost=480;
        var invoice=await ledger.CheckoutAsync(Guid.NewGuid(),[line],"CARD",510);Assert.Equal(510,invoice.Total);Assert.Equal(0,invoice.Change);Assert.Equal(0,db.Context.ServicePostings.Single().CashChange);Assert.Equal(30,db.Context.ServicePostings.Single().Income);
        await Assert.ThrowsAsync<DomainException>(()=>ledger.CheckoutAsync(Guid.NewGuid(),[CartEntry(account,"CASHOUT",100)],"CARD",0));Assert.Single(db.Context.ServiceCheckouts);
    }
    [Fact]public async Task InvalidTenderedOrConflictingRetryCannotPostExtraMoney()
    {
        using var db=new TestDb();var(ledger,_)=await Fixture(db);var account=(await ledger.ProvidersAsync()).Single();var row=CartEntry(account,"TRANSFER",100,5);var key=Guid.NewGuid();
        await Assert.ThrowsAsync<DomainException>(()=>ledger.CheckoutAsync(key,[row],"CASH",104));Assert.Empty(db.Context.ServiceCheckouts);
        var invoice=await ledger.CheckoutAsync(key,[row],"CASH",110);Assert.Equal(5,invoice.Change);
        row.CustomerPhone="Another recipient";await Assert.ThrowsAsync<DomainException>(()=>ledger.CheckoutAsync(key,[row],"CASH",110));Assert.Single(db.Context.ServicePostings);
    }
    [Fact]public async Task DifferentSalesWithBlankReferencesAreAllowedAndUnknownCostIsNotProfit()
    {
        using var db=new TestDb();var(ledger,_)=await Fixture(db);var account=(await ledger.ProvidersAsync()).Single();
        foreach(var price in new[]{100,200}){var row=CartEntry(account,"LOAD",price);row.ProviderCost=0;row.ProviderCostKnown=false;await ledger.CheckoutAsync(Guid.NewGuid(),[row],"CASH",price);}
        Assert.Equal(2,await db.Context.ServiceCheckouts.CountAsync());var report=await ledger.ReportAsync(DateTime.Today,DateTime.Today.AddDays(1));Assert.Equal(0,report.Income);Assert.Equal(2,report.UnknownCosts);
    }
    [Fact]public async Task ProviderChargeDefaultsPersistAndInvalidDestinationsCannotPost()
    {
        using var db=new TestDb();var(ledger,_)=await Fixture(db);var provider=(await ledger.ProvidersAsync()).Single();provider.CustomerFeeMode="PERCENT";provider.CustomerFeePercent=2;provider.AccountNumberLengths="14";await ledger.SaveProviderAsync(provider);
        var saved=(await ledger.ProvidersAsync()).Single();Assert.Equal(20,saved.CustomerCharge(1000));Assert.Equal("14",saved.AccountNumberLengths);
        var row=CartEntry(saved,"TRANSFER",1000,20);row.CustomerPhone="123";await Assert.ThrowsAsync<DomainException>(()=>ledger.CheckoutAsync(Guid.NewGuid(),[row],"CASH",1020));Assert.Empty(db.Context.ServicePostings);
        row=CartEntry(saved,"TRANSFER",1000,25);row.CustomerPhone="00123456789012";await ledger.CheckoutAsync(Guid.NewGuid(),[row],"CASH",1025);Assert.Equal(2,(await ledger.ProvidersAsync()).Single().CustomerFeePercent);Assert.Equal(25,db.Context.ServicePostings.Single().Income);
    }
}
