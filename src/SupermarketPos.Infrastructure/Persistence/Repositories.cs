using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Entities;

namespace SupermarketPos.Infrastructure.Persistence;

public sealed class ProductRepository : IProductRepository
{
    private readonly PosDbContext _db;
    public ProductRepository(PosDbContext db) => _db = db;

    public Task<Product?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.Products.Include(p => p.Category).Include(p => p.Batches).FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<Product?> GetCurrentForRegisterAsync(int id, CancellationToken ct = default) =>
        _db.Products.AsNoTracking().Include(p => p.Batches).FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken ct = default) =>
        _db.Products.AsNoTracking().Include(p => p.Batches).FirstOrDefaultAsync(p => p.Barcode == barcode || p.PackageBarcode==barcode, ct);

    public Task<Product?> GetByPluAsync(string plu, CancellationToken ct = default) =>
        _db.Products.AsNoTracking().Include(p => p.Batches).FirstOrDefaultAsync(p =>
            p.Barcode.Length == 13 && p.Barcode.StartsWith("2") && p.Barcode.Substring(2, 5) == plu, ct);

    public async Task<IReadOnlyList<Product>> SearchByNameAsync(string term, CancellationToken ct = default)
    {
        term = (term ?? string.Empty).Trim();
        return await _db.Products.Include(p => p.Category)
            .Where(p => p.IsActive && (EF.Functions.Like(p.Name, $"%{term}%") || p.Barcode.StartsWith(term)))
            .OrderBy(p => p.Name)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken ct = default) =>
        await _db.Products.AsNoTracking().Include(p => p.Category).Include(p => p.Batches).OrderBy(p => p.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<Product>> GetByCategoryAsync(int categoryId, CancellationToken ct = default) =>
        await _db.Products.Include(p => p.Category)
            .Where(p => p.IsActive && p.CategoryId == categoryId).OrderBy(p => p.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<Product>> GetLowStockAsync(decimal threshold, CancellationToken ct = default)
    {
        // Money/quantity are TEXT in SQLite, so filter the numeric comparison in memory.
        var all = await _db.Products.AsNoTracking().Include(p => p.Category).Include(p => p.Batches).ToListAsync(ct);
        return all.Where(p => p.StockTracked&&p.AvailableForSale(DateOnly.FromDateTime(DateTime.Today)) <= (p.MinimumStock??threshold))
                  .OrderBy(p => p.StockQuantity).ToList();
    }

    public async Task AddAsync(Product product, CancellationToken ct = default) =>
        await _db.Products.AddAsync(product, ct);

    public async Task<IReadOnlyList<StockMovement>> GetMovementsAsync(int productId, CancellationToken ct = default) =>
        await _db.StockMovements.AsNoTracking().Where(move => move.ProductId == productId)
            .OrderByDescending(move => move.OccurredAt).ThenByDescending(move => move.Id).ToListAsync(ct);

    public void Remove(Product product) => _db.Products.Remove(product);
}

public sealed class CategoryRepository : ICategoryRepository
{
    private readonly PosDbContext _db;
    public CategoryRepository(PosDbContext db) => _db = db;

    public async Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct = default) =>
        await _db.Categories.OrderBy(c => c.Name).ToListAsync(ct);

    public Task<Category?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task AddAsync(Category category, CancellationToken ct = default) =>
        await _db.Categories.AddAsync(category, ct);

    public void Remove(Category category) => _db.Categories.Remove(category);
}

public sealed class SaleRepository : ISaleRepository
{
    private readonly PosDbContext _db;
    public SaleRepository(PosDbContext db) => _db = db;

    public async Task<IReadOnlyList<CustomerCreditMovement>> GetCreditRefundsAsync(DateTime fromInclusive, DateTime toExclusive, CancellationToken ct = default) =>
        await _db.CustomerCreditMovements.AsNoTracking().Where(m => m.TargetSaleId == null && m.RecordedAt >= fromInclusive && m.RecordedAt < toExclusive).ToArrayAsync(ct);

    public Task<bool> HasSoldSerialAsync(int productId, string serial, CancellationToken ct = default) =>
        _db.SaleLines.AnyAsync(line => line.ProductId == productId && line.SerialNumber == serial, ct);

    public async Task AddAsync(Sale sale, CancellationToken ct = default) =>
        await _db.Sales.AddAsync(sale, ct);

    public Task<Sale?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.Sales.Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<Sale?> GetByReceiptNoAsync(string receiptNo, CancellationToken ct = default) =>
        _db.Sales.Include(s => s.Lines).FirstOrDefaultAsync(s => s.ReceiptNo == receiptNo, ct);

    public Task<Sale?> GetByCheckoutIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Sales.Include(s => s.Lines).FirstOrDefaultAsync(s => s.CheckoutId == id, ct);

    public Task<bool> HasSourceReceiptAsync(string reference, CancellationToken ct = default) =>
        _db.Sales.AnyAsync(s => s.SourceReceiptNo == reference || s.ReceiptNo == reference, ct);

    public async Task<IReadOnlyList<Sale>> GetCustomerSalesAsync(CancellationToken ct = default) =>
        await _db.Sales.AsNoTracking().Where(s => s.CustomerName != null || s.CustomerPhone != null ||
                s.CustomerEmail != null || s.CustomerAddress != null)
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id).ToListAsync(ct);

    public async Task<IReadOnlyList<Sale>> GetByDateRangeAsync(DateTime fromInclusive, DateTime toExclusive, CancellationToken ct = default) =>
        await _db.Sales.Include(s => s.Lines)
            .Where(s => s.CreatedAt >= fromInclusive && s.CreatedAt < toExclusive)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);
}

public sealed class PurchaseRepository : IPurchaseRepository
{
    private readonly PosDbContext _db;
    public PurchaseRepository(PosDbContext db) => _db = db;

    public async Task AddAsync(Purchase purchase, CancellationToken ct = default) =>
        await _db.Purchases.AddAsync(purchase, ct);

    public async Task<IReadOnlyList<Purchase>> GetRecentAsync(CancellationToken ct = default) =>
        await _db.Purchases.AsNoTracking().Include(p => p.Lines)
            .OrderByDescending(p => p.ReceivedAt).ThenByDescending(p => p.Id)
            .ToListAsync(ct);

    public Task<Purchase?> GetByConfirmationAsync(Guid key, CancellationToken ct = default) =>
        _db.Purchases.Include(p => p.Lines).SingleOrDefaultAsync(p => p.ConfirmationId == key, ct);

    public async Task<IReadOnlyList<Supplier>> GetSuppliersAsync(CancellationToken ct = default) =>
        await _db.Suppliers.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);

    public Task<Supplier?> FindSupplierAsync(int id, CancellationToken ct = default) =>
        _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<Supplier?> FindSupplierAsync(string name, CancellationToken ct = default) =>
        _db.Suppliers.FirstOrDefaultAsync(s => s.Name == name, ct);

    public async Task AddSupplierAsync(Supplier supplier, CancellationToken ct = default) =>
        await _db.Suppliers.AddAsync(supplier, ct);
}

public sealed class UserRepository : IUserRepository
{
    private readonly PosDbContext _db;
    public UserRepository(PosDbContext db) => _db = db;

    public Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);

    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default) =>
        await _db.Users.OrderBy(u => u.Username).ToListAsync(ct);

    public async Task AddAsync(User user, CancellationToken ct = default) =>
        await _db.Users.AddAsync(user, ct);

    public Task SaveAsync(User user, CancellationToken ct = default) =>
        SavePropertyAsync(user, u => u.PasswordHash, ct);

    public Task SaveDisplayNameAsync(User user, CancellationToken ct = default) =>
        SavePropertyAsync(user, u => u.DisplayName, ct);

    private Task SavePropertyAsync(User user, Expression<Func<User, string>> property, CancellationToken ct)
    {
        // A failed stock operation may have cleared this account from the change tracker.
        if (_db.Entry(user).State == EntityState.Detached)
            _db.Attach(user);
        _db.Entry(user).Property(property).IsModified = true;
        return _db.SaveChangesAsync(ct);
    }
}

public sealed class SettingsRepository : ISettingsRepository
{
    private readonly PosDbContext _db;
    public SettingsRepository(PosDbContext db) => _db = db;

    public async Task<AppSettings> GetAsync(CancellationToken ct = default)
    {
        var settings = await _db.Settings.FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new AppSettings("Tech Store", string.Empty, "en", 5m, new[] { 0.01m, 0.10m, 0.20m });
            await _db.Settings.AddAsync(settings, ct);
            await _db.SaveChangesAsync(ct);
        }
        return settings;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        if (settings.Id == 0)
            await _db.Settings.AddAsync(settings, ct);
        else
            _db.Settings.Update(settings);
        if(await _db.BusinessConfigurations.SingleOrDefaultAsync(ct) is{} business)
        {
            var profile=System.Text.Json.JsonSerializer.Deserialize<SupermarketPos.Domain.Business.BusinessProfile>(business.ProfileJson)!;
            business.ProfileJson=System.Text.Json.JsonSerializer.Serialize(profile with{StoreName=settings.StoreName,Address=settings.StoreAddress,Logo=settings.LogoPath,Language=settings.Language});business.Revision++;
        }
        await _db.SaveChangesAsync(ct);
    }
}
