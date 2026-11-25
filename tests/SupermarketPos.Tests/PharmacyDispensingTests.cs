using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SupermarketPos.Application.Services;
using SupermarketPos.Domain.Business;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Infrastructure.Persistence;

namespace SupermarketPos.Tests;
public sealed class PharmacyDispensingTests
{
    private static (SaleService Service,User User) Setup(TestDb db)
    {
        db.Context.Categories.Add(new Category("Medicines"));db.Context.SaveChanges();
        return(new(new ProductRepository(db.Context),new SaleRepository(db.Context),new ReceiptNumberGenerator(db.Context),new UnitOfWork(db.Context),compliance:new FbrCompliance(db.Context)),new User("owner","hash","Owner",UserRole.Admin));
    }
    private static Product Medicine(TestDb db,bool tracked=true)
    {
        var p=new Product("","Test medicine",10,0,Unit.Piece,tracked?30:0,1);p.ConfigurePackaging("pack",30,null,null,250,null);p.ConfigureMedicineUnits("tablet",10,90);p.ReviewDispensingRules(false,false);if(!tracked)p.MarkStockUntracked();db.Context.Products.Add(p);db.Context.SaveChanges();return p;
    }
    [Fact]public async Task TabletAndStripSalesUseExactBaseStockAndIndependentPrices()
    {
        using var db=new TestDb();var (service,user)=Setup(db);var p=Medicine(db);
        var cart=new Cart();cart.AddOrIncrement(p,2,medicineUnit:"tablet");var first=await service.CheckoutAsync(cart,new CashPayment(20,20),user);Assert.Equal(28,p.StockQuantity);
        cart.Clear();cart.AddOrIncrement(p,10,medicineUnit:"strip");var second=await service.CheckoutAsync(cart,new CashPayment(90,90),user);Assert.Equal(18,p.StockQuantity);Assert.Equal(1,second.Lines.Single().Quantity);Assert.Equal(10,second.Lines.Single().BaseQuantity);Assert.Equal("strip",second.Lines.Single().UnitCode);Assert.Equal(-10,db.Context.StockMovements.OrderBy(m=>m.Id).Last().QuantityChange);
        p.SetSellingUnitPrice("strip",95);Assert.Equal(90,second.Lines.Single().UnitPrice);Assert.Equal(10,first.Lines.Single().UnitPrice);
    }
    [Fact]public async Task UntrackedSaleHasNoFictionalStockCostProfitOrStockMovementAndCreatesReconciliation()
    {
        using var db=new TestDb();var (service,user)=Setup(db);var p=Medicine(db,false);p.SetPurchaseCost(5);await db.Context.SaveChangesAsync();
        using(var verify=db.NewContext())Assert.False((await verify.Products.SingleAsync()).StockTracked);
        var cart=new Cart();cart.AddOrIncrement(p,2,medicineUnit:"tablet");var sale=await service.CheckoutAsync(cart,new CashPayment(20,20),user);
        Assert.Equal(0,p.StockQuantity);Assert.Null(sale.Lines.Single().UnitCost);Assert.Null(sale.GrossProfit);Assert.True(sale.Lines.Single().StockUntracked);Assert.Empty(db.Context.StockMovements);Assert.Single(db.Context.StockReconciliations);Assert.Throws<DomainException>(()=>p.Restock(10));
    }
    [Fact]public async Task TrackedShortageCannotBecomeUntracked()
    {
        using var db=new TestDb();var (service,user)=Setup(db);var p=Medicine(db);Assert.Throws<DomainException>(()=>p.MarkStockUntracked());var cart=new Cart();cart.AddOrIncrement(p,31,medicineUnit:"tablet");await Assert.ThrowsAsync<InsufficientStockException>(()=>service.CheckoutAsync(cart,new CashPayment(310,310),user));Assert.Equal(30,(await db.Context.Products.SingleAsync()).StockQuantity);Assert.Empty(db.Context.Sales);Assert.Empty(db.Context.StockReconciliations);
    }
    [Fact]public async Task RequiredDispensingChecksCannotBeBypassedByProvisionalSales()
    {
        using var db=new TestDb();var (service,user)=Setup(db);var p=Medicine(db,false);p.ReviewDispensingRules(true,true);await db.Context.SaveChangesAsync();var cart=new Cart();cart.AddOrIncrement(p,1,medicineUnit:"tablet");await Assert.ThrowsAsync<DomainException>(()=>service.CheckoutAsync(cart,new CashPayment(10,10),user));Assert.Empty(db.Context.Sales);
        p=await db.Context.Products.SingleAsync();cart.Clear();cart.AddOrIncrement(p,1,medicineUnit:"tablet",prescriptionChecked:true,dispensingReference:"RX-verified-1");var sale=await service.CheckoutAsync(cart,new CashPayment(10,10),user);Assert.True(sale.Lines.Single().PrescriptionChecked);Assert.Equal("RX-verified-1",sale.Lines.Single().DispensingReference);
    }
    [Fact]public void UnitPricesMustBeExplicitAndBatchesOnHoldCannotBeSold()
    {
        using var db=new TestDb();Setup(db);var p=Medicine(db);p.ConfigureMedicineUnits("tablet",10);Assert.Equal(0,new CartLine(p,10,medicineUnit:"strip").GrossUnitPrice);p.SetExpiryTracking(true);var b=p.RecordBatch("LOT",DateOnly.FromDateTime(DateTime.Today.AddMonths(2)),2,null,DateTime.Now);db.Context.SaveChanges();b.SetHold("Recall investigation");Assert.Throws<DomainException>(()=>p.SellBatch(b.Id,b.ExpiryDate,1,DateTime.Now));Assert.Equal(2,b.Quantity);
    }
    [Fact]public async Task OrdinaryMedicineNeedsNoUniversalChecklistButMissingUnitPriceStillBlocksPosting()
    {
        using var db=new TestDb();var (service,user)=Setup(db);var p=new Product("DIRECT","Ordinary medicine",12,0,Unit.Piece,30,1);p.ConfigureMedicineUnits("tablet",10);db.Context.Products.Add(p);await db.Context.SaveChangesAsync();var cart=new Cart();cart.AddOrIncrement(p,1,medicineUnit:"tablet");await service.CheckoutAsync(cart,new CashPayment(12,12),user);Assert.False(p.DispensingRulesReviewed);Assert.Single(db.Context.Sales);
        cart.Clear();cart.AddOrIncrement(p,10,medicineUnit:"strip");await Assert.ThrowsAsync<DomainException>(()=>service.CheckoutAsync(cart,new CashPayment(0,0),user));Assert.Single(db.Context.Sales);Assert.Equal(29,(await db.Context.Products.SingleAsync()).StockQuantity);
    }
    [Fact]public void OptionalBarcodesGetUniqueInternalIdsAndLocationNamespaceIsReserved()
    {
        var a=new Product("","A",10,0,Unit.Piece,0,1);var b=new Product("","B",10,0,Unit.Piece,0,1);Assert.NotEqual(a.Barcode,b.Barcode);Assert.StartsWith("INTERNAL-",a.Barcode);Assert.Throws<DomainException>(()=>a.SetBarcode("POSLOC:rack"));
    }
}
