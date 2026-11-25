using Microsoft.EntityFrameworkCore;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;

namespace SupermarketPos.Infrastructure.Persistence;

public sealed class TradeLedger(PosDbContext db, IUnitOfWork work) : ITradeLedger
{
    public async Task<IReadOnlyList<AccountPayment>> PaymentsAsync(int invoiceId, bool purchase, CancellationToken ct = default) =>
        await db.AccountPayments.AsNoTracking().Where(p => purchase ? p.PurchaseId == invoiceId : p.SaleId == invoiceId)
            .OrderByDescending(p => p.PaidOn).ThenByDescending(p => p.Id).ToArrayAsync(ct);

    public Task RecordPaymentAsync(int invoiceId, bool purchase, decimal amount, string method, DateTime paidOn,
        string? reference, User recordedBy, Guid confirmationId, CancellationToken ct = default) =>
        work.ExecuteInTransactionAsync(async token =>
        {
            if (await db.AccountPayments.AsNoTracking().SingleOrDefaultAsync(p => p.ConfirmationId == confirmationId, token) is { } saved)
            {
                if ((purchase ? saved.PurchaseId : saved.SaleId) != invoiceId || saved.Amount != amount || saved.Method != method)
                    throw new DomainException("This payment confirmation belongs to another payment.");
                return true;
            }
            var payment = new AccountPayment(confirmationId, purchase ? null : invoiceId, purchase ? invoiceId : null,
                amount, method, paidOn, reference, recordedBy.DisplayName);
            if (purchase)
            {
                var invoice = await db.Purchases.Include(p => p.Lines).SingleOrDefaultAsync(p => p.Id == invoiceId, token)
                    ?? throw new DomainException("Purchase invoice no longer exists.");
                await db.Entry(invoice).ReloadAsync(token);
                invoice.RecordBalancePayment(amount);
            }
            else
            {
                var invoice = await db.Sales.SingleOrDefaultAsync(p => p.Id == invoiceId, token)
                    ?? throw new DomainException("Sales invoice no longer exists.");
                await db.Entry(invoice).ReloadAsync(token);
                invoice.RecordBalancePayment(amount);
            }
            db.AccountPayments.Add(payment);
            await work.SaveChangesAsync(token);
            return true;
        }, ct);

    public async Task<IReadOnlyList<SerialRecord>> SerialsAsync(int? productId = null, CancellationToken ct = default)
    {
        await EnsureElectronicsAllowedAsync(ct);
        return await (from unit in db.SerializedUnits.AsNoTracking()
               join product in db.Products.AsNoTracking() on unit.ProductId equals product.Id
               join sale in db.Sales.AsNoTracking() on unit.SaleId equals (int?)sale.Id into saleJoin
               from sale in saleJoin.DefaultIfEmpty()
               where productId == null || unit.ProductId == productId
               orderby unit.Id descending
               select new SerialRecord(unit, product.Name, sale == null ? null : sale.ReceiptNo,
                   sale == null ? null : sale.CustomerName, sale == null ? null : sale.CustomerPhone,
                   sale == null ? null : sale.CustomerEmail)).ToArrayAsync(ct);
    }

    public Task RegisterExistingAsync(int productId, IReadOnlyList<UnitIdentifiers> units, CancellationToken ct = default) =>
        work.ExecuteInTransactionAsync(async token =>
        {
            var product = await db.Products.SingleOrDefaultAsync(p => p.Id == productId, token)
                ?? throw new DomainException("Product no longer exists.");
            await db.Entry(product).ReloadAsync(token);
            var registered = await db.SerializedUnits.CountAsync(u => u.ProductId == productId && u.SaleId == null, token);
            if (!product.TrackSerial || registered + units.Count > product.StockQuantity)
                throw new DomainException("Enable serial tracking and register no more units than the unregistered stock on hand.");
            await AddUnitsAsync(product.Id, units, null, DateTime.Now, token);
            await work.SaveChangesAsync(token);
            return true;
        }, ct);

    public async Task ReceiveUnitsAsync(Product product, IReadOnlyList<UnitIdentifiers> units, int purchaseId, DateTime receivedOn, CancellationToken ct) =>
        await AddUnitsAsync(product.Id, units, purchaseId, receivedOn, ct);

    private async Task AddUnitsAsync(int productId, IReadOnlyList<UnitIdentifiers> units, int? purchaseId, DateTime receivedOn, CancellationToken ct)
    {
        await EnsureElectronicsAllowedAsync(ct);
        if (units.Count == 0) throw new DomainException("Enter at least one serial or IMEI.");
        var all = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in units)
        {
            var unit = new SerializedUnit(productId, input, purchaseId, receivedOn);
            foreach (var identifier in UnitIdentifiers.Create(unit.Serial, unit.Imei1, unit.Imei2).All)
            {
                if (!all.Add(identifier) || db.SerializedUnits.Local.Any(u => u.Serial == identifier || u.Imei1 == identifier || u.Imei2 == identifier) ||
                    await db.SerializedUnits.AnyAsync(u => u.Serial == identifier || u.Imei1 == identifier || u.Imei2 == identifier, ct))
                    throw new DomainException($"Serial or IMEI {identifier} is already registered.");
            }
            if (await db.SaleLines.AnyAsync(l => l.SerialNumber == unit.Serial, ct))
                throw new DomainException($"Serial {unit.Serial} already belongs to a previous sale.");
            db.SerializedUnits.Add(unit);
        }
    }

    public async Task<SerializedUnit> AvailableUnitAsync(int productId, string serial, CancellationToken ct)
    {
        var normalized = UnitIdentifiers.Create(serial).Serial;
        var unit = await db.SerializedUnits.SingleOrDefaultAsync(u => u.ProductId == productId && u.Serial == normalized, ct)
            ?? throw new DomainException("Register this device serial or IMEI in inventory first.");
        await db.Entry(unit).ReloadAsync(ct);
        if (unit.SaleId.HasValue) throw new DomainException("This device has already been sold.");
        return unit;
    }

    public async Task<(Product Product, string Serial)?> FindUnitAsync(string identifier, CancellationToken ct = default)
    {
        if (!await ElectronicsAllowedAsync(ct)) return null;
        var normalized = identifier.Trim().ToUpperInvariant();
        var unit = await db.SerializedUnits.AsNoTracking().SingleOrDefaultAsync(u =>
            u.Serial == normalized || u.Imei1 == normalized || u.Imei2 == normalized, ct);
        if (unit is null) return null;
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == unit.ProductId, ct);
        return product is null ? null : (product, unit.Serial);
    }

    public async Task ValidateProductTrackingAsync(Product product, CancellationToken ct = default)
    {
        if(product.TrackSerial||product.WarrantyMonths>0)await EnsureElectronicsAllowedAsync(ct);
        var registered = await db.SerializedUnits.CountAsync(u => u.ProductId == product.Id && u.SaleId == null, ct);
        if (!product.TrackSerial && registered > 0 || registered > product.StockQuantity)
            throw new DomainException("Keep serial tracking enabled and stock at least as high as the registered devices on hand.");
    }
    private async Task EnsureElectronicsAllowedAsync(CancellationToken ct)
    {
        if(!await ElectronicsAllowedAsync(ct)) throw new DomainException("Trade_DeviceUnavailable");
    }
    private async Task<bool> ElectronicsAllowedAsync(CancellationToken ct)
    {
        var row=await db.BusinessConfigurations.AsNoTracking().SingleOrDefaultAsync(ct);
        return row is null || System.Text.Json.JsonSerializer.Deserialize<SupermarketPos.Domain.Business.BusinessProfile>(row.ProfileJson)?.DeviceTrackingAvailable==true;
    }
}
