using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Application.Services;
using SupermarketPos.Domain.Business;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Infrastructure.Commercial;
using SupermarketPos.Infrastructure.Persistence;

namespace SupermarketPos.Tests;
public sealed class CommercialUpgradeTests
{
    private static (BusinessSetup Setup,AuthService Auth) Services(TestDb test)
    {
        var hash=new BCryptPasswordHasher();var auth=new AuthService(new UserRepository(test.Context),hash);
        return(new(test.Context,new UnitOfWork(test.Context),auth,hash),auth);
    }
    private static async Task SeedSettings(TestDb test)
    {
        test.Context.Settings.Add(new AppSettings("Shop","","en",5,[0m,.18m]));await test.Context.SaveChangesAsync();
    }
    [Fact]
    public async Task FreshSetupResumesInUrduCreatesStrongOwnerAndOneUseRecovery()
    {
        using var db=new TestDb();await SeedSettings(db);var (setup,auth)=Services(db);
        Assert.False((await setup.LoadAsync()).HasUsers);
        await setup.SaveDraftAsync(new(){StoreName="My shop",Language="ur",InvoiceLanguage="ur"},4);
        var resumed=await setup.LoadAsync();Assert.Equal(4,resumed.Step);Assert.Equal("ur",resumed.Profile.Language);Assert.False(resumed.Completed);
        await Assert.ThrowsAsync<BusinessValidationException>(()=>setup.CompleteAsync(resumed.Profile,"owner","short"));
        var code=await setup.CompleteAsync(resumed.Profile,"owner","LocalPassword123!");Assert.NotNull(code);
        Assert.True((await setup.LoadAsync()).Completed);Assert.DoesNotContain("LocalPassword123!",JsonSerializer.Serialize(db.Context.Users.Single()));
        Assert.NotEqual(code,db.Context.BusinessConfigurations.Single().RecoveryHash);
        await auth.LoginAsync("owner","LocalPassword123!");
        await setup.RecoverAsync("owner",code!,"Replacement123!");await auth.LoginAsync("owner","Replacement123!");
        await Assert.ThrowsAsync<BusinessValidationException>(()=>setup.RecoverAsync("owner",code!,"AnotherPassword123!"));
        await setup.RestartAsync();Assert.False((await setup.LoadAsync()).Completed);Assert.Single(db.Context.Users);
    }
    [Fact]
    public async Task StoreIdentityIsSharedAcrossBusinessAndOriginalProfileSettings()
    {
        using var db=new TestDb();await SeedSettings(db);var (setup,auth)=Services(db);
        await setup.CompleteAsync(new(){StoreName="Demo Account",LegalName="Actual store"},"owner","LocalPassword123!");await auth.LoginAsync("owner","LocalPassword123!");
        Assert.Equal("Actual store",db.Context.Settings.Single().StoreName);Assert.Equal("Actual store",(await setup.LoadAsync()).Profile.StoreName);
        var settings=db.Context.Settings.Single();settings.Update("Trading name",settings.StoreAddress,settings.Language,settings.LowStockThreshold,settings.VatRates);await new SettingsRepository(db.Context).SaveAsync(settings);
        var profile=(await setup.LoadAsync()).Profile;Assert.Equal("Trading name",profile.StoreName);Assert.Equal("Actual store",profile.LegalName);
    }
    [Fact]
    public async Task PartialDraftRetainsInputButCannotBecomeCompletedBusiness()
    {
        using var db=new TestDb();await SeedSettings(db);var (setup,_)=Services(db);
        var draft=new BusinessProfile{StoreName="Test",Registered=true,Ntn="123",Email="unfinished@"};
        await setup.SaveDraftAsync(draft,5);
        var resumed=await setup.LoadAsync();Assert.Equal("123",resumed.Profile.Ntn);Assert.Equal("unfinished@",resumed.Profile.Email);Assert.False(resumed.Completed);
        await Assert.ThrowsAsync<BusinessValidationException>(()=>setup.CompleteAsync(resumed.Profile,"owner","LocalPassword123!"));
        Assert.Empty(db.Context.Users);
    }
    [Fact]
    public async Task ExistingStoreNeedsAdminForProfileChangesAndRestartNeverErasesRecords()
    {
        using var db=new TestDb();await SeedSettings(db);var (setup,auth)=Services(db);
        db.Context.Users.Add(new User("existing",new BCryptPasswordHasher().Hash("ExistingPassword123!"),"Owner",UserRole.Admin));
        db.Context.Categories.Add(new Category("Saved category"));await db.Context.SaveChangesAsync();
        await setup.SaveDraftAsync(new(){StoreName="Unauthorized replacement",Language="ur"},5);
        Assert.Equal("Shop",(await setup.LoadAsync()).Profile.StoreName);Assert.Equal("ur",(await setup.LoadAsync()).Profile.Language);
        await Assert.ThrowsAsync<AuthException>(()=>setup.CompleteAsync(new(){StoreName="Replacement"},null,null));
        await auth.LoginAsync("existing","ExistingPassword123!");await setup.CompleteAsync(new(){StoreName="Configured"},null,null);
        await setup.RestartAsync();Assert.Single(db.Context.Categories);Assert.Single(db.Context.Users);Assert.Contains(await setup.AuditAsync(),e=>e.Action=="business.setup.restarted");
    }
    [Theory]
    [InlineData("1234567",false,"")]
    [InlineData("1234567890123",true,"1234567890123")]
    public void RegistrationIdentifiersRemainDistinct(string ntn,bool salesTax,string strn)
    {
        var profile=new BusinessProfile{Registered=true,StoreName="Shop",LegalName="Legal",Address="Address",City="City",Ntn=ntn,SalesTaxRegistered=salesTax,Strn=strn};profile.Validate();
        Assert.Throws<BusinessValidationException>(()=>(profile with{Ntn="123"}).Validate());
        Assert.Throws<BusinessValidationException>(()=>(profile with{SalesTaxRegistered=true,Strn=""}).Validate());
    }
    [Fact]
    public async Task MandatoryFbrCannotBeBypassedBySandboxAndCheckoutPreservesStock()
    {
        using var db=new TestDb();await SeedSettings(db);var (setup,auth)=Services(db);
        await setup.CompleteAsync(new(){StoreName="Shop"},"owner","LocalPassword123!");await auth.LoginAsync("owner","LocalPassword123!");
        await setup.SaveDraftAsync(new(){StoreName="Shop",FbrRequired=true,FbrRoute="digital",FbrStatus="sandbox"},8);
        var cat=new Category("Test");db.Context.Categories.Add(cat);await db.Context.SaveChangesAsync();
        var product=new Product("111","Item",118,.18m,Unit.Piece,2,cat.Id);db.Context.Products.Add(product);await db.Context.SaveChangesAsync();
        var cart=new Cart();cart.AddOrIncrement(product,1);var work=new UnitOfWork(db.Context);
        var sales=new SaleService(new ProductRepository(db.Context),new SaleRepository(db.Context),new ReceiptNumberGenerator(db.Context),work,compliance:new FbrCompliance(db.Context));
        await Assert.ThrowsAsync<BusinessValidationException>(()=>sales.CheckoutAsync(cart,new CashPayment(118,118),auth.CurrentUser!));
        using var fresh=db.NewContext();Assert.Equal(2,fresh.Products.Single().StockQuantity);Assert.Empty(fresh.Sales);
        Assert.False((await new MockFbrAdapter("digital").ValidateAsync(new(){FbrProvider="mock",FbrEnvironment="production"},"{\"lines\":[{}]}")).Valid);
    }
    [Fact]
    public async Task EffectiveTaxRulesSupportExclusiveInputAndPreserveVatArithmetic()
    {
        using var db=new TestDb();await SeedSettings(db);var (setup,auth)=Services(db);
        await setup.CompleteAsync(new(){StoreName="Shop"},"owner","LocalPassword123!");await auth.LoginAsync("owner","LocalPassword123!");
        var cat=new Category("Test");db.Context.Categories.Add(cat);await db.Context.SaveChangesAsync();
        var p=new Product("111","Item",100,0,Unit.Piece,2,cat.Id);db.Context.Products.Add(p);await db.Context.SaveChangesAsync();
        var rule=new ProductTaxRule{ProductId=p.Id,Rate=.18m,EffectiveFrom=DateTime.Today,AuthorityReference="Verified test rule",HsCode="1234.0000"};
        await setup.SaveTaxRuleAsync(rule,priceInput:100,taxExclusive:true);Assert.Equal(118,p.UnitPrice);Assert.Equal(.18m,p.VatRate);
        var cart=new Cart();cart.AddOrIncrement(p,1);var summary=PricingService.Summarize(cart);Assert.Equal(100,summary.NetTotal);Assert.Equal(18,summary.VatTotal);
        await Assert.ThrowsAsync<BusinessValidationException>(()=>setup.SaveTaxRuleAsync(new(){ProductId=p.Id,Rate=.18m,EffectiveFrom=DateTime.Today,AuthorityReference="Overlapping"}));
        await setup.EndTaxRuleAsync(rule.Id,DateTime.Today);await setup.SaveTaxRuleAsync(new(){ProductId=p.Id,Rate=0,Classification="exempt",EffectiveFrom=DateTime.Today.AddDays(1),AuthorityReference="Verified exemption"});
        Assert.Equal(2,(await setup.TaxRulesAsync()).Count);
    }
    private static SignedEnvelope Sign<T>(T data,ECDsa key)
    {
        var payload=JsonSerializer.SerializeToUtf8Bytes(data,SignedDocuments.Json);return new(Convert.ToBase64String(payload),Convert.ToBase64String(key.SignData(payload,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
    }
    [Fact]
    public void PerpetualCertificateWorksAfterMaintenanceExpiryAndRejectsOtherDeviceOrTampering()
    {
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);var publicKey=key.ExportSubjectPublicKeyInfoPem();
        var claims=new LicenseClaims(1,"supermarketpos","license","activation","device","retail",1,DateTimeOffset.UtcNow.AddYears(-1),DateTimeOffset.UtcNow.AddYears(-2));
        var signed=Sign(claims,key);Assert.Equal(claims,SignedDocuments.License(signed,publicKey,"device",1));
        Assert.Throws<CommercialException>(()=>SignedDocuments.License(signed,publicKey,"different",1));
        Assert.Throws<CommercialException>(()=>SignedDocuments.License(signed,publicKey,"device",2));
        Assert.Throws<CommercialException>(()=>SignedDocuments.License(signed with{Payload=Convert.ToBase64String("{}"u8.ToArray())},publicKey,"device",1));
    }
    [Fact]
    public void ReleaseSignaturesEnforceChannelCompatibilityVersionAndUpdateEntitlement()
    {
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);var pub=key.ExportSubjectPublicKeyInfoPem();var now=DateTimeOffset.UtcNow;
        var license=new LicenseClaims(1,"supermarketpos","license","activation","device","retail",1,now.AddDays(1),now.AddDays(-1));
        var release=new ReleaseClaims(1,"supermarketpos","retail","stable","1.0.1","1.0.0",1,1,"bugfix",now,"SupermarketPos-1.0.1-full.nupkg",100,new('a',64),new('b',40));
        Assert.Equal(release,SignedDocuments.Release(Sign(release,key),pub,license,"stable",new(1,0,0),1));
        Assert.Throws<CommercialException>(()=>SignedDocuments.Release(Sign(release,key),pub,license,"testing",new(1,0,0),1));
        Assert.Throws<CommercialException>(()=>SignedDocuments.Release(Sign(release,key),pub,license with{UpdatesUntil=now.AddDays(-1)},"stable",new(1,0,0),1));
        Assert.Throws<CommercialException>(()=>SignedDocuments.Release(Sign(release with{Version="2.0.0"},key),pub,license,"stable",new(1,0,0),1));
        Assert.Throws<CommercialException>(()=>SignedDocuments.Release(Sign(release with{FileName="../evil-full.nupkg"},key),pub,license,"stable",new(1,0,0),1));
    }
    [Fact]
    public void DiagnosticsOmitExceptionMessagesAndFilePaths()
    {
        var folder=Path.Combine(Path.GetTempPath(),"pos-diagnostic-test-"+Guid.NewGuid().ToString("N"));
        try
        {
            var report=new Diagnostics(folder).Capture(new InvalidOperationException("Customer secret / password / invoice"),"test");
            var text=JsonSerializer.Serialize(report);Assert.DoesNotContain("Customer secret",text);Assert.DoesNotContain("password",text);Assert.Equal("InvalidOperationException",report.Category);
        }
        finally{Directory.Delete(folder,true);}
    }
    [Fact]
    public async Task ReadOnlyLicenseGuardPreservesBusinessData()
    {
        using var test=new TestDb();test.Context.WritePermission=()=>false;test.Context.Categories.Add(new Category("Blocked"));
        await Assert.ThrowsAsync<CommercialException>(()=>test.Context.SaveChangesAsync());using var current=test.NewContext();Assert.Empty(current.Categories);
    }
}
