using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Application.Services;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Infrastructure.Persistence;
using SupermarketPos.Infrastructure;

namespace SupermarketPos.Tests;

public sealed class ExpiryInventoryTests
{
    private static readonly DateTime SaleDate = new(2026, 11, 18, 10, 0, 0);
    private static readonly DateOnly Early = new(2027, 12, 31);
    private static readonly DateOnly Late = new(2028, 6, 30);
    private static readonly User Operator = new("operator", "hash", "Store operator", UserRole.Cashier);
    private const string Gtin = "09521101530001";

    [Theory]
    [InlineData("]d201095211015300011727120010B2027\u001d21SN-1", "B2027", "2027-12-31", "SN-1")]
    [InlineData("]Q321SN-2\u001d01095211015300011728020010BX", "BX", "2028-02-29", "SN-2")]
    [InlineData("(01)09521101530001(17)280630(10)B2028", "B2028", "2028-06-30", null)]
    public void Gs1ParsesReorderedFieldsSeparatorsAndMonthEnd(string raw, string lot, string expiry, string? serial)
    {
        var code = Assert.IsType<Gs1Barcode>(Gs1Barcode.Parse(raw, new DateOnly(2026, 11, 18)));
        Assert.Equal(Gtin, code.Gtin);
        Assert.Equal(lot, code.Batch);
        Assert.Equal(expiry, code.ExpiryDate?.ToString("yyyy-MM-dd"));
        Assert.Equal(serial, code.SerialNumber);
    }

    [Fact]
    public void RequiresGs1SymbologyAndRejectsInvalidDatesChecksumsAndDuplicateAis()
    {
        Assert.False(Gs1Barcode.IsMarked("010952110153000117280630", "]d1"));
        Assert.False(Gs1Barcode.IsMarked("010952110153000117280630", "]Q1"));
        Assert.Null(Gs1Barcode.Parse($"]d201{Gtin}17270229", DateOnly.FromDateTime(SaleDate)));
        Assert.Null(Gs1Barcode.Parse("]d2010952110153000217280630", DateOnly.FromDateTime(SaleDate)));
        Assert.Null(Gs1Barcode.Parse($"]d201{Gtin}1728063017280630", DateOnly.FromDateTime(SaleDate)));
    }

    [Fact]
    public async Task ExistingDatabaseIsBackedUpBeforeMigrationAndLegacyStockStaysUntracked()
    {
        var directory = Directory.CreateTempSubdirectory("pos-expiry-");
        try
        {
            var path = Path.Combine(directory.FullName, "pos.db");
            var services = new ServiceCollection().AddPos(path);
            await using var provider = services.BuildServiceProvider();
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
                await db.GetService<IMigrator>().MigrateAsync("20261118010000_SalesAndSupplierDetails");
                await db.Database.ExecuteSqlRawAsync("INSERT INTO Categories(Name) VALUES('Legacy category'); INSERT INTO Products(Barcode,Name,UnitPrice,VatRate,Unit,StockQuantity,CategoryId) VALUES('LEGACY','Legacy stock','42','0',0,'9',1);");
            }

            await using (var scope = provider.CreateAsyncScope())
            {
                await DependencyInjection.InitializeDatabaseAsync(scope.ServiceProvider);
                var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
                var product = await db.Products.OrderBy(item => item.Id).FirstAsync();
                Assert.Equal(9m, product.StockQuantity);
                Assert.False(product.TrackExpiry);
                Assert.Empty(db.InventoryBatches);
            }

            var backup = Assert.Single(Directory.GetFiles(Path.Combine(directory.FullName, "Backups"), "Before-expiry-*.db"));
            await using var original = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = backup, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            await original.OpenAsync();
            await using var query = original.CreateCommand();
            query.CommandText = "SELECT StockQuantity FROM Products ORDER BY Id LIMIT 1";
            Assert.Equal("9", await query.ExecuteScalarAsync());
            query.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'InventoryBatches'";
            Assert.Equal(0L, await query.ExecuteScalarAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ReceivesSeparateBatchesAndDeductsOnlyTheScannedLotAcrossReceipts()
    {
        using var db = new TestDb();
        var product = Seed(db, stock: 3m);
        var repo = new ProductRepository(db.Context);
        var purchases = new PurchaseService(repo, new PurchaseRepository(db.Context), new UnitOfWork(db.Context));
        var supplier = await purchases.AddSupplierAsync("Supplier");
        var confirmation = Guid.NewGuid();
        var items = new[] { new PurchaseItem(product.Id, 4m, 80m, "B27", Early), new PurchaseItem(product.Id, 6m, 90m, "B28", Late) };
        var receipt = await purchases.ReceiveAsync(supplier.Id, "INV1", SaleDate, null, items, Operator, confirmation);
        var repeated = await purchases.ReceiveAsync(supplier.Id, "INV1", SaleDate, null, items, Operator, confirmation);
        Assert.Equal(receipt.Id, repeated.Id);
        Assert.Equal(new[] { "B27", "B28" }, receipt.Lines.Select(line => line.BatchLot));
        Assert.All(receipt.Lines, line => Assert.True(line.BatchId > 0));
        await purchases.ReceiveAsync(supplier.Id, "INV2", SaleDate, null,
            [new PurchaseItem(product.Id, 2m, 92m, "B28", Late)], Operator, Guid.NewGuid());

        var lookup = new RegisterBarcodeLookup(repo, new BarcodeService());
        var match = await lookup.FindGs1Async(new Gs1Barcode(Gtin, "B28", Late, "SERIAL1"));
        Assert.NotNull(match);
        var laterBatch = Assert.Single(match.Product.Batches.Where(batch => batch.Lot == "B28"));
        var earlierBatch = Assert.Single(match.Product.Batches.Where(batch => batch.Lot == "B27"));
        var cart = new Cart();
        cart.AddOrIncrement(match.Product, 1m, laterBatch, "SERIAL1");
        cart.AddOrIncrement(match.Product, 2m, earlierBatch);
        var checkout = Checkout(db);
        var billed = await checkout.CheckoutAsync(cart, new CardPayment(360m), Operator);
        Assert.Equal(new[] { "B28", "B27" }, billed.Lines.Select(line => line.BatchLot));
        Assert.Equal("SERIAL1", billed.Lines[0].SerialNumber);

        var duplicate = new Cart();
        duplicate.AddOrIncrement(match.Product, 1m, laterBatch, "SERIAL1");
        await Assert.ThrowsAsync<DomainException>(() => checkout.CheckoutAsync(duplicate, new CashPayment(120m, 120m), Operator));

        using var saved = db.NewContext();
        var refreshed = await new ProductRepository(saved).GetByIdAsync(product.Id);
        Assert.NotNull(refreshed);
        Assert.Equal(12m, refreshed.StockQuantity);
        Assert.Equal(3m, refreshed.UnassignedStock);
        Assert.Equal(2m, refreshed.Batches.Single(batch => batch.Lot == "B27").Quantity);
        Assert.Equal(7m, refreshed.Batches.Single(batch => batch.Lot == "B28").Quantity);
        Assert.Equal(90m, saved.PurchaseLines.Single(line => line.PurchaseId == receipt.Id && line.BatchLot == "B28").UnitCost);
        Assert.Equal(2, saved.StockMovements.Count(movement => movement.Kind == StockMovementKind.Sale && movement.BatchId != null));
    }

    [Fact]
    public async Task ExpiredOrUnspecifiedBatchRollsBackAllLinesAndDateMismatchRollsBackReceiving()
    {
        using var db = new TestDb();
        var tracked = Seed(db);
        var regular = new Product("123", "Normal item", 100m, 0m, Unit.Piece, 5m, tracked.CategoryId);
        db.Context.Products.Add(regular);
        tracked.RecordBatch("OLD", new DateOnly(2026, 10, 1), 6m, null, SaleDate);
        db.Context.SaveChanges();
        var selectable = Assert.Single(tracked.Batches);
        var cart = new Cart();
        cart.AddOrIncrement(regular, 1m);
        cart.AddOrIncrement(tracked, 1m, selectable);
        await Assert.ThrowsAsync<DomainException>(() => Checkout(db).CheckoutAsync(cart, new CashPayment(220m, 220m), Operator));

        var missing = new Cart();
        missing.AddOrIncrement(tracked, 1m);
        await Assert.ThrowsAsync<DomainException>(() => Checkout(db).CheckoutAsync(missing, new CardPayment(120m), Operator));

        var purchases = new PurchaseService(new ProductRepository(db.Context), new PurchaseRepository(db.Context), new UnitOfWork(db.Context));
        await Assert.ThrowsAsync<DomainException>(() => purchases.ReceiveAsync("Supplier", null,
            [new PurchaseItem(regular.Id, 2m, 40m), new PurchaseItem(tracked.Id, 2m, 90m, "OLD", Late)], Operator));

        using var saved = db.NewContext();
        Assert.Equal(5m, saved.Products.Single(p => p.Id == regular.Id).StockQuantity);
        Assert.Equal(6m, saved.InventoryBatches.Single().Quantity);
        Assert.Empty(saved.Sales);
        Assert.Empty(saved.Purchases);
        Assert.Empty(saved.StockMovements);
    }

    private static Product Seed(TestDb db, decimal stock = 0m)
    {
        var category = new Category("Medicines");
        db.Context.Categories.Add(category);
        db.Context.SaveChanges();
        var product = new Product(Gtin[1..], "Medicine", 120m, 0m, Unit.Piece, stock, category.Id);
        product.SetExpiryTracking(true);
        db.Context.Products.Add(product);
        db.Context.SaveChanges();
        return product;
    }

    private static SaleService Checkout(TestDb db) => new(new ProductRepository(db.Context), new SaleRepository(db.Context),
        new ReceiptNumberGenerator(db.Context), new UnitOfWork(db.Context), () => SaleDate);
}
