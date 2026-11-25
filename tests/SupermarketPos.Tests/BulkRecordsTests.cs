using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SupermarketPos.Application.Services;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Infrastructure.Persistence;

namespace SupermarketPos.Tests;

public sealed class BulkRecordsTests
{
    [Fact]
    public async Task PhotoOnlyRowsUpdateExistingProductsAndLeaveStockPricingAndMovementsIntact()
    {
        using var database = new TestDb();
        var context = database.Context;
        var (product, _) = await SeedAsync(context);
        var service = Records(context);
        var photoPath = @"D:\Example\Product Images\imported.png";
        var csv = BulkRecordService.ProductTemplate + "\n0005,,,,,,,,,\"images/case.png\"\n";
        Assert.Equal(1, await service.ImportProductsAsync(new StringReader(csv), path =>
        {
            Assert.Equal("images/case.png", path);
            return photoPath;
        }));
        using var fresh = database.NewContext();
        var updated = await fresh.Products.SingleAsync();
        Assert.Equal(product.Id, updated.Id);
        Assert.Equal(photoPath, updated.ImagePath);
        Assert.Equal(120m, updated.UnitPrice);
        Assert.Equal(5m, updated.StockQuantity);
        Assert.Empty(await fresh.StockMovements.ToListAsync());
        var invalidBatch = csv + "0006,Cable,Missing,140,20,1,piece,50,,\n";
        await Assert.ThrowsAsync<DomainException>(() =>
            service.ImportProductsAsync(new StringReader(invalidBatch), _ => "uncommitted.png"));
        await fresh.Entry(updated).ReloadAsync();
        Assert.Equal(photoPath, updated.ImagePath);
    }

    [Fact]
    public async Task ProductsImportPreservesBarcodeAndSupplierAndRollsBackAnInvalidBatch()
    {
        using var database = new TestDb();
        var context = database.Context;
        context.Categories.Add(new Category("Accessories"));
        var supplier = new Supplier("Supplier One");
        supplier.Update(new SupplierDetails("Supplier One", "COMPANY", "Contact", "03000000000"));
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();
        var service = Records(context);
        await Assert.ThrowsAsync<DomainException>(() => service.ImportProductsAsync(new StringReader(
            BulkRecordService.ProductTemplate + "\n0005,\"Case, clear\",Accessories,120,20,3,piece,55,Supplier One\n" +
            "0006,Cable,Missing,140,20,1,piece,50,\n")));
        Assert.Empty(await context.Products.ToListAsync());
        Assert.Empty(await context.StockMovements.ToListAsync());

        Assert.Equal(1, await service.ImportProductsAsync(new StringReader(
            BulkRecordService.ProductTemplate + "\n0005,\"Case, clear\",Accessories,120,20,3,piece,55,Supplier One\n")));
        var product = await context.Products.SingleAsync();
        Assert.Equal("0005", product.Barcode);
        Assert.Equal("Case, clear", product.Name);
        Assert.Equal(supplier.Id, product.SupplierId);
        Assert.Equal(55m, product.PurchaseCost);
        Assert.Equal(3m, (await context.StockMovements.SingleAsync()).QuantityChange);
    }

    [Fact]
    public async Task HistoricalImportGroupsReceiptsAndPreservesCostsPaymentsAndInventory()
    {
        using var database = new TestDb();
        var context = database.Context;
        var (product, user) = await SeedAsync(context);
        var csv = BulkRecordService.SaleTemplate + "\n" +
                  "OLD-001,2025-06-01 13:05,BANK_TRANSFER,0005,Old case,2,120,20,50,216,,A Customer,,,\n" +
                  "OLD-001,2025-06-01 13:05,BANK_TRANSFER,,Discontinued cable,1,120,20,30,,,A Customer,,,\n";
        var service = Records(context);
        var imported = await service.ImportSalesAsync(new StringReader(csv), user);
        Assert.Equal(1, imported.Count);
        var sale = await context.Sales.Include(s => s.Lines).SingleAsync();
        Assert.Equal("OLD-001", sale.SourceReceiptNo);
        Assert.Equal(new DateTime(2025, 6, 1, 13, 5, 0), sale.CreatedAt);
        Assert.Equal("BANK_TRANSFER", sale.PaymentMethod);
        Assert.Equal("A Customer", sale.CustomerName);
        Assert.Equal(336m, sale.GrossTotal);
        Assert.Equal(56m, sale.VatTotal);
        Assert.Equal(150m, sale.GrossProfit);
        Assert.Contains(sale.Lines, line => line.ProductId == 0 && line.Name == "Discontinued cable");
        Assert.Equal(5m, (await context.Products.FindAsync(product.Id))!.StockQuantity);
        Assert.Empty(await context.StockMovements.ToListAsync());
        await Assert.ThrowsAsync<DomainException>(() => service.ImportSalesAsync(new StringReader(csv), user));
        Assert.Equal(1, await context.Sales.CountAsync());

        var report = await new ReportService(new SaleRepository(context)).GetXReportAsync(sale.CreatedAt);
        Assert.Equal(1, report.PaymentSplit.OtherCount);
        Assert.Equal(336m, report.PaymentSplit.TotalAmount);
        await service.SaveCustomerAsync(sale.Id, null);
        Assert.Null((await context.Sales.SingleAsync()).Customer);
    }

    [Fact]
    public async Task CheckoutFreezesPurchaseCostAndOptionalCustomerDetails()
    {
        using var database = new TestDb();
        var context = database.Context;
        var (product, user) = await SeedAsync(context);
        var cart = new Cart();
        cart.AddOrIncrement(product, 1);
        var service = new SaleService(new ProductRepository(context), new SaleRepository(context),
            new ReceiptNumberGenerator(context), new UnitOfWork(context));
        var sale = await service.CheckoutAsync(cart, new CashPayment(120m, 120m), user,
            customer: SaleCustomer.Create("Customer", "03001234567", null, null));
        product.SetPurchaseCost(95m);
        await context.SaveChangesAsync();
        using var fresh = database.NewContext();
        var stored = await fresh.Sales.Include(s => s.Lines).SingleAsync();
        Assert.Equal(sale.Id, stored.Id);
        Assert.Equal(50m, stored.Lines.Single().UnitCost);
        Assert.Equal(50m, stored.GrossProfit);
        Assert.Equal("03001234567", stored.CustomerPhone);
    }

    [Fact]
    public async Task MigrationKeepsExistingSuppliersAndCatalog()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new PosDbContext(new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261117190000_SupplierReceipts");
        await context.Database.ExecuteSqlRawAsync("INSERT INTO Suppliers(Name) VALUES ('Legacy supplier'); INSERT INTO Categories(Name) VALUES('Legacy category'); INSERT INTO Products(Barcode,Name,UnitPrice,VatRate,Unit,StockQuantity,CategoryId) VALUES('LEGACY','Legacy item','42','0',0,'7',1);");
        await context.Database.MigrateAsync();
        var supplier = await context.Suppliers.SingleAsync();
        Assert.Equal("Legacy supplier", supplier.Name);
        Assert.Null(supplier.Phone);
        Assert.Equal("Legacy item",(await context.Products.SingleAsync()).Name);Assert.Equal(7,(await context.Products.SingleAsync()).StockQuantity);
        Assert.Empty(await context.Sales.ToListAsync());
    }

    private static BulkRecordService Records(PosDbContext context) => new(new ProductRepository(context),
        new CategoryRepository(context), new PurchaseRepository(context), new SaleRepository(context),
        new ReceiptNumberGenerator(context), new BarcodeService(), new UnitOfWork(context));

    private static async Task<(Product, User)> SeedAsync(PosDbContext context)
    {
        var category = new Category("Accessories");
        var user = new User("operator", "test-hash", "Operator", UserRole.Admin);
        context.Categories.Add(category);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var product = new Product("0005", "Case", 120m, 0.2m, Unit.Piece, 5m, category.Id);
        product.SetPurchaseCost(50m);
        context.Products.Add(product);
        await context.SaveChangesAsync();
        return (product, user);
    }
}
