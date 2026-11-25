using SupermarketPos.Application.Abstractions;
using SupermarketPos.Application.Services;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Persistence;

namespace SupermarketPos.Tests;

public class InventoryBatchTests
{
    [Fact]
    public async Task BatchEditChangesOnlyRequestedFieldsAndBatchDeleteRemovesAllSelectedProducts()
    {
        using var db = new TestDb();
        var products = Seed(db);
        var inventory = Build(db);

        await inventory.UpdateProductsAsync(products.Select(p => p.Id).ToArray(),
            new ProductBatchUpdate(399m, null, null, null));
        using (var persisted = db.NewContext())
        {
            Assert.All(persisted.Products, product => Assert.Equal(399m, product.UnitPrice));
            Assert.All(persisted.Products, product => Assert.Equal(8m, product.StockQuantity));
        }

        await inventory.DeleteProductsAsync(products.Select(p => p.Id).ToArray());
        using var deleted = db.NewContext();
        Assert.Empty(deleted.Products);
        Assert.Single(deleted.Categories);
    }

    [Fact]
    public async Task FailedBatchEditRollsBackEarlierRows()
    {
        using var db = new TestDb();
        var products = Seed(db);
        var inventory = Build(db);

        await Assert.ThrowsAsync<DomainException>(() =>
            inventory.UpdateProductsAsync(new[] { products[0].Id, int.MaxValue },
                new ProductBatchUpdate(899m, null, null, null)));

        using var persisted = db.NewContext();
        Assert.All(persisted.Products, product => Assert.Equal(499m, product.UnitPrice));
    }

    private static InventoryService Build(TestDb db) =>
        new(new ProductRepository(db.Context), new SettingsRepository(db.Context), new UnitOfWork(db.Context));

    private static Product[] Seed(TestDb db)
    {
        var category = new Category("Cases");
        db.Context.Categories.Add(category);
        db.Context.SaveChanges();
        var products = new[]
        {
            new Product("01", "Vivo case", 499m, 0.20m, Unit.Piece, 8m, category.Id),
            new Product("02", "Oppo case", 499m, 0.20m, Unit.Piece, 8m, category.Id)
        };
        db.Context.Products.AddRange(products);
        db.Context.SaveChanges();
        return products;
    }
}
