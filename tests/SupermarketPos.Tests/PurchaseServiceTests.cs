using SupermarketPos.Application.Abstractions;
using SupermarketPos.Application.Services;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Persistence;

namespace SupermarketPos.Tests;

public sealed class PurchaseServiceTests
{
    private static readonly User Operator = new("operator", "hash", "Store operator", UserRole.Cashier);

    [Fact]
    public async Task ReceivingPurchase_PersistsLinesAndIncreasesStockTogether()
    {
        using var db = new TestDb();
        var products = Seed(db);
        var service = CreateService(db);

        var purchase = await service.ReceiveAsync("Local Supplier", "INV-101", new[]
        {
            new PurchaseItem(products.Pieces.Id, 3m, 100m),
            new PurchaseItem(products.Weight.Id, 1.250m, 200.50m)
        }, Operator);

        Assert.Equal(550.63m, purchase.Total);
        Assert.Equal(Operator.DisplayName, purchase.ReceivedBy);
        using var persisted = db.NewContext();
        Assert.Equal(7m, persisted.Products.Single(p => p.Id == products.Pieces.Id).StockQuantity);
        Assert.Equal(5.250m, persisted.Products.Single(p => p.Id == products.Weight.Id).StockQuantity);
        var history = await new PurchaseRepository(persisted).GetRecentAsync();
        var saved = Assert.Single(history);
        Assert.Equal("INV-101", saved.Reference);
        Assert.Equal(2, saved.Lines.Count);
        Assert.Equal(purchase.Total, saved.Total);
    }

    [Fact]
    public async Task InvalidLaterLine_RollsBackThePurchaseAndAllowsRetry()
    {
        using var db = new TestDb();
        var products = Seed(db);
        var service = CreateService(db);

        await Assert.ThrowsAsync<DomainException>(() => service.ReceiveAsync("Supplier", null, new[]
        {
            new PurchaseItem(products.Pieces.Id, 2m, 100m),
            new PurchaseItem(products.Weight.Id, -1m, 20m)
        }, Operator));

        using (var persisted = db.NewContext())
        {
            Assert.Empty(persisted.Purchases);
            Assert.Empty(persisted.StockMovements);
            Assert.Empty(persisted.Suppliers);
            Assert.All(persisted.Products, product => Assert.Equal(4m, product.StockQuantity));
        }

        await service.ReceiveAsync("Supplier", null,
            new[] { new PurchaseItem(products.Pieces.Id, 2m, 100m) }, Operator);

        using var retried = db.NewContext();
        Assert.Single(retried.Purchases);
        Assert.Equal(6m, retried.Products.Single(p => p.Id == products.Pieces.Id).StockQuantity);
    }

    [Fact]
    public async Task LinkedReceiptIsIdempotentAndRetainsSupplierCostAndStockHistory()
    {
        using var db = new TestDb();
        var (product, _) = Seed(db);
        var service = CreateService(db);
        var firstSupplier = await service.AddSupplierAsync("First supplier");
        var duplicateSupplier = await service.AddSupplierAsync(" first supplier ");
        Assert.Equal(firstSupplier.Id, duplicateSupplier.Id);
        var secondSupplier = await service.AddSupplierAsync("Second supplier");
        var confirmation = Guid.NewGuid();
        var date = new DateTime(2026, 10, 1);
        var items = new[] { new PurchaseItem(product.Id, 2m, 80m) };
        var first = await service.ReceiveAsync(firstSupplier.Id, "A1", date, "Original", items, Operator, confirmation);
        var duplicate = await service.ReceiveAsync(firstSupplier.Id, "A1", date, "Original", items, Operator, confirmation);
        Assert.Equal(first.Id, duplicate.Id);
        await service.ReceiveAsync(secondSupplier.Id, "B1", date, null,
            new[] { new PurchaseItem(product.Id, 1m, 95m) }, Operator, Guid.NewGuid());

        using var persisted = db.NewContext();
        var updated = persisted.Products.Single(p => p.Id == product.Id);
        Assert.Equal(7m, updated.StockQuantity);
        Assert.Equal(95m, updated.PurchaseCost);
        Assert.Equal(150m, updated.UnitPrice);
        var movement = persisted.StockMovements.OrderBy(m => m.Id).ToArray();
        Assert.Equal(new[] { 2m, 1m }, movement.Select(m => m.QuantityChange));
        Assert.All(movement, m => Assert.Equal(StockMovementKind.PurchaseReceipt, m.Kind));
        var history = await service.GetHistoryAsync();
        var original = Assert.Single(history.Where(p => p.SupplierId == firstSupplier.Id));
        Assert.Equal(date, original.PurchasedOn);
        Assert.Equal("Original", original.Notes);
        Assert.Equal(80m, Assert.Single(original.Lines).UnitCost);
        Assert.Single(history.Where(p => p.SupplierId == secondSupplier.Id));
    }

    private static PurchaseService CreateService(TestDb db) =>
        new(new ProductRepository(db.Context), new PurchaseRepository(db.Context), new UnitOfWork(db.Context));

    private static (Product Pieces, Product Weight) Seed(TestDb db)
    {
        var category = new Category("Groceries");
        db.Context.Categories.Add(category);
        db.Context.SaveChanges();
        var pieces = new Product("100", "Flour", 150m, 0.1m, Unit.Piece, 4m, category.Id);
        var weight = new Product("101", "Rice", 220m, 0.1m, Unit.Kilogram, 4m, category.Id);
        db.Context.Products.AddRange(pieces, weight);
        db.Context.SaveChanges();
        return (pieces, weight);
    }
}
