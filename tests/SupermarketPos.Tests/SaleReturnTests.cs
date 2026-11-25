using Microsoft.EntityFrameworkCore;
using SupermarketPos.Application.Services;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Infrastructure.Persistence;

namespace SupermarketPos.Tests;

public sealed class SaleReturnTests
{
    private static async Task<(ReturnsService Returns, SaleService Sale, Product Product, User Owner)> Prepare(TestDb fixture)
    {
        var db=fixture.Context;
        var hasher=new BCryptPasswordHasher();
        var owner=new User("owner",hasher.Hash("test-owner-123"),"Owner",UserRole.Admin);
        db.Users.Add(owner);db.Categories.Add(new("General"));
        await db.SaveChangesAsync();
        var product=new Product("RETURN-E2E","Product",10,0,Unit.Piece,30,1);
        db.Products.Add(product);await db.SaveChangesAsync();
        var auth=new AuthService(new UserRepository(db),hasher);
        await auth.LoginAsync("owner","test-owner-123");
        var work=new UnitOfWork(db);
        return(new(db,work,auth),new(new ProductRepository(db),new SaleRepository(db),new ReceiptNumberGenerator(db),work),product,owner);
    }

    [Fact]
    public async Task CashRefundAndRestockPostOnceAndKeepOriginalInvoice()
    {
        using var db=new TestDb();
        var (returns,sales,product,owner)=await Prepare(db);
        var cart=new Cart();cart.AddOrIncrement(product,3);
        var sale=await sales.CheckoutAsync(cart,new CashPayment(30,30),owner);
        var confirmation=Guid.NewGuid();ReturnSelection[] items=[new(sale.Lines.Single().Id,2,true)];
        var refund=await returns.RecordAsync(sale.Id,items,"Verified resale",true,confirmation);
        Assert.Equal(refund.Id,(await returns.RecordAsync(sale.Id,items,"Verified resale",true,confirmation)).Id);
        Assert.Equal(20,refund.CashRefund);Assert.Equal(29,product.StockQuantity);Assert.Equal(30,sale.GrossTotal);
        Assert.Single(await db.Context.StockMovements.Where(m=>m.Kind==StockMovementKind.SaleReturn).ToListAsync());
        await Assert.ThrowsAsync<DomainException>(()=>returns.RecordAsync(sale.Id,items,"Excess return",true,Guid.NewGuid()));
        Assert.Single(await db.Context.SaleReturns.ToListAsync());
    }

    [Fact]
    public async Task CardRefundOnlyPostsAfterExternalConfirmation()
    {
        using var db=new TestDb();
        var (returns,sales,product,owner)=await Prepare(db);
        var cart=new Cart();cart.AddOrIncrement(product,2);
        var sale=await sales.CheckoutAsync(cart,new CardPayment(20),owner);
        var refund=await returns.RecordAsync(sale.Id,[new(sale.Lines.Single().Id,1,false)],"Damaged",false,Guid.NewGuid());
        Assert.Equal("Pending",refund.Status);Assert.Equal(0,refund.CardRefund);
        await returns.ResolveCardAsync(refund.Id,true,"External receipt");
        await returns.ResolveCardAsync(refund.Id,true,"External receipt");
        Assert.Equal(10,refund.CardRefund);Assert.Equal("Posted",refund.Status);Assert.Equal(28,product.StockQuantity);
    }

    [Fact]
    public async Task CreditReturnFirstReducesOutstandingAndRecordsRemainderAsCredit()
    {
        using var db=new TestDb();
        var (returns,sales,product,owner)=await Prepare(db);
        var cart=new Cart();cart.AddOrIncrement(product,3);
        var customer=SaleCustomer.Create("Customer","03001234567",null,null);
        var sale=await sales.CheckoutAsync(cart,new CreditPayment(30,15),owner,customer:customer);
        var refund=await returns.RecordAsync(sale.Id,[new(sale.Lines.Single().Id,2,false)],"Customer return",false,Guid.NewGuid());
        Assert.Equal(15,refund.ReceivableReduction);Assert.Equal(5,refund.CustomerCredit);
        Assert.Equal(0,refund.CashRefund);Assert.Equal(0,sale.Outstanding);Assert.Equal(30,sale.GrossTotal);
    }

    [Fact]
    public async Task SeparatePartialReturnsShareOneRoundingBasisAndCannotExceedTheInvoice()
    {
        using var db = new TestDb();
        var (returns, sales, product, owner) = await Prepare(db);
        var cart = new Cart();
        cart.AddOrIncrement(product, 3);
        cart.ApplyCartDiscount(new FixedAmountDiscount(1));
        var sale = await sales.CheckoutAsync(cart, new CashPayment(29, 29), owner);
        ReturnSelection[] oneItem = [new(sale.Lines.Single().Id, 1, false)];
        var refunds = new List<decimal>();
        for (var i = 0; i < 3; i++)
        {
            var refund = await returns.RecordAsync(sale.Id, oneItem, "Partial return", true, Guid.NewGuid());
            refunds.Add(refund.CashRefund);
        }
        Assert.Equal(new[] { 9.67m, 9.66m, 9.67m }, refunds);
        Assert.Equal(sale.GrossTotal, refunds.Sum());
        await Assert.ThrowsAsync<DomainException>(() =>
            returns.RecordAsync(sale.Id, oneItem, "Excess return", true, Guid.NewGuid()));
    }
}
