using Microsoft.EntityFrameworkCore;
using SupermarketPos.Application.Services;
using SupermarketPos.Domain.Business;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Infrastructure.Persistence;
namespace SupermarketPos.Tests;

public sealed class RetailWorkflowTests
{
    private sealed record Fixture(RetailInventory Inventory,RetailReferenceCatalog Catalog,DigitalServiceLedger Ledger,RetailCheckout Checkout,AuthService Auth,SaleService Sales,int Provider);
    private static async Task<Fixture> Setup(TestDb db)
    {
        db.Context.Settings.Add(new AppSettings("Retail test","","en",5,[0m]));await db.Context.SaveChangesAsync();
        var hasher=new BCryptPasswordHasher();var auth=new AuthService(new UserRepository(db.Context),hasher);var work=new UnitOfWork(db.Context);var setup=new BusinessSetup(db.Context,work,auth,hasher);
        var profile=new BusinessProfile{StoreName="Retail test",BusinessType="electronics"};profile.ApplyBusinessDefaults();
        await setup.CompleteAsync(profile,"owner","IsolatedRetail123!");await auth.LoginAsync("owner","IsolatedRetail123!");
        var ledger=new DigitalServiceLedger(db.Context,work,auth,setup);var provider=new ServiceProviderAccount{Name="Test account",AllowedTypes="CASHIN,CASHOUT,TRANSFER,LOAD,PACKAGE",OpeningFloat=2000};await ledger.SaveProviderAsync(provider);
        var catalog=new RetailReferenceCatalog(db.Context);var inventory=new RetailInventory(db.Context,work,auth,setup,catalog,ledger);
        var sales=new SaleService(new ProductRepository(db.Context),new SaleRepository(db.Context),new ReceiptNumberGenerator(db.Context),work);
        return new(inventory,catalog,ledger,new(work,sales,ledger,inventory),auth,sales,provider.Id);
    }
    private static async Task<Product> Goods(TestDb db)
    {
        var category=new Category("Test goods");db.Context.Categories.Add(category);await db.Context.SaveChangesAsync();
        var product=new Product("TEST-STOCK","Test physical goods",500,0,Unit.Piece,10,category.Id);db.Context.Products.Add(product);await db.Context.SaveChangesAsync();return product;
    }
    private static RetailServicePayment Service(int provider,string type,decimal amount,string reference="TEST-REF")
    {
        var tx=DigitalServiceLedger.ProviderQuote(new ServiceProviderAccount{Id=provider},type,amount);tx.ConfirmationId=Guid.NewGuid();tx.Status="Confirmed";tx.Reference=reference;tx.OccurredAt=DateTime.Now;tx.CustomerPhone="PK00TESTACCOUNT";return new(tx,true);
    }
    [Fact]public async Task BundledTemplatesPreserveReferenceCoverageWithoutCreatingInventory()
    {
        using var db=new TestDb();var f=await Setup(db);
        Assert.Equal(650,(await f.Catalog.SearchAsync("","model",1000)).Count);
        Assert.Equal(176,(await f.Catalog.SearchAsync("","package",1000)).Count);
        Assert.Equal(138,(await f.Catalog.BrandsAsync()).Count);Assert.Equal(56,(await f.Catalog.CategoriesAsync()).Count);
        var providers=await f.Catalog.SearchAsync("","provider",1000);
        Assert.Equal(5,providers.Count(p=>p.Kind=="network"));Assert.Contains(providers,p=>p.Name.Contains("JazzCash",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(providers,p=>p.Name.Contains("Easypaisa",StringComparison.OrdinalIgnoreCase));
        await f.Inventory.EnsureProviderTemplateAsync();var count=await db.Context.ServiceProviders.CountAsync();await f.Inventory.EnsureProviderTemplateAsync();Assert.Equal(count,await db.Context.ServiceProviders.CountAsync());
        Assert.Empty(db.Context.Products);Assert.Empty(db.Context.ServicePostings);
    }
    [Fact]public async Task QuickLooseGoodsRememberTheirUnitAndPriceWithoutFabricatingStock()
    {
        using var db=new TestDb();var f=await Setup(db);var p=await f.Inventory.QuickAddAsync("Loose cable");p.SetRetailKind("loose");p.SetUnit(Unit.Kilogram);
        await f.Inventory.RememberAsync(p,1200);var saved=await db.Context.Products.AsNoTracking().SingleAsync();
        Assert.Equal(Unit.Kilogram,saved.Unit);Assert.Equal("loose",saved.Kind);Assert.Equal(1200,saved.UnitPrice);Assert.False(saved.StockTracked);Assert.False(saved.QuickEntry);Assert.Equal(0,saved.StockQuantity);
        Assert.Equal(p.Id,(await f.Inventory.QuickAddAsync("loose CABLE")).Id);
    }
    [Fact]public async Task AReferenceCreatesOnlyOneLocalProductAndNeverUsesSourcePriceAsShopPrice()
    {
        using var db=new TestDb();var f=await Setup(db);var reference=(await f.Catalog.SearchAsync("iPhone 15")).First();
        var p=await f.Inventory.QuickAddAsync(reference.Name,"labeled",reference);var again=await f.Inventory.QuickAddAsync(reference.Name,"labeled",reference);
        Assert.Equal(p.Id,again.Id);Assert.Equal(0,p.UnitPrice);Assert.False(p.StockTracked);Assert.True(p.QuickEntry);Assert.Equal(reference.Brand,p.Brand);Assert.Single(db.Context.Products);
        Assert.Equal(55,await f.Inventory.LoadCategoryTemplateAsync());Assert.Equal(56,await db.Context.Categories.CountAsync());Assert.Equal(0,await f.Inventory.LoadCategoryTemplateAsync());
    }
    [Fact]public async Task MixedCashOutAndGoodsSettleNetCashAndKeepPrincipalOutOfSalesRevenue()
    {
        using var db=new TestDb();var f=await Setup(db);var goods=await Goods(db);var service=await f.Inventory.QuickAddAsync("Cash withdrawal","digital",price:1000);service.ConfigureService("CASHOUT",f.Provider);
        var cart=new Cart();cart.AddOrIncrement(goods,1);cart.AddOrIncrement(service,1);var tx=Service(f.Provider,"CASHOUT",1000);tx.Transaction.CustomerFee=20;
        var key=Guid.NewGuid();var result=await f.Checkout.CheckoutAsync(cart,null,0,f.Auth.CurrentUser!,null,key,[tx]);
        Assert.Equal(500,result.Sale!.GrossTotal);Assert.Single(result.Sale.Lines);Assert.Equal(20,db.Context.ServicePostings.Single().Income);
        Assert.Equal(-980,db.Context.ServicePostings.Single().CashChange);Assert.Equal(9,(await db.Context.Products.FindAsync(goods.Id))!.StockQuantity);
        await f.Checkout.CheckoutAsync(cart,null,0,f.Auth.CurrentUser!,null,key,[tx]);Assert.Single(db.Context.Sales);Assert.Single(db.Context.ServicePostings);Assert.Equal(9,(await db.Context.Products.FindAsync(goods.Id))!.StockQuantity);
    }
    [Fact]public async Task FailedServiceRollsBackTheGoodsSaleAndStock()
    {
        using var db=new TestDb();var f=await Setup(db);var goods=await Goods(db);var service=await f.Inventory.QuickAddAsync("Mobile load","digital",price:3000);service.ConfigureService("LOAD",f.Provider);
        var cart=new Cart();cart.AddOrIncrement(goods,1);cart.AddOrIncrement(service,1);var tx=Service(f.Provider,"LOAD",3000);tx.Transaction.CustomerPhone="03001234567";
        await Assert.ThrowsAsync<DomainException>(()=>f.Checkout.CheckoutAsync(cart,new CashPayment(3500,3500),3500,f.Auth.CurrentUser!,null,Guid.NewGuid(),[tx]));
        Assert.Empty(db.Context.Sales);Assert.Empty(db.Context.ServicePostings);Assert.Empty(db.Context.ServiceTransactions);
        Assert.Equal(10,(await db.Context.Products.AsNoTracking().SingleAsync(p=>p.Id==goods.Id)).StockQuantity);
    }
    [Theory][InlineData("Pending")][InlineData("Failed")]
    public async Task UnconfirmedServiceRowsNeverCollectCashOrPostIncome(string status)
    {
        using var db=new TestDb();var f=await Setup(db);var p=await f.Inventory.QuickAddAsync("Mobile load","digital",price:100);p.ConfigureService("LOAD",f.Provider);var cart=new Cart();cart.AddOrIncrement(p,1);
        var entry=Service(f.Provider,"LOAD",100);entry.Transaction.CustomerPhone="03001234567";entry.Transaction.Status=status;
        await f.Checkout.CheckoutAsync(cart,new CashPayment(0,0),0,f.Auth.CurrentUser!,null,Guid.NewGuid(),[entry with{ConfirmedExternally=false}]);
        Assert.Empty(db.Context.Sales);Assert.Empty(db.Context.ServicePostings);Assert.Single(db.Context.ServiceTransactions);
    }
    [Fact]public async Task TransferPrincipalIsNotIncomeAndCannotBypassServiceCheckout()
    {
        using var db=new TestDb();var f=await Setup(db);var p=await f.Inventory.QuickAddAsync("Bank transfer","digital",price:1000);p.ConfigureService("TRANSFER",f.Provider);var cart=new Cart();cart.AddOrIncrement(p,1);
        await Assert.ThrowsAsync<DomainException>(()=>f.Sales.CheckoutAsync(cart,new CashPayment(1000,1000),f.Auth.CurrentUser!));
        var entry=Service(f.Provider,"TRANSFER",1000);entry.Transaction.CustomerFee=15;
        await f.Checkout.CheckoutAsync(cart,new CashPayment(1015,1015),1015,f.Auth.CurrentUser!,null,Guid.NewGuid(),[entry]);
        Assert.Empty(db.Context.Sales);Assert.Equal(15,db.Context.ServicePostings.Single().Income);Assert.Equal(1015,db.Context.ServicePostings.Single().CashChange);
    }
    [Fact]public async Task InvalidServiceConfigurationLeavesNoHalfCreatedProduct()
    {
        using var db=new TestDb();var f=await Setup(db);
        await Assert.ThrowsAsync<DomainException>(()=>f.Inventory.SaveServiceAsync(0,"Incomplete transfer",100,"TRANSFER",null));
        Assert.Empty(db.Context.Products);
        await f.Inventory.SaveServiceAsync(0,"Transfer",100,"TRANSFER",f.Provider);
        var product=await db.Context.Products.SingleAsync();Assert.True(product.IsFinancialService);Assert.Equal(f.Provider,product.ServiceProviderId);Assert.Equal(100,product.UnitPrice);
    }
    [Fact]public async Task DigitalPackagesDoNotCreatePhysicalProducts()
    {
        using var db=new TestDb();var f=await Setup(db);var reference=(await f.Catalog.SearchAsync("Hajj","package")).First(p=>p.Category!="directory");
        var package=await f.Inventory.PrepareReferencePackageAsync(reference,200,"Prepaid");
        var account=await db.Context.ServiceProviders.FindAsync(package.ProviderId);account!.OpeningFloat=2000;await db.Context.SaveChangesAsync();
        Assert.False(package.ProviderCostKnown);Assert.Empty(db.Context.Products);Assert.Empty(db.Context.ServicePostings);
        var tx=await f.Ledger.SellPackageAsync(package.Id,250,null,"03001234567","COST-UNKNOWN","Confirmed",true,Guid.NewGuid());
        Assert.False(tx.ProviderCostKnown);Assert.Equal(250,tx.CashChange);Assert.Equal(0,tx.Income);Assert.Equal(1,(await f.Ledger.ReportAsync(DateTime.Today,DateTime.Today.AddDays(1))).UnknownCosts);
        var known=await f.Ledger.SellPackageAsync(package.Id,270,180,"03001234567","COST-KNOWN","Confirmed",true,Guid.NewGuid());Assert.Equal(90,known.Income);Assert.Equal(200, (await db.Context.MobileServicePackages.FindAsync(package.Id))!.Price);Assert.Empty(db.Context.Products);
    }
    [Fact]public async Task LegacyDigitalItemsCannotEnterPhysicalSalesOrPurchases()
    {
        using var db=new TestDb();var f=await Setup(db);var product=await f.Inventory.QuickAddAsync("Legacy package","digital",price:100);
        var cart=new Cart();cart.AddOrIncrement(product,1);
        await Assert.ThrowsAsync<DomainException>(()=>f.Sales.CheckoutAsync(cart,new CashPayment(100,100),f.Auth.CurrentUser!));
        var purchases=new PurchaseService(new ProductRepository(db.Context),new PurchaseRepository(db.Context),new UnitOfWork(db.Context));
        await Assert.ThrowsAsync<DomainException>(()=>purchases.ReceiveAsync("Supplier",null,[new(product.Id,1,50)],f.Auth.CurrentUser!));
        Assert.Empty(db.Context.Sales);Assert.Empty(db.Context.Purchases);
    }
    [Fact]public async Task CashierCanSellBundledPackagesWithoutConfigurationPermission()
    {
        using var db=new TestDb();var f=await Setup(db);await f.Inventory.EnsureProviderTemplateAsync();
        db.Context.Users.Add(new User("cashier",new BCryptPasswordHasher().Hash("CashierTest123!"),"Cashier",UserRole.Cashier));await db.Context.SaveChangesAsync();f.Auth.Logout();await f.Auth.LoginAsync("cashier","CashierTest123!");
        var reference=(await f.Catalog.SearchAsync("Hajj","package")).First(p=>p.Category!="directory");
        var package=await f.Inventory.PrepareReferencePackageAsync(reference,250,"Prepaid");
        var saved=await f.Ledger.SellPackageAsync(package.Id,250,null,"03001234567","","Pending",false,Guid.NewGuid());
        Assert.Equal("Pending",saved.Status);Assert.Empty(db.Context.Products);Assert.Empty(db.Context.ServicePostings);
        await Assert.ThrowsAsync<AuthException>(()=>f.Ledger.SavePackageAsync(new(){ProviderId=package.ProviderId,Name="Unauthorized edit",Price=1}));
    }
}
