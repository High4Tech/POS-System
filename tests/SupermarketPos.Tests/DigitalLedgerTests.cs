using Microsoft.EntityFrameworkCore;
using SupermarketPos.Application.Services;
using SupermarketPos.Domain.Business;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Infrastructure.Persistence;

namespace SupermarketPos.Tests;
public sealed partial class DigitalLedgerTests
{
    private static async Task<(DigitalServiceLedger Ledger,int Provider)> Fixture(TestDb db,bool techRetail=false)
    {
        db.Context.Settings.Add(new AppSettings("Shop","","en",5,[0m]));await db.Context.SaveChangesAsync();
        var hasher=new BCryptPasswordHasher();var auth=new AuthService(new UserRepository(db.Context),hasher);var work=new UnitOfWork(db.Context);var setup=new BusinessSetup(db.Context,work,auth,hasher);
        var profile=new BusinessProfile{StoreName="Shop",EnableDigitalWalletServices=true,EnableMobileRecharge=true};
        if(techRetail){profile.BusinessType="electronics";profile.ApplyBusinessDefaults();}
        await setup.CompleteAsync(profile,"owner","TestPassword123");await auth.LoginAsync("owner","TestPassword123");
        var ledger=new DigitalServiceLedger(db.Context,work,auth,setup);var provider=new ServiceProviderAccount{Name="Test provider",AllowedTypes="CASHIN,CASHOUT,TRANSFER,LOAD,PACKAGE",OpeningFloat=2000};await ledger.SaveProviderAsync(provider);return(ledger,provider.Id);
    }
    private static ServiceTransaction Entry(int provider,string type="CASHIN",string status="Confirmed")=>new(){ConfirmationId=Guid.NewGuid(),ProviderId=provider,Type=type,Status=status,Principal=1000,CustomerPhone="03001234567",Reference="TEST-REFERENCE",OccurredAt=DateTime.Now,FloatChange=-1000};
    [Fact]public async Task TechRetailDefaultsSupportLoadAndConfiguredPackagesWithoutPhysicalStock()
    {
        using var db=new TestDb();var(ledger,provider)=await Fixture(db,techRetail:true);
        var load=DigitalServiceLedger.ProviderQuote((await ledger.ProvidersAsync()).Single(),"LOAD",100);
        load.ConfirmationId=Guid.NewGuid();load.OccurredAt=DateTime.Now;load.Status="Confirmed";load.CustomerPhone="03001234567";load.Reference="TECH-LOAD";
        await ledger.SaveAsync(load,true);
        var package=new MobileServicePackage{ProviderId=provider,Name="Configured test package",Price=500,ProviderCost=480};await ledger.SavePackageAsync(package);
        var purchase=DigitalServiceLedger.PackageQuote(package);purchase.ConfirmationId=Guid.NewGuid();purchase.OccurredAt=DateTime.Now;purchase.Status="Confirmed";purchase.CustomerPhone="03001234567";purchase.Reference="TECH-PACKAGE";
        await ledger.SaveAsync(purchase,true);await ledger.SaveAsync(purchase,true);
        Assert.Equal(2,db.Context.ServicePostings.Count());Assert.Empty(db.Context.Products);Assert.Empty(db.Context.Sales);
    }
    [Fact]public async Task PharmacyRestrictionsAreEnforcedEvenForForgedConfigurationFlags()
    {
        using var db=new TestDb();var (ledger,provider)=await Fixture(db);var pending=await ledger.SaveAsync(Entry(provider,status:"Pending"),false);
        db.Context.BusinessConfigurations.Single().ProfileJson=System.Text.Json.JsonSerializer.Serialize(new BusinessProfile{StoreName="Pharmacy",BusinessType="pharmacy",EnableDigitalWalletServices=true,EnableMobileRecharge=true});await db.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<DomainException>(()=>ledger.SaveAsync(Entry(provider),true));await Assert.ThrowsAsync<DomainException>(()=>ledger.SetStatusAsync(pending.Id,"Confirmed",true));
        await Assert.ThrowsAsync<DomainException>(()=>ledger.ReverseAsync(pending.Id,"Blocked pharmacy reversal",true));
        await Assert.ThrowsAsync<DomainException>(()=>ledger.SaveProviderAsync(new(){Name="Blocked"}));Assert.Empty(db.Context.ServicePostings);Assert.Single(await ledger.HistoryAsync());
    }
    [Fact]public async Task CashInPostsOnceAndNeverBecomesRetailRevenue()
    {
        using var db=new TestDb();var (ledger,provider)=await Fixture(db);var tx=Entry(provider);await ledger.SaveAsync(tx,true);await ledger.SaveAsync(tx,true);await ledger.SetStatusAsync(tx.Id,"Confirmed",true);
        Assert.Single(db.Context.ServicePostings);Assert.Equal(1000,await ledger.FloatAsync(provider));var posting=db.Context.ServicePostings.Single();Assert.Equal(1000,posting.CashChange);Assert.Equal(0,posting.Income);Assert.Empty(db.Context.Sales);
    }
    [Fact]public async Task CashOutCountsOnlyFeeAndReversalIsSeparateAndIdempotent()
    {
        using var db=new TestDb();var (ledger,provider)=await Fixture(db);var tx=Entry(provider,"CASHOUT");tx.CustomerFee=20;tx.FloatChange=1000;await ledger.SaveAsync(tx,true);
        Assert.Equal(-980,db.Context.ServicePostings.Single().CashChange);Assert.Equal(20,db.Context.ServicePostings.Single().Income);
        var reversal=await ledger.ReverseAsync(tx.Id,"External reversal confirmed",true);var again=await ledger.ReverseAsync(tx.Id,"External reversal confirmed",true);Assert.Equal(reversal.Id,again.Id);
        Assert.Equal(2,db.Context.ServicePostings.Count());Assert.Equal(2000,await ledger.FloatAsync(provider));var report=await ledger.ReportAsync(DateTime.Today,DateTime.Today.AddDays(1));Assert.Equal(0,report.Income);Assert.Equal(0,report.CashChange);Assert.Equal(0,report.CashOut);Assert.Empty(db.Context.Sales);
    }
    [Theory][InlineData("Pending")][InlineData("Failed")][InlineData("Draft")]
    public async Task UnconfirmedStatesNeverPostMoney(string state)
    {
        using var db=new TestDb();var (ledger,provider)=await Fixture(db);await ledger.SaveAsync(Entry(provider,status:state),false);Assert.Empty(db.Context.ServicePostings);Assert.Equal(2000,await ledger.FloatAsync(provider));
    }
    [Fact]public async Task ManualRecordsDoNotRequireVerificationReferenceOrFundedBalance()
    {
        using var db=new TestDb();var (ledger,provider)=await Fixture(db);
        var account=await db.Context.ServiceProviders.FindAsync(provider);account!.OpeningFloat=0;await db.Context.SaveChangesAsync();
        var tx=DigitalServiceLedger.ProviderQuote(account,"CASHOUT",1000);tx.Status="Confirmed";tx.ConfirmationId=Guid.NewGuid();tx.OccurredAt=DateTime.Now;tx.CustomerPhone="03001234567";
        await ledger.SaveAsync(tx,false);Assert.Equal(-1000,db.Context.ServicePostings.Single().CashChange);Assert.Equal(0,db.Context.ServicePostings.Single().FloatChange);
    }
    [Fact]public async Task RechargeRecordsActualCostsAndNoPhysicalInventory()
    {
        using var db=new TestDb();var (ledger,provider)=await Fixture(db);var tx=Entry(provider,"LOAD");tx.CustomerPhone="03001234567";tx.Principal=500;tx.ProviderCost=480;tx.Collected=500;tx.Commission=5;tx.ProviderFee=2;tx.FloatChange=-480;await ledger.SaveAsync(tx,true);
        Assert.Equal(23,db.Context.ServicePostings.Single().Income);Assert.Equal(500,db.Context.ServicePostings.Single().CashChange);Assert.Empty(db.Context.Products);Assert.Empty(db.Context.Sales);
    }
    [Fact]public async Task ReenteringAnAlreadyConfirmedProviderReferenceDoesNotPostTwice()
    {
        using var db=new TestDb();var (ledger,provider)=await Fixture(db);await ledger.SaveAsync(Entry(provider),true);
        await Assert.ThrowsAsync<DomainException>(()=>ledger.SaveAsync(Entry(provider),true));Assert.Single(db.Context.ServicePostings);
    }
    [Fact]public async Task PackageQuotePostsCatalogPricesOnceAndSnapshotsDetails()
    {
        using var db=new TestDb();var (ledger,provider)=await Fixture(db);
        var package=new MobileServicePackage{ProviderId=provider,Name="Configured test package",PackageIdentifier="TEST-ONLY",AccountType="Postpaid",Benefits="Test benefits",Validity="Test validity",Price=500,ProviderCost=480,Commission=5,CustomerFee=10,ProviderFee=2};await ledger.SavePackageAsync(package);
        var tx=DigitalServiceLedger.PackageQuote(package);tx.ConfirmationId=Guid.NewGuid();tx.CustomerPhone="923001234567";tx.OccurredAt=DateTime.Now;tx.Status="Pending";
        await ledger.SaveAsync(tx,false);Assert.Empty(db.Context.ServicePostings);
        await ledger.SetStatusAsync(tx.Id,"Confirmed",true,"PACKAGE-CONFIRMED");await ledger.SetStatusAsync(tx.Id,"Confirmed",true,"PACKAGE-CONFIRMED");
        var post=Assert.Single(db.Context.ServicePostings);Assert.Equal(510,post.CashChange);Assert.Equal(33,post.Income);Assert.Equal(0,post.FloatChange);
        package.Name="Changed catalog";package.Benefits="Changed benefits";await ledger.SavePackageAsync(package);
        Assert.Equal("Configured test package",tx.PackageName);Assert.Equal("Test benefits",tx.PackageBenefits);Assert.Equal("Postpaid",tx.AccountType);Assert.Equal("TEST-ONLY",tx.PackageIdentifier);
        Assert.Empty(db.Context.Products);Assert.Empty(db.Context.Sales);
    }
    [Fact]public async Task PackageWithStaleOrEditedTariffCannotPostMoney()
    {
        using var db=new TestDb();var (ledger,provider)=await Fixture(db);
        var package=new MobileServicePackage{ProviderId=provider,Name="Test package",Price=500,ProviderCost=480};await ledger.SavePackageAsync(package);
        var tx=DigitalServiceLedger.PackageQuote(package);tx.ConfirmationId=Guid.NewGuid();tx.CustomerPhone="03001234567";tx.OccurredAt=DateTime.Now;tx.Status="Confirmed";tx.Reference="TEST";
        tx.Collected=1;await Assert.ThrowsAsync<DomainException>(()=>ledger.SaveAsync(tx,true));Assert.Empty(db.Context.ServiceTransactions);Assert.Empty(db.Context.ServicePostings);
    }
    [Fact]public async Task UnconfiguredAccountTypeIsRejectedAndLegacyPackagesDefaultToPrepaid()
    {
        using var db=new TestDb();var (ledger,provider)=await Fixture(db);var package=new MobileServicePackage{ProviderId=provider,Name="Test package",Price=100};
        Assert.Equal("Prepaid",package.AccountType);package.AccountType="Anything";await Assert.ThrowsAsync<DomainException>(()=>ledger.SavePackageAsync(package));Assert.Empty(db.Context.MobileServicePackages);
    }
    [Theory][InlineData("CASHIN",1010)][InlineData("CASHOUT",-990)][InlineData("LOAD",1010)]
    public void CompactTransactionsUseConfiguredFeesWithoutManualReentry(string type,decimal expectedCash)
    {
        var quote=DigitalServiceLedger.ProviderQuote(new(){Id=1,DefaultCustomerFee=10,DefaultProviderFee=3,DefaultCommission=10,LoadCostRate=.98m},type,1000);
        Assert.Equal(0,quote.FloatChange);Assert.Equal(expectedCash,quote.CashChange);
    }
    [Fact]public async Task ExistingPackagesMigrateWithoutInventingTariffsOrUnknownAccountTypes()
    {
        using var connection=new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        await using var db=new PosDbContext(new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options);
        await Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db).MigrateAsync("20261119190000_DigitalServices");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO ServiceProviders(Id,Name,AllowedTypes,OpeningFloat,FeePolicy,CommissionPolicy,IsActive,Revision) VALUES(1,'Test provider','PACKAGE','5000','','',1,0); INSERT INTO MobileServicePackages(ProviderId,Name,Kind,Price,ProviderCost,Commission,Validity,IsActive) VALUES(1,'Old postpaid','Postpaid','100','90','2','Saved validity',1),(1,'Unknown account type','Data','200','180','0','Saved validity',1);");
        await db.Database.MigrateAsync();var rows=await db.MobileServicePackages.OrderBy(p=>p.Id).ToArrayAsync();Assert.Equal("Postpaid",rows[0].AccountType);Assert.Equal("Unspecified",rows[1].AccountType);Assert.Equal(100,rows[0].Price);Assert.Equal(180,rows[1].ProviderCost);Assert.Empty(db.ServiceTransactions);
    }
}
