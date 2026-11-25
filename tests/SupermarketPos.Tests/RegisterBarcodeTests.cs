using SupermarketPos.Application.Services;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Infrastructure.Persistence;

namespace SupermarketPos.Tests;

public sealed class RegisterBarcodeTests
{
    [Fact]
    public async Task ReadsCurrentStockEvenWhenRegisterContextHasAnOlderProduct()
    {
        using var db = new TestDb();
        var category = new Category("Audio");
        db.Context.Categories.Add(category);
        await db.Context.SaveChangesAsync();
        var product = new Product("X003SWHW5T", "Microphone", 24999m, 0m, Unit.Piece, 4m, category.Id);
        db.Context.Products.Add(product);
        await db.Context.SaveChangesAsync();

        using (var inventory = db.NewContext())
        {
            (await inventory.Products.FindAsync(product.Id))!.SetStock(1m);
            await inventory.SaveChangesAsync();
        }

        var repository = new ProductRepository(db.Context);
        var register = new RegisterBarcodeLookup(repository, new BarcodeService());
        Assert.Equal(4m, product.StockQuantity);
        Assert.Equal(1m, (await register.FindAsync(product.Barcode))!.Product.StockQuantity);
        Assert.Equal(1m, (await repository.GetCurrentForRegisterAsync(product.Id))!.StockQuantity);
        Assert.Null(await register.FindAsync("@553S8EW5T"));
    }

    [Fact]
    public async Task UsesValidatedUpcAliasOnlyWhenThereIsNoExactMatch()
    {
        using var db = new TestDb();
        var category = new Category("Cables");
        db.Context.Categories.Add(category);
        await db.Context.SaveChangesAsync();
        db.Context.Products.AddRange(
            new Product("036000291452", "USB cable", 300m, 0m, Unit.Piece, 1m, category.Id),
            new Product("036000291453", "HDMI cable", 500m, 0m, Unit.Piece, 1m, category.Id));
        await db.Context.SaveChangesAsync();
        var register = new RegisterBarcodeLookup(new ProductRepository(db.Context), new BarcodeService());

        Assert.Equal("USB cable", (await register.FindAsync("0036000291452"))!.Product.Name);
        Assert.Null(await register.FindAsync("0036000291453"));

        db.Context.Products.Add(new Product("0036000291452", "DisplayPort cable", 600m, 0m, Unit.Piece, 1m, category.Id));
        await db.Context.SaveChangesAsync();
        Assert.Equal("DisplayPort cable", (await register.FindAsync("0036000291452"))!.Product.Name);
    }

    [Fact]
    public void StockCheckAllowsExactRemainingQuantityAndPreventsOverselling()
    {
        Assert.True(CartStock.CanAdd(4m, 3m, 1m));
        Assert.True(CartStock.CanAdd(2.75m, 2.5m, 0.25m));
        Assert.False(CartStock.CanAdd(4m, 4m, 1m));
        Assert.False(CartStock.CanAdd(0m, 0m, 1m));
        Assert.False(CartStock.CanAdd(2.75m, 2.5m, 0.251m));
        Assert.False(CartStock.CanAdd(5m, 1m, -1m));
    }
}
