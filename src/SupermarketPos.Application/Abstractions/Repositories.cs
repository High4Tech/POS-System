using SupermarketPos.Domain.Entities;

namespace SupermarketPos.Application.Abstractions;

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Product?> GetCurrentForRegisterAsync(int id, CancellationToken ct = default);
    Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken ct = default);
    /// <summary>Find a weighed product by the PLU embedded in a variable-measure barcode.</summary>
    Task<Product?> GetByPluAsync(string plu, CancellationToken ct = default);
    Task<IReadOnlyList<Product>> SearchByNameAsync(string term, CancellationToken ct = default);
    Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Product>> GetByCategoryAsync(int categoryId, CancellationToken ct = default);
    Task<IReadOnlyList<Product>> GetLowStockAsync(decimal threshold, CancellationToken ct = default);
    Task<IReadOnlyList<StockMovement>> GetMovementsAsync(int productId, CancellationToken ct = default);
    Task AddAsync(Product product, CancellationToken ct = default);
    void Remove(Product product);
}

public interface ICategoryRepository
{
    Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken ct = default);
    Task<Category?> GetByIdAsync(int id, CancellationToken ct = default);
    Task AddAsync(Category category, CancellationToken ct = default);
    void Remove(Category category);
}

public interface ISaleRepository
{
    Task<bool> HasSoldSerialAsync(int productId, string serial, CancellationToken ct = default);
    Task AddAsync(Sale sale, CancellationToken ct = default);
    Task<Sale?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Sale?> GetByReceiptNoAsync(string receiptNo, CancellationToken ct = default);
    Task<Sale?> GetByCheckoutIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> HasSourceReceiptAsync(string reference, CancellationToken ct = default);
    Task<IReadOnlyList<Sale>> GetCustomerSalesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Sale>> GetByDateRangeAsync(DateTime fromInclusive, DateTime toExclusive, CancellationToken ct = default);
    Task<IReadOnlyList<CustomerCreditMovement>> GetCreditRefundsAsync(DateTime fromInclusive, DateTime toExclusive, CancellationToken ct = default);
}

public interface IPurchaseRepository
{
    Task AddAsync(Purchase purchase, CancellationToken ct = default);
    Task<IReadOnlyList<Purchase>> GetRecentAsync(CancellationToken ct = default);
    Task<Purchase?> GetByConfirmationAsync(Guid key, CancellationToken ct = default);
    Task<IReadOnlyList<Supplier>> GetSuppliersAsync(CancellationToken ct = default);
    Task<Supplier?> FindSupplierAsync(int id, CancellationToken ct = default);
    Task<Supplier?> FindSupplierAsync(string name, CancellationToken ct = default);
    Task AddSupplierAsync(Supplier supplier, CancellationToken ct = default);
}

public interface IUserRepository
{
    Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    Task SaveAsync(User user, CancellationToken ct = default);
    Task SaveDisplayNameAsync(User user, CancellationToken ct = default);
}

public interface ISettingsRepository
{
    Task<AppSettings> GetAsync(CancellationToken ct = default);
    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
}

/// <summary>Generates atomic, sequential receipt numbers (yyyy-NNNNNN).</summary>
public interface IReceiptNumberGenerator
{
    Task<string> NextAsync(DateTime now, CancellationToken ct = default);
}

/// <summary>Commits tracked changes; lets a use-case run inside a single DB transaction.</summary>
public interface IUnitOfWork
{
    void RecordMovement(StockMovement movement);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default);
}
