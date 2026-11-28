using Microsoft.EntityFrameworkCore;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Application.Services;
using SupermarketPos.Domain.Business;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Infrastructure.Persistence;

namespace SupermarketPos.Tests;

public sealed class RetailCompletionTests
{
    private static async Task<(AuthService Auth,ReturnsService Returns,PurchaseService Purchases,SaleService Sales,Product Product,TradeLedger Trade)> Fixture(TestDb test,bool serial=false)
    {
        var db=test.Context;db.Settings.Add(new("Shop","","en",5,[0m]));db.Categories.Add(new("Stock"));await db.SaveChangesAsync();
        var hasher=new BCryptPasswordHasher();var auth=new AuthService(new UserRepository(db),hasher);var work=new UnitOfWork(db);var setup=new BusinessSetup(db,work,auth,hasher);
        var profile=new BusinessProfile{StoreName="Shop",BusinessType="electronics"};profile.ApplyBusinessDefaults();await setup.CompleteAsync(profile,"owner","TestPassword123");await auth.LoginAsync("owner","TestPassword123");
        var product=new Product("RETAIL-COMPLETE","Stock item",100,0,Unit.Piece,serial?0:20,1);product.SetSerialTracking(serial,12);db.Products.Add(product);await db.SaveChangesAsync();
        var ledger=new TradeLedger(db,work);
        return(auth,new(db,work,auth),new(new ProductRepository(db),new PurchaseRepository(db),work,ledger),new(new ProductRepository(db),new SaleRepository(db),new ReceiptNumberGenerator(db),work,ledger:ledger),product,ledger);
    }
    private static Cart One(Product product,string? serial=null){var cart=new Cart();cart.AddOrIncrement(product,1,serial:serial);return cart;}

    [Fact]
    public async Task ReturnedCreditCanPayAnotherInvoiceOrBeRefundedWithoutLosingItsAudit()
    {
        using var test=new TestDb();var f=await Fixture(test);var customer=new SaleCustomer("Customer","03001234567",null,null);
        var original=await f.Sales.CheckoutAsync(One(f.Product),new CreditPayment(100,10),f.Auth.CurrentUser!,customer:customer);
        var refund=await f.Returns.RecordAsync(original.Id,[new(original.Lines.Single().Id,1,false)],"Return",false,Guid.NewGuid());
        Assert.Equal(10,refund.CustomerCredit);Assert.Equal(0,original.Outstanding);
        var next=await f.Sales.CheckoutAsync(One(f.Product),new CreditPayment(100,0),f.Auth.CurrentUser!,customer:customer);
        var key=Guid.NewGuid();await f.Returns.UseCreditAsync(refund.Id,next.Id,6,"APPLY","",key);
        await f.Returns.UseCreditAsync(refund.Id,next.Id,6,"APPLY","",key);
        Assert.Equal(94,next.Outstanding);Assert.Equal(4,(await f.Returns.AvailableCreditsAsync([original.Id,next.Id])).Single().Available);
        await Assert.ThrowsAsync<DomainException>(()=>f.Returns.UseCreditAsync(refund.Id,null,4,"CASH","",Guid.NewGuid()));
        await f.Returns.UseCreditAsync(refund.Id,null,4,"CASH","Paid back",Guid.NewGuid(),true);
        Assert.Empty(await f.Returns.AvailableCreditsAsync([original.Id,next.Id]));Assert.Equal(100,original.GrossTotal);
        Assert.Equal(2,(await f.Returns.CreditMovementsAsync([original.Id])).Count);
        Assert.Equal(4,(await f.Returns.CreditRefundsBetweenAsync(DateTime.Today,DateTime.Today.AddDays(1))).Single().Amount);
        var report=await new ReportService(new SaleRepository(test.Context)).GetXReportAsync(DateTime.Today);
        Assert.Equal(4,report.CreditCashRefunds);Assert.Equal(0,report.CreditCardRefunds);Assert.Equal(200,report.GrossTotal);
        await Assert.ThrowsAsync<DomainException>(()=>f.Returns.UseCreditAsync(refund.Id,null,1,"CASH","",Guid.NewGuid(),true));
    }

    [Fact]
    public async Task CreditCannotPayAnotherCustomerOrReuseConfirmationWithChangedAmount()
    {
        using var test=new TestDb();var f=await Fixture(test);var customer=new SaleCustomer("Customer","03001234567",null,null);
        var sale=await f.Sales.CheckoutAsync(One(f.Product),new CreditPayment(100,99),f.Auth.CurrentUser!,customer:customer);
        var credit=await f.Returns.RecordAsync(sale.Id,[new(sale.Lines.Single().Id,1,false)],"Return",false,Guid.NewGuid());
        var other=await f.Sales.CheckoutAsync(One(f.Product),new CreditPayment(100,0),f.Auth.CurrentUser!,customer:new("Other","03007654321",null,null));
        await Assert.ThrowsAsync<DomainException>(()=>f.Returns.UseCreditAsync(credit.Id,other.Id,5,"APPLY","",Guid.NewGuid()));
        var key=Guid.NewGuid();await f.Returns.UseCreditAsync(credit.Id,null,10,"CASH","",key,true);
        await Assert.ThrowsAsync<DomainException>(()=>f.Returns.UseCreditAsync(credit.Id,null,11,"CASH","",key,true));
        Assert.Single(test.Context.CustomerCreditMovements);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public async Task SerialCostFollowsItsOwnReceiptAndInspectedReturnCanSellAgain(bool located)
    {
        using var test=new TestDb();var f=await Fixture(test,true);var supplier=await f.Purchases.AddSupplierAsync("Supplier");
        await f.Purchases.ReceiveAsync(supplier.Id,"A",DateTime.Today,null,[new(f.Product.Id,1,30,Units:[UnitIdentifiers.Create("A")])],f.Auth.CurrentUser!,Guid.NewGuid());
        await f.Purchases.ReceiveAsync(supplier.Id,"B",DateTime.Today,null,[new(f.Product.Id,1,35,Units:[UnitIdentifiers.Create("B")])],f.Auth.CurrentUser!,Guid.NewGuid());
        var service=f.Sales;var firstCart=One(f.Product,"A");
        if(located)
        {
            var rack=new StockLocation("Device shelf");test.Context.StockLocations.Add(rack);await test.Context.SaveChangesAsync();
            f.Product.SetDefaultLocation(rack.Id);var place=new StockPlacement(f.Product.Id,null,rack.Id);place.Adjust(2);test.Context.StockPlacements.Add(place);await test.Context.SaveChangesAsync();
            var work=new UnitOfWork(test.Context);service=new(new ProductRepository(test.Context),new SaleRepository(test.Context),new ReceiptNumberGenerator(test.Context),work,ledger:f.Trade,locations:new StockLocationService(test.Context,work,f.Auth));
            firstCart.Clear();firstCart.AddOrIncrement(f.Product,1,serial:"A",locationId:rack.Id);
        }
        var first=await service.CheckoutAsync(firstCart,new CashPayment(100,100),f.Auth.CurrentUser!);
        Assert.Equal(30,first.Lines.Single().UnitCost);Assert.Equal(70,first.GrossProfit);
        var refund=await f.Returns.RecordAsync(first.Id,[new(first.Lines.Single().Id,1,false,"A")],"Returned",true,Guid.NewGuid());
        await Assert.ThrowsAsync<DomainException>(()=>f.Sales.CheckoutAsync(One(f.Product,"A"),new CashPayment(100,100),f.Auth.CurrentUser!));
        var key=Guid.NewGuid();await f.Returns.ReleaseDeviceAsync(refund.Id,first.Lines.Single().Id,"A","Inspected and approved",key);
        await f.Returns.ReleaseDeviceAsync(refund.Id,first.Lines.Single().Id,"A","Inspected and approved",key);
        var resell=One(f.Product,"A");if(located){resell.Clear();resell.AddOrIncrement(f.Product,1,serial:"A",locationId:0);Assert.Equal(1,test.Context.StockPlacements.Single().BaseQuantity);}
        var second=await service.CheckoutAsync(resell,new CashPayment(100,100),f.Auth.CurrentUser!);
        Assert.Equal(30,second.Lines.Single().UnitCost);Assert.Single(await f.Returns.DeviceReleasesAsync(first.Id));
        Assert.Equal(first.Id,refund.SaleId);Assert.Equal(second.Id,test.Context.SerializedUnits.Single(u=>u.Serial=="A").SaleId);
        Assert.Equal(1,test.Context.Products.Single().StockQuantity);
    }

    [Fact]
    public async Task PurchaseRetriesRejectChangedCostsQuantityReferenceAndPayment()
    {
        using var test=new TestDb();var f=await Fixture(test);var supplier=await f.Purchases.AddSupplierAsync("Supplier");var key=Guid.NewGuid();
        PurchaseItem[] items=[new(f.Product.Id,2,25)];var first=await f.Purchases.ReceiveAsync(supplier.Id,"A",DateTime.Today,"Notes",items,f.Auth.CurrentUser!,key,initialAmountPaid:10);
        Assert.Equal(first.Id,(await f.Purchases.ReceiveAsync(supplier.Id,"A",DateTime.Today,"Notes",items,f.Auth.CurrentUser!,key,initialAmountPaid:10)).Id);
        await Assert.ThrowsAsync<DomainException>(()=>f.Purchases.ReceiveAsync(supplier.Id,"A",DateTime.Today,"Notes",[new(f.Product.Id,2,26)],f.Auth.CurrentUser!,key,initialAmountPaid:10));
        await Assert.ThrowsAsync<DomainException>(()=>f.Purchases.ReceiveAsync(supplier.Id,"A",DateTime.Today,"Notes",[new(f.Product.Id,3,25)],f.Auth.CurrentUser!,key,initialAmountPaid:10));
        await Assert.ThrowsAsync<DomainException>(()=>f.Purchases.ReceiveAsync(supplier.Id,"B",DateTime.Today,"Notes",items,f.Auth.CurrentUser!,key,initialAmountPaid:10));
        await Assert.ThrowsAsync<DomainException>(()=>f.Purchases.ReceiveAsync(supplier.Id,"A",DateTime.Today,"Notes",items,f.Auth.CurrentUser!,key,initialAmountPaid:11));
        Assert.Single(test.Context.Purchases);Assert.Equal(22,test.Context.Products.Single().StockQuantity);
    }

    [Fact]
    public async Task BatchCostDoesNotUseAnotherLotsLatestPrice()
    {
        using var test=new TestDb();var f=await Fixture(test);f.Product.SetExpiryTracking(true);await test.Context.SaveChangesAsync();var supplier=await f.Purchases.AddSupplierAsync("Supplier");
        var expiry=DateOnly.FromDateTime(DateTime.Today.AddYears(1));
        await f.Purchases.ReceiveAsync(supplier.Id,"A",DateTime.Today,null,[new(f.Product.Id,2,20,"LOT-A",expiry)],f.Auth.CurrentUser!,Guid.NewGuid());
        await f.Purchases.ReceiveAsync(supplier.Id,"B",DateTime.Today,null,[new(f.Product.Id,2,40,"LOT-B",expiry)],f.Auth.CurrentUser!,Guid.NewGuid());
        var batch=f.Product.Batches.Single(b=>b.Lot=="LOT-A");var cart=new Cart();cart.AddOrIncrement(f.Product,1,batch);
        var sale=await f.Sales.CheckoutAsync(cart,new CashPayment(100,100),f.Auth.CurrentUser!);Assert.Equal(20,sale.Lines.Single().UnitCost);
    }

    [Fact]
    public async Task AnUnrecordedBatchCostIsNotInventedFromTheCurrentProductCost()
    {
        using var test=new TestDb();var f=await Fixture(test);f.Product.SetExpiryTracking(true);f.Product.SetConvertedPurchaseCost(70);
        var batch=f.Product.RecordBatch("OLD",DateOnly.FromDateTime(DateTime.Today.AddYears(1)),1,null,DateTime.Now);
        await test.Context.SaveChangesAsync();var cart=new Cart();cart.AddOrIncrement(f.Product,1,batch);
        var sale=await f.Sales.CheckoutAsync(cart,new CashPayment(100,100),f.Auth.CurrentUser!);
        Assert.Null(sale.Lines.Single().UnitCost);
    }
}
