using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SupermarketPos.Application.Services;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Infrastructure.Persistence;

namespace SupermarketPos.Tests;
public sealed class MeasuredRetailTests
{
    [Theory] [InlineData(160,100,625,100)] [InlineData(160,99,618,98.88)] [InlineData(333,100,300,99.90)]
    public void AmountUsesWholeGramsAndNeverExceedsBudget(decimal price,decimal amount,long grams,decimal charge)
    {
        var result=MeasuredGoods.ForAmount(amount,price,Unit.Kilogram);Assert.Equal(grams,result.BaseQuantity);Assert.Equal(charge,result.Charge);Assert.True(result.Charge<=amount);
    }
    [Fact] public async Task WeighedCheckoutDeductsOnlyAfterCommitAndPreservesExactIntegerStock()
    {
        using var db=new TestDb();var cat=new Category("Loose");db.Context.Categories.Add(cat);await db.Context.SaveChangesAsync();
        var p=new Product("","Sugar",160,0,Unit.Kilogram,50,cat.Id);db.Context.Products.Add(p);await db.Context.SaveChangesAsync();
        var cart=new Cart();cart.AddOrIncrement(p,.5m);Assert.Equal(50000,p.BaseStockQuantity);Assert.Equal(80,PricingService.GrossTotal(cart));
        var sale=await Sales(db).CheckoutAsync(cart,new CashPayment(80,80),new User("cashier","hash","Cashier",UserRole.Admin));
        using var fresh=db.NewContext();var saved=fresh.Products.Single();Assert.Equal(49500,saved.BaseStockQuantity);Assert.Equal(49.5m,saved.StockQuantity);Assert.Equal("kg",sale.Lines.Single().UnitCode);
    }
    [Fact] public async Task BoxesReceiveAndSellWithoutDuplicateStockOrRoundingLoss()
    {
        using var db=new TestDb();var cat=new Category("Candy");db.Context.Categories.Add(cat);await db.Context.SaveChangesAsync();
        var p=new Product("PIECE","Candy",10,0,Unit.Piece,0,cat.Id);p.ConfigurePackaging("box",100,"BOX",600,850,null);db.Context.Products.Add(p);await db.Context.SaveChangesAsync();
        var purchases=new PurchaseService(new ProductRepository(db.Context),new PurchaseRepository(db.Context),new UnitOfWork(db.Context));var user=new User("owner","hash","Owner",UserRole.Admin);
        var purchase=await purchases.ReceiveAsync("Supplier",null,[new(p.Id,5,600,Packaged:true)],user);Assert.Equal(3000,purchase.Total);Assert.Equal(500,p.BaseStockQuantity);Assert.Equal(6,p.PurchaseCost);
        var cart=new Cart();cart.AddOrIncrement(p,100,packaged:true);Assert.Equal(850,PricingService.GrossTotal(cart));
        var sale=await Sales(db).CheckoutAsync(cart,new CashPayment(850,850),user);Assert.Equal(400,p.BaseStockQuantity);Assert.Equal(1,sale.Lines.Single().Quantity);Assert.Equal("box",sale.Lines.Single().UnitCode);Assert.Equal(100,sale.Lines.Single().BaseQuantity);
        var pieces=new Cart();pieces.AddOrIncrement(p,2);await Sales(db).CheckoutAsync(pieces,new CashPayment(20,20),user);Assert.Equal(398,p.BaseStockQuantity);
        var match=await new RegisterBarcodeLookup(new ProductRepository(db.Context),new BarcodeService()).FindAsync("BOX");Assert.True(match!.Packaged);Assert.Equal(100,match.Quantity);
    }
    [Fact]public void PrecisionAndUnavailableStockAreRejected()
    {
        Assert.Throws<DomainException>(()=>new Product("","Liquid",100,0,Unit.Litre,.0001m,1));
        var p=new Product("x","Liquid",100,0,Unit.Litre,1,1);Assert.Throws<InsufficientStockException>(()=>p.DecreaseStock(2));Assert.Equal(1000,p.BaseStockQuantity);
        var c=new Cart();Assert.Throws<DomainException>(()=>c.AddOrIncrement(p,.0001m));
    }
    [Fact]public void WeighedExpiryBatchesAlsoKeepIntegerGramBalances()
    {
        var p=new Product("","Loose medicine",100,0,Unit.Kilogram,0,1);p.SetExpiryTracking(true);
        var batch=p.RecordBatch("LOT1",DateOnly.FromDateTime(DateTime.Today.AddYears(1)),1.25m,null,DateTime.Now);
        Assert.Equal(1250,batch.BaseQuantity);Assert.Equal(1250,p.BaseStockQuantity);
        p.SellBatch(batch.Id,batch.ExpiryDate,.625m,DateTime.Now);Assert.Equal(625,batch.BaseQuantity);Assert.Equal(.625m,batch.Quantity);Assert.Equal(625,p.BaseStockQuantity);
    }
    [Fact]public async Task ExistingDecimalStockMigratesExactlyAndNewInsertsStillWork()
    {
        using var connection=new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        await using var db=new PosDbContext(new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261119140000_BusinessOnboarding");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Categories (Name,ColorHex) VALUES ('Test','#123456'); INSERT INTO Products (Barcode,Name,UnitPrice,VatRate,Unit,StockQuantity,CategoryId,IsActive,TrackExpiry,StockRevision,TrackSerial,WarrantyMonths) VALUES ('old','Old sugar','160','0',1,'12.345',1,1,0,0,0,0)");
        await db.Database.MigrateAsync();Assert.Equal(12345,db.Products.Single(p=>p.Barcode=="old").BaseStockQuantity);var before=await db.Products.CountAsync();
        db.Products.Add(new Product("new","New",10,0,Unit.Piece,2,1));await db.SaveChangesAsync();Assert.Equal(before+1,await db.Products.CountAsync());
    }
    private static SaleService Sales(TestDb db)=>new(new ProductRepository(db.Context),new SaleRepository(db.Context),new ReceiptNumberGenerator(db.Context),new UnitOfWork(db.Context));
}
