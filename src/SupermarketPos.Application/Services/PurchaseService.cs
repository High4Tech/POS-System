using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;

namespace SupermarketPos.Application.Services;

public sealed class PurchaseService(IProductRepository products, IPurchaseRepository purchases, IUnitOfWork unitOfWork,
    ITradeLedger? ledger = null,IStockLocations? locations=null,IAuthService? auth=null) : IPurchaseService
{
    public Task<IReadOnlyList<Purchase>> GetHistoryAsync(CancellationToken ct = default)
    {
        auth?.RequireAdmin();
        return purchases.GetRecentAsync(ct);
    }

    public Task<IReadOnlyList<Supplier>> GetSuppliersAsync(CancellationToken ct = default) => purchases.GetSuppliersAsync(ct);

    public Task<Supplier> AddSupplierAsync(string name, CancellationToken ct = default) =>
        unitOfWork.ExecuteInTransactionAsync(token =>
        {
            auth?.RequireAdmin();
            return GetOrAddSupplierAsync(name, token);
        }, ct);

    public Task<Supplier> SaveSupplierAsync(int? id, SupplierDetails details, CancellationToken ct = default) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            auth?.RequireAdmin();
            var name = details.Name.Trim();
            if (await purchases.FindSupplierAsync(name, token) is { } duplicate && duplicate.Id != id)
                throw new DomainException("A supplier with this name already exists.");
            var supplier = id is { } supplierId
                ? await purchases.FindSupplierAsync(supplierId, token) ?? throw new DomainException("Supplier no longer exists.")
                : new Supplier(name);
            supplier.Update(details);
            if (id is null) await purchases.AddSupplierAsync(supplier, token);
            await unitOfWork.SaveChangesAsync(token);
            return supplier;
        }, ct);

    public Task<Purchase> ReceiveAsync(string supplier, string? reference, IReadOnlyList<PurchaseItem> items,
        User receivedBy, CancellationToken ct = default) =>
        unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            auth?.RequireAdmin();
            var selected = await GetOrAddSupplierAsync(supplier, token);
            return await ReceiveCoreAsync(selected, reference, DateTime.Today, null, items, receivedBy, null, token);
        }, ct);

    public Task<Purchase> ReceiveAsync(int supplierId, string? reference, DateTime purchaseDate, string? notes,
        IReadOnlyList<PurchaseItem> items, User receivedBy, Guid confirmationId, CancellationToken ct = default,
        decimal? initialAmountPaid = null, string paymentMethod = "CASH")
    {
        auth?.RequireAdmin();
        if (confirmationId == Guid.Empty) throw new DomainException("A purchase confirmation ID is required.");
        return unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            if (await purchases.GetByConfirmationAsync(confirmationId, token) is { } saved)
            {
                if (saved.SupplierId != supplierId) throw new DomainException("The confirmation belongs to another purchase.");
                return saved;
            }
            var supplier = await purchases.FindSupplierAsync(supplierId, token)
                ?? throw new DomainException("Select an existing supplier.");
            return await ReceiveCoreAsync(supplier, reference, purchaseDate, notes, items, receivedBy, confirmationId, token, initialAmountPaid, paymentMethod);
        }, ct);
    }

    private async Task<Supplier> GetOrAddSupplierAsync(string name, CancellationToken ct)
    {
        var supplier = new Supplier(name);
        if (await purchases.FindSupplierAsync(supplier.Name, ct) is { } existing) return existing;

        await purchases.AddSupplierAsync(supplier, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return supplier;
    }

    private async Task<Purchase> ReceiveCoreAsync(Supplier supplier, string? reference, DateTime purchaseDate,
        string? notes, IReadOnlyList<PurchaseItem> items, User receivedBy, Guid? confirmationId, CancellationToken ct,
        decimal? initialAmountPaid = null, string paymentMethod = "CASH")
    {
        var now = DateTime.Now;
        var entries = new List<(Product Product, PurchaseLine Line, InventoryBatch? Batch)>();
        foreach (var item in items)
        {
            var product = await products.GetByIdAsync(item.ProductId, ct)
                ?? throw new DomainException("A product on this purchase is no longer available.");
            var factor=item.MedicineUnit is{} medicineUnit?product.SellingUnitSize(medicineUnit):item.Packaged?product.UnitsPerPackage:1;
            if(item.ExpectedUnitSize is{} expected&&expected!=factor||item.ExpectedUnit is{} unit&&unit!=product.Unit)throw new DomainException("Measure_ChangedUnit");
            var line = new PurchaseLine(product, item.Quantity, item.UnitCost,item.Packaged,item.MedicineUnit);
            if (product.TrackSerial && (ledger is null || item.Units?.Count != item.Quantity))
                throw new DomainException($"Enter one serial or IMEI for each received {product.Name}.");
            if (!product.TrackSerial && item.Units?.Count > 0) throw new DomainException("Enable serial tracking before receiving device identifiers.");
            InventoryBatch? batch = null;
            if (product.TrackExpiry)
            {
                if (string.IsNullOrWhiteSpace(item.BatchLot) || item.BatchExpiry is not { } expiry)
                    throw new DomainException($"Enter the batch and expiry for {product.Name}.");
                batch = product.RecordBatch(item.BatchLot, expiry, line.Quantity, supplier.Id, now);
            }
            else
            {
                if (item.BatchLot is not null || item.BatchExpiry is not null)
                    throw new DomainException($"Enable expiry tracking for {product.Name} before receiving its batch.");
                product.Restock(line.Quantity);
            }
            product.SetConvertedPurchaseCost(line.UnitCost);
            entries.Add((product, line, batch));
        }

        var purchase = new Purchase(supplier.Name, reference, now, receivedBy.DisplayName,
            entries.Select(entry => entry.Line), supplier.Id, confirmationId, purchaseDate, notes, initialAmountPaid, paymentMethod);
        await purchases.AddAsync(purchase, ct);
        await unitOfWork.SaveChangesAsync(ct);

        for (var index = 0; index < entries.Count; index++)
            if (entries[index].Product.TrackSerial)
                await ledger!.ReceiveUnitsAsync(entries[index].Product, items[index].Units!, purchase.Id, purchaseDate, ct);

        for(var i=0;i<entries.Count;i++)
        {
            var (product,line,batch)=entries[i];
            if (batch is not null) line.SetBatch(batch);
            if(locations is not null)line.SetLocation(await locations.PlaceReceivedAsync(product.Id,batch?.Id,SupermarketPos.Domain.Pricing.MeasuredGoods.BaseQuantity(line.Quantity,product.Unit),items[i].LocationId,ct));
            unitOfWork.RecordMovement(new StockMovement(product.Id, product.Name, line.Quantity,
                StockMovementKind.PurchaseReceipt, now, purchase.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), batch));
        }
        await unitOfWork.SaveChangesAsync(ct);
        return purchase;
    }
}
