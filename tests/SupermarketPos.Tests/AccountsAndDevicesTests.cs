using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Application.Services;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Infrastructure;
using SupermarketPos.Infrastructure.Persistence;

namespace SupermarketPos.Tests;

public sealed class AccountsAndDevicesTests
{
    private static readonly User Operator = new("operator", "hash", "Operator", UserRole.Admin);
    private static readonly SaleCustomer Customer = new("Customer", "03001234567", null, null);

    private static Product Seed(TestDb test, bool tracked = false)
    {
        var category = new Category("Electronics");
        test.Context.Categories.Add(category);
        test.Context.SaveChanges();
        var product = new Product("123456789", "Phone", 1100m, .1m, Unit.Piece, 2, category.Id);
        product.SetSerialTracking(tracked, 12);
        test.Context.Products.Add(product);
        test.Context.SaveChanges();
        return product;
    }

    private static SaleService Sales(PosDbContext db, DateTime? date = null)
    {
        var uow = new UnitOfWork(db);
        return new SaleService(new ProductRepository(db), new SaleRepository(db), new ReceiptNumberGenerator(db), uow,
            () => date ?? DateTime.Now, new TradeLedger(db, uow));
    }

    private static PurchaseService Purchases(PosDbContext db)
    {
        var uow = new UnitOfWork(db);
        return new PurchaseService(new ProductRepository(db), new PurchaseRepository(db), uow, new TradeLedger(db, uow));
    }

    [Fact]
    public async Task CreditSaleAndLaterPaymentsPreserveRevenueAndPreventDuplicateCollections()
    {
        using var test = new TestDb();
        var product = Seed(test);
        var cart = new Cart();
        cart.AddOrIncrement(product, 1);
        var sale = await Sales(test.Context).CheckoutAsync(cart, new CreditPayment(1100, 100), Operator, customer: Customer);
        Assert.Equal(1000, sale.Outstanding);
        Assert.Equal(1100, sale.GrossTotal);
        var report = await new ReportService(new SaleRepository(test.Context)).GetXReportAsync(sale.CreatedAt);
        Assert.Equal(100, report.PaymentSplit.CashTotal);
        var ledger = new TradeLedger(test.Context, new UnitOfWork(test.Context));
        var confirmation = Guid.NewGuid();
        await ledger.RecordPaymentAsync(sale.Id, false, 300, "CASH", DateTime.Today, "First", Operator, confirmation);
        await ledger.RecordPaymentAsync(sale.Id, false, 300, "CASH", DateTime.Today, "First", Operator, confirmation);
        Assert.Single(await ledger.PaymentsAsync(sale.Id, false));
        Assert.Equal(700, sale.Outstanding);
        Assert.Equal(1100, sale.GrossTotal);
        Assert.Equal(100, sale.AmountPaid);
        await Assert.ThrowsAsync<DomainException>(() => ledger.RecordPaymentAsync(sale.Id, false, 701, "CASH",
            DateTime.Today, null, Operator, Guid.NewGuid()));
        using var current = test.NewContext();
        var persisted = current.Sales.Single();
        Assert.Equal(700, persisted.Outstanding);
        Assert.Single(current.AccountPayments);
        await new TradeLedger(current, new UnitOfWork(current)).RecordPaymentAsync(persisted.Id, false, 700, "BANK_TRANSFER",
            DateTime.Today, "Final", Operator, Guid.NewGuid());
        Assert.Equal(0, persisted.Outstanding);
    }

    [Fact]
    public async Task CreditRequiresAnIdentifiableCustomerAndRollsBackStock()
    {
        using var test = new TestDb();
        var product = Seed(test);
        var cart = new Cart();
        cart.AddOrIncrement(product, 1);
        await Assert.ThrowsAsync<DomainException>(() => Sales(test.Context).CheckoutAsync(cart, new CreditPayment(1100, 0), Operator));
        using var persisted = test.NewContext();
        Assert.Empty(persisted.Sales);
        Assert.Equal(2, persisted.Products.Single().StockQuantity);
    }

    [Fact]
    public async Task SupplierPartPaymentAndLaterSettlementDoNotChangeStockTwice()
    {
        using var test = new TestDb();
        var product = Seed(test);
        var service = Purchases(test.Context);
        var supplier = await service.AddSupplierAsync("Supplier");
        var confirmation = Guid.NewGuid();
        var items = new[] { new PurchaseItem(product.Id, 2, 800) };
        var purchase = await service.ReceiveAsync(supplier.Id, "A1", DateTime.Today, null, items, Operator, confirmation,
            initialAmountPaid: 500, paymentMethod: "BANK_TRANSFER");
        var again = await service.ReceiveAsync(supplier.Id, "A1", DateTime.Today, null, items, Operator, confirmation,
            initialAmountPaid: 500, paymentMethod: "BANK_TRANSFER");
        Assert.Equal(purchase.Id, again.Id);
        Assert.Equal(1100, purchase.Outstanding);
        await new TradeLedger(test.Context, new UnitOfWork(test.Context)).RecordPaymentAsync(purchase.Id, true,
            1100, "CASH", DateTime.Today, null, Operator, Guid.NewGuid());
        Assert.Equal(0, purchase.Outstanding);
        Assert.Equal(4, test.Context.Products.Single().StockQuantity);
        Assert.Single(test.Context.Purchases);
    }

    [Fact]
    public async Task RegisteredDevicesSellOnceAndRetainCustomerAndWarrantySnapshots()
    {
        using var test = new TestDb();
        var product = Seed(test, true);
        var ledger = new TradeLedger(test.Context, new UnitOfWork(test.Context));
        await ledger.RegisterExistingAsync(product.Id, [UnitIdentifiers.Create("phone-a", "123456789012345", "223456789012345")]);
        var found = await ledger.FindUnitAsync("223456789012345");
        Assert.Equal("PHONE-A", found!.Value.Serial);
        var cart = new Cart();
        cart.AddOrIncrement(product, 1, serial: "PHONE-A");
        var date = new DateTime(2026, 11, 19);
        var sale = await Sales(test.Context, date).CheckoutAsync(cart, new CashPayment(1100, 1100), Operator, customer: Customer);
        Assert.Equal(date.AddMonths(12), sale.Lines.Single().WarrantyUntil);
        Assert.Equal("123456789012345", sale.Lines.Single().Imei1);
        product.SetSerialTracking(true, 3);
        await test.Context.SaveChangesAsync();
        var serial = (await ledger.SerialsAsync()).Single();
        Assert.Equal(sale.Id, serial.Unit.SaleId);
        Assert.Equal(Customer.Name, serial.CustomerName);
        Assert.Equal(date.AddMonths(12), serial.Unit.WarrantyUntil);
        using var next = test.NewContext();
        var retry = new Cart();
        retry.AddOrIncrement(next.Products.Single(), 1, serial: "PHONE-A");
        await Assert.ThrowsAsync<DomainException>(() => Sales(next).CheckoutAsync(retry, new CashPayment(1100, 1100), Operator));
        using var check = test.NewContext();
        Assert.Single(check.Sales);
        Assert.Equal(1, check.Products.Single().StockQuantity);
    }

    [Fact]
    public async Task ReceivedUnitsLinkToSupplierInvoiceAndOnlyTheChosenDeviceIsSold()
    {
        using var test = new TestDb();
        var product = Seed(test, true);
        var purchases = Purchases(test.Context);
        var supplier = await purchases.AddSupplierAsync("Phone distributor");
        var received = await purchases.ReceiveAsync(supplier.Id, "PHONES-1", DateTime.Today, null,
            [new PurchaseItem(product.Id, 2, 800, Units: [UnitIdentifiers.Create("A"), UnitIdentifiers.Create("B")])],
            Operator, Guid.NewGuid(), initialAmountPaid: 0);
        Assert.Equal(1600, received.Outstanding);
        var cart = new Cart();
        cart.AddOrIncrement(product, 1, serial: "B");
        var sale = await Sales(test.Context).CheckoutAsync(cart, new CreditPayment(1100, 0), Operator, customer: Customer);
        using var check = test.NewContext();
        Assert.Equal(received.Id, check.SerializedUnits.Single(u => u.Serial == "B").PurchaseId);
        Assert.Equal(sale.Id, check.SerializedUnits.Single(u => u.Serial == "B").SaleId);
        Assert.Null(check.SerializedUnits.Single(u => u.Serial == "A").SaleId);
        Assert.Equal(3, check.Products.Single().StockQuantity);
        Assert.Equal(1100, check.Sales.Single().Outstanding);
        Assert.Equal(800, check.SaleLines.Single().UnitCost);
        Assert.False(check.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task DuplicateIdentifiersRollbackPurchaseAndStock()
    {
        using var test = new TestDb();
        var product = Seed(test, true);
        var service = Purchases(test.Context);
        var supplier = await service.AddSupplierAsync("Supplier");
        var units = new[] { UnitIdentifiers.Create("A", "123456789012345"), UnitIdentifiers.Create("123456789012345") };
        await Assert.ThrowsAsync<DomainException>(() => service.ReceiveAsync(supplier.Id, "BAD", DateTime.Today, null,
            [new PurchaseItem(product.Id, 2, 800, Units: units)], Operator, Guid.NewGuid()));
        using var current = test.NewContext();
        Assert.Empty(current.Purchases);
        Assert.Empty(current.SerializedUnits);
        Assert.Equal(2, current.Products.Single().StockQuantity);
    }

    [Fact]
    public async Task SerialCountMustMatchReceivedQuantityAndExistingRegistrationCannotExceedStock()
    {
        using var test = new TestDb();
        var product = Seed(test, true);
        var ledger = new TradeLedger(test.Context, new UnitOfWork(test.Context));
        await Assert.ThrowsAsync<DomainException>(() => ledger.RegisterExistingAsync(product.Id,
            [UnitIdentifiers.Create("A"), UnitIdentifiers.Create("B"), UnitIdentifiers.Create("C")]));
        var service = Purchases(test.Context);
        var supplier = await service.AddSupplierAsync("Supplier");
        await Assert.ThrowsAsync<DomainException>(() => service.ReceiveAsync(supplier.Id, null, DateTime.Today, null,
            [new PurchaseItem(product.Id, 2, 800, Units: [UnitIdentifiers.Create("A")])], Operator, Guid.NewGuid()));
        using var check = test.NewContext();
        Assert.Empty(check.SerializedUnits);
        Assert.Empty(check.Purchases);
    }

    [Fact]
    public async Task PaymentReloadsCurrentBalanceAndPreventsOverpaymentFromAnotherContext()
    {
        using var test = new TestDb();
        var product = Seed(test);
        var cart = new Cart();
        cart.AddOrIncrement(product, 1);
        var sale = await Sales(test.Context).CheckoutAsync(cart, new CreditPayment(1100, 0), Operator, customer: Customer);
        using var other = test.NewContext();
        _ = other.Sales.Single();
        await new TradeLedger(test.Context, new UnitOfWork(test.Context)).RecordPaymentAsync(sale.Id, false, 1000,
            "CASH", DateTime.Today, null, Operator, Guid.NewGuid());
        await Assert.ThrowsAsync<DomainException>(() => new TradeLedger(other, new UnitOfWork(other)).RecordPaymentAsync(sale.Id,
            false, 200, "CASH", DateTime.Today, null, Operator, Guid.NewGuid()));
        Assert.Single(test.Context.AccountPayments);
    }

    [Fact]
    public async Task StockCorrectionsCannotRemoveRegisteredUnsoldDevices()
    {
        using var test = new TestDb();
        var product = Seed(test, true);
        var work = new UnitOfWork(test.Context);
        var ledger = new TradeLedger(test.Context, work);
        await ledger.RegisterExistingAsync(product.Id, [UnitIdentifiers.Create("A"), UnitIdentifiers.Create("B")]);
        var settings = new SettingsRepository(test.Context);
        var inventory = new InventoryService(new ProductRepository(test.Context), settings, work, ledger);
        await Assert.ThrowsAsync<DomainException>(() => inventory.AdjustStockAsync(product.Id, 1));
        using var check = test.NewContext();
        Assert.Equal(2, check.Products.Single().StockQuantity);
        Assert.Equal(2, check.SerializedUnits.Count());
        Assert.Empty(check.StockMovements);
    }

    [Fact]
    public async Task UpgradePreservesExistingPurchasesAsPaidAndBacksUpTheDatabase()
    {
        var folder = Directory.CreateTempSubdirectory("pos-accounts-");
        try
        {
            var services = new ServiceCollection().AddPos(Path.Combine(folder.FullName, "pos.db"));
            await using var provider = services.BuildServiceProvider();
            await using (var old = provider.CreateAsyncScope())
            {
                var db = old.ServiceProvider.GetRequiredService<PosDbContext>();
                await db.GetService<IMigrator>().MigrateAsync("20261118140000_InventoryExpiry");
                await db.Database.ExecuteSqlRawAsync("""
                    INSERT INTO Purchases (Supplier,Reference,ReceivedAt,ReceivedBy,Notes) VALUES ('Old supplier','OLD','2026-11-01','Operator','');
                    INSERT INTO PurchaseLines (PurchaseId,ProductId,ProductName,Barcode,Unit,Quantity,UnitCost)
                    VALUES (last_insert_rowid(),1,'Cable','OLD',0,'2','100');
                    """);
            }
            await using (var scope = provider.CreateAsyncScope())
            {
                await DependencyInjection.InitializeDatabaseAsync(scope.ServiceProvider);
                var db = scope.ServiceProvider.GetRequiredService<PosDbContext>();
                var purchase = await db.Purchases.Include(p => p.Lines).SingleAsync();
                Assert.Equal(200, purchase.Total);
                Assert.Equal(0, purchase.Outstanding);
                Assert.Null(purchase.InitialAmountPaid);
                Assert.Empty(db.AccountPayments);
            }
            Assert.Single(Directory.GetFiles(Path.Combine(folder.FullName, "Backups"), "Before-accounts-devices-*.db"));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); folder.Delete(true); }
    }
}
