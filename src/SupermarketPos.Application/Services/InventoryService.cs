using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;

namespace SupermarketPos.Application.Services;

public sealed class InventoryService : IInventoryService
{
    private readonly IProductRepository _products;
    private readonly ISettingsRepository _settings;
    private readonly IUnitOfWork _uow;
    private readonly ITradeLedger? _ledger;

    public InventoryService(IProductRepository products, ISettingsRepository settings, IUnitOfWork uow, ITradeLedger? ledger = null)
    {
        _products = products;
        _settings = settings;
        _uow = uow;
        _ledger = ledger;
    }

    public async Task RestockAsync(int productId, decimal quantity, CancellationToken ct = default)
    {
        var product = await _products.GetByIdAsync(productId, ct)
                      ?? throw new DomainException("Product not found.");
        if (product.Unit == Unit.Piece ? quantity != decimal.Truncate(quantity) : quantity != Math.Round(quantity, 3))
            throw new DomainException("Enter whole quantities for pieces or up to three decimals for kilograms.");
        product.Restock(quantity);
        _uow.RecordMovement(new StockMovement(product.Id, product.Name, quantity, StockMovementKind.ManualAdjustment, DateTime.Now));
        await _uow.SaveChangesAsync(ct);
    }

    public Task AdjustStockAsync(int productId, decimal newQuantity, CancellationToken ct = default) =>
        _uow.ExecuteInTransactionAsync(async token =>
        {
            var product = await _products.GetByIdAsync(productId, token)
                          ?? throw new DomainException("Product not found.");
            var delta = newQuantity - product.StockQuantity;
            product.SetStock(newQuantity);
            if (_ledger is not null) await _ledger.ValidateProductTrackingAsync(product, token);
            RecordAdjustment(product, delta);
            await _uow.SaveChangesAsync(token);
            return true;
        }, ct);

    public async Task<IReadOnlyList<Product>> GetLowStockAsync(CancellationToken ct = default)
    {
        var settings = await _settings.GetAsync(ct);
        return await _products.GetLowStockAsync(settings.LowStockThreshold, ct);
    }

    public Task UpdateProductsAsync(IReadOnlyList<int> ids, ProductBatchUpdate change, CancellationToken ct = default) =>
        ApplyToProductsAsync(ids, product =>
        {
            if (change.Price is { } price) product.SetPrice(price);
            if (change.Stock is { } stock)
            {
                if (product.Unit == Unit.Piece ? stock != decimal.Truncate(stock) : stock != Math.Round(stock, 3))
                    throw new DomainException("Enter whole stock for pieces or up to three decimals for kilograms.");
                product.SetStock(stock);
            }
            if (change.VatRate is { } vat) product.SetVatRate(vat);
            if (change.CategoryId is { } categoryId) product.SetCategory(categoryId);
        }, ct);

    public Task DeleteProductsAsync(IReadOnlyList<int> ids, CancellationToken ct = default) =>
        ApplyToProductsAsync(ids, product =>
        {
            if (product.TrackExpiry && (product.StockQuantity > 0m || product.Batches.Count > 0))
                throw new DomainException("Mark this product inactive to keep its batch history.");
            RecordAdjustment(product, -product.StockQuantity);
            _products.Remove(product);
        }, ct);

    private Task ApplyToProductsAsync(IReadOnlyList<int> ids, Action<Product> change, CancellationToken ct) =>
        _uow.ExecuteInTransactionAsync(async token =>
        {
            foreach (var id in ids.Distinct())
            {
                var product = await _products.GetByIdAsync(id, token)
                    ?? throw new DomainException("One of the selected products is no longer available.");
                var oldStock = product.StockQuantity;
                change(product);
                if (_ledger is not null) await _ledger.ValidateProductTrackingAsync(product, token);
                RecordAdjustment(product, product.StockQuantity - oldStock);
            }

            await _uow.SaveChangesAsync(token);
            return true;
        }, ct);

    private void RecordAdjustment(Product product, decimal delta)
    {
        if (delta != 0)
            _uow.RecordMovement(new StockMovement(product.Id, product.Name, delta, StockMovementKind.ManualAdjustment, DateTime.Now));
    }
}
