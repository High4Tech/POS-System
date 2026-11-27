using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;

namespace SupermarketPos.Application.Services;

/// <summary>
/// Checkout use-case. All money math is delegated to <see cref="PricingService"/>; stock decrement,
/// receipt numbering and persistence happen atomically inside one transaction so a stock shortfall
/// rolls the whole sale back.
/// </summary>
public sealed class SaleService : ISaleService
{
    private readonly IProductRepository _products;
    private readonly ISaleRepository _sales;
    private readonly IReceiptNumberGenerator _receiptNumbers;
    private readonly IUnitOfWork _uow;
    private readonly Func<DateTime> _clock;
    private readonly ITradeLedger? _ledger;
    private readonly IBusinessCompliance? _compliance;
    private readonly IStockLocations? _locations;

    public SaleService(IProductRepository products, ISaleRepository sales,
                       IReceiptNumberGenerator receiptNumbers, IUnitOfWork uow,
                       Func<DateTime>? clock = null, ITradeLedger? ledger = null, IBusinessCompliance? compliance = null,IStockLocations? locations=null)
    {
        _products = products;
        _sales = sales;
        _receiptNumbers = receiptNumbers;
        _uow = uow;
        _clock = clock ?? (() => DateTime.Now);
        _ledger = ledger;
        _compliance = compliance;
        _locations=locations;
    }

    public Task<Sale> CheckoutAsync(Cart cart, Payment payment, User cashier, CancellationToken ct = default, SaleCustomer? customer = null, Guid? checkoutId = null)
    {
        if (cart is null || cart.IsEmpty)
            throw new DomainException("Cannot check out an empty cart.");
        if(cart.Lines.Any(line=>line.Product.Kind=="digital"))throw new DomainException("Digital_Separate");
        if (payment is null)
            throw new DomainException("A payment is required.");
        if (cashier is null)
            throw new DomainException("A cashier is required.");
        if (checkoutId == Guid.Empty) throw new DomainException("Trade_CheckoutConflict");

        var summary = PricingService.Summarize(cart);

        // The payment must cover the resolved gross total.
        if (payment is CreditPayment credit ? credit.Total != summary.GrossTotal : payment.Amount != summary.GrossTotal)
            throw new DomainException("Payment amount does not match the sale total.");

        return _uow.ExecuteInTransactionAsync(async token =>
        {
            if (checkoutId is { } checkoutKey && await _sales.GetByCheckoutIdAsync(checkoutKey, token) is { } existing)
            {
                var request = summary.LineCharges.Select(c => (c.Line.Product.Id, c.Line.DisplayQuantity, c.Line.UnitCode,
                    c.Line.VatRate, c.Line.GrossUnitPrice, c.Line.BatchId, c.Line.SerialNumber, c.FinalGross));
                var saved = existing.Lines.OrderBy(line=>line.Id).Select(l => (l.ProductId, l.Quantity, l.UnitCode,
                    l.VatRate, l.UnitPrice, l.BatchId, l.SerialNumber, l.LineGross));
                if (existing.CashierId != cashier.Id || existing.PaymentMethod != payment.Method || existing.AmountPaid != payment.Amount ||
                    existing.CreditDueDate != (payment as CreditPayment)?.DueDate || existing.CreditNote != ((payment as CreditPayment)?.Note ?? "") ||
                    existing.CashTendered != (payment as CashPayment)?.Tendered || existing.Customer != customer || !request.SequenceEqual(saved))
                    throw new DomainException("Trade_CheckoutConflict");
                return existing;
            }
            if (_compliance is not null) await _compliance.ValidateCheckoutAsync(cart, token);
            var now = _clock();
            var saleLines = new List<SaleLine>();
            var allocated = new Dictionary<int, InventoryBatch>();
            var devices = new List<(SerializedUnit Unit, int Warranty)>();

            foreach (var charge in summary.LineCharges)
            {
                var product = await _products.GetByIdAsync(charge.Line.Product.Id, token)
                              ?? throw new DomainException($"Product '{charge.Line.Product.Name}' no longer exists.");
                if (!product.IsActive)
                    throw new DomainException($"Product '{product.Name}' is inactive.");
                if(product.Kind=="digital")throw new DomainException("Digital_Separate");
                if(product.VatRate!=charge.Line.VatRate)throw new DomainException("Tax_RateMismatch");
                if(!product.SellingUnitConfigured)throw new DomainException("Pharmacy_MissingUnit");
                if(product.Unit!=charge.Line.Product.Unit||charge.Line.MedicineUnit is{} medicineUnit&&product.SellingUnitSize(medicineUnit)!=charge.Line.PackageSize||charge.Line.MedicineUnit is null&&charge.Line.IsPackage&&product.UnitsPerPackage!=charge.Line.PackageSize)throw new DomainException("Measure_ChangedUnit");
                if(charge.Line.HasPriceOverride&&!cashier.IsAdmin)throw new DomainException("Trade_PricePermission");
                if(charge.Line.MedicineUnit is not null&&charge.Line.GrossUnitPrice<=0)throw new DomainException("Pharmacy_ConfirmUnitPrice");
                if(product.RequiresPrescription&&!charge.Line.PrescriptionChecked||product.RequiresDispensingReference&&string.IsNullOrWhiteSpace(charge.Line.DispensingReference))throw new DomainException("Pharmacy_TraceabilityRequired");
                if(!product.StockTracked&&(product.TrackExpiry||product.TrackSerial))throw new DomainException("Pharmacy_TraceabilityRequired");

                var serial = charge.Line.SerialNumber;
                SerializedUnit? device = null;
                if (product.TrackSerial)
                {
                    if (serial is null || charge.Line.Quantity != 1 || _ledger is null)
                        throw new DomainException("Select one registered serial or IMEI for every tracked device.");
                    device = await _ledger.AvailableUnitAsync(product.Id, serial, token);
                    if (devices.Any(item => item.Unit.Id == device.Id)) throw new DomainException("The same device appears twice in this sale.");
                    devices.Add((device, product.WarrantyMonths));
                }
                if (serial is not null && (saleLines.Any(line => line.ProductId == product.Id && line.SerialNumber == serial) ||
                    await _sales.HasSoldSerialAsync(product.Id, serial, token)))
                    throw new DomainException($"Serial {serial} has already been sold.");
                InventoryBatch? batch = null;
                if (charge.Line.BatchId is { } id)
                {
                    batch = product.SellBatch(id, charge.Line.BatchExpiry, charge.Line.Quantity, now);
                    allocated[id] = batch;
                }
                else if(product.StockTracked)product.DecreaseStock(charge.Line.Quantity);

                saleLines.Add(new SaleLine(
                    productId: product.Id,
                    barcode: product.Barcode,
                    name: product.Name,
                    unitPrice: charge.Line.GrossUnitPrice,
                    vatRate: charge.Line.VatRate,
                    quantity: charge.Line.DisplayQuantity,
                    discountDescription: charge.Line.LineDiscount?.Describe(),
                    lineGross: charge.FinalGross,
                    unitCost: product.StockTracked?product.PurchaseCost*(charge.Line.IsPackage?charge.Line.PackageSize:1):null, batch: batch, serialNumber: serial));
                saleLines[^1].SetDispensing(!product.StockTracked,charge.Line.PrescriptionChecked,charge.Line.DispensingReference);
                if(product.StockTracked&&_locations is not null)saleLines[^1].SetLocation(await _locations.AllocateSaleAsync(product,batch?.Id,MeasuredGoods.BaseQuantity(charge.Line.Quantity,product.Unit),charge.Line.LocationId,token));
                saleLines[^1].SetMeasurement(charge.Line.UnitCode,MeasuredGoods.BaseQuantity(charge.Line.Quantity,product.Unit));
                saleLines[^1].SetWarranty(product.WarrantyMonths, now, device);
            }

            var receiptNo = await _receiptNumbers.NextAsync(now, token);

            var sale = new Sale(
                receiptNo: receiptNo,
                cashierId: cashier.Id,
                cashierName: cashier.DisplayName,
                lines: saleLines,
                grossTotal: summary.GrossTotal,
                netTotal: summary.NetTotal,
                vatTotal: summary.VatTotal,
                payment: payment,
                createdAt: now,
                customer: customer, checkoutId: checkoutId);

            await _sales.AddAsync(sale, token);
            await _uow.SaveChangesAsync(token);
            foreach (var device in devices) device.Unit.Sell(sale.Id, now, device.Warranty);
            if (_compliance is not null) await _compliance.CaptureAsync(sale, token);
            foreach (var line in saleLines.Where(l=>!l.StockUntracked))
                _uow.RecordMovement(new StockMovement(line.ProductId, line.Name, -MeasuredGoods.Quantity(line.BaseQuantity,cart.Lines.First(l=>l.Product.Id==line.ProductId).Product.Unit),
                    StockMovementKind.Sale, now, receiptNo, line.BatchId is { } batchId ? allocated[batchId] : null));
            await _uow.SaveChangesAsync(token);
            return sale;
        }, ct);
    }
}
