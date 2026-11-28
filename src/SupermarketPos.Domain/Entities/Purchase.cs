using SupermarketPos.Domain.Common;

namespace SupermarketPos.Domain.Entities;

public sealed class Purchase
{
    private readonly List<PurchaseLine> _lines = new();

    public int Id { get; private set; }
    public string Supplier { get; private set; } = string.Empty;
    public int? SupplierId { get; private set; }
    public string Reference { get; private set; } = string.Empty;
    public DateTime ReceivedAt { get; private set; }
    public string ReceivedBy { get; private set; } = string.Empty;
    public Guid? ConfirmationId { get; private set; }
    public string RequestFingerprint { get; private set; } = "";
    public DateTime? PurchasedOn { get; private set; }
    public string Notes { get; private set; } = "";
    public IReadOnlyList<PurchaseLine> Lines => _lines;
    public decimal Total => _lines.Sum(line => line.Total);
    public decimal? InitialAmountPaid { get; private set; }
    public string InitialPaymentMethod { get; private set; } = "CASH";
    public decimal BalancePaid { get; private set; }
    public int BalanceRevision { get; private set; }
    public decimal Outstanding => Money.Round2(Total - (InitialAmountPaid ?? Total) - BalancePaid);

    private Purchase() { }

    public Purchase(string supplier, string? reference, DateTime receivedAt, string receivedBy,
                    IEnumerable<PurchaseLine> lines, int? supplierId = null, Guid? confirmationId = null,
                    DateTime? purchasedOn = null, string? notes = null, decimal? initialAmountPaid = null, string paymentMethod = "CASH",string requestFingerprint="")
    {
        Supplier = (supplier ?? string.Empty).Trim();
        Reference = (reference ?? string.Empty).Trim();
        if (Supplier.Length is 0 or > 150)
            throw new DomainException("Enter a supplier name of up to 150 characters.");
        if (Reference.Length > 80)
            throw new DomainException("Enter an invoice reference of up to 80 characters.");

        _lines.AddRange(lines);
        if (_lines.Count == 0)
            throw new DomainException("Add at least one product to the purchase.");

        ReceivedAt = receivedAt;
        ReceivedBy = receivedBy;
        SupplierId = supplierId;
        ConfirmationId = confirmationId;
        if(requestFingerprint.Length!=0&&(requestFingerprint.Length!=64||!requestFingerprint.All(Uri.IsHexDigit)))throw new DomainException("Purchase_ConfirmationConflict");
        RequestFingerprint=requestFingerprint;
        PurchasedOn = purchasedOn?.Date;
        Notes = (notes ?? "").Trim();
        if (Notes.Length > 500) throw new DomainException("Enter purchase notes of up to 500 characters.");
        _ = Total; // Validate the combined amount before stock changes.
        if (initialAmountPaid is { } paid && (paid < 0 || paid > Total || paid != Money.Round2(paid)))
            throw new DomainException("Amount paid must be between zero and the purchase total, with up to two decimals.");
        if (!SupermarketPos.Domain.Pricing.Payment.RecordedMethods.Contains(paymentMethod)) throw new DomainException("Select a payment method.");
        InitialAmountPaid = initialAmountPaid;
        InitialPaymentMethod = paymentMethod;
    }

    public void RecordBalancePayment(decimal amount)
    {
        if (amount <= 0 || amount != Money.Round2(amount) || amount > Outstanding)
            throw new DomainException("The payment must be positive and cannot exceed the outstanding balance.");
        BalancePaid += amount;
        BalanceRevision++;
    }
}

public sealed class PurchaseLine
{
    public int Id { get; private set; }
    public int PurchaseId { get; private set; }
    public int ProductId { get; private set; }
    public string ProductName { get; private set; } = string.Empty;
    public string Barcode { get; private set; } = string.Empty;
    public Unit Unit { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitCost { get; private set; }
    public string UnitCode { get; private set; } = "";
    public string? StockLocation{get;private set;}
    public void SetLocation(string? location)=>StockLocation=location;
    public int PackageSize { get; private set; } = 1;
    public decimal DisplayQuantity => Quantity/PackageSize;
    public decimal DisplayUnitCost => UnitCost*PackageSize;
    public int? BatchId { get; private set; }
    public string? BatchLot { get; private set; }
    public DateOnly? BatchExpiry { get; private set; }
    public decimal Total => Money.Round2(Quantity * UnitCost);

    private PurchaseLine() { }

    public PurchaseLine(Product product, decimal quantity, decimal unitCost,bool packaged=false,string? medicineUnit=null)
    {
        if (quantity <= 0 ||
            (product.Unit == Unit.Piece ? quantity != decimal.Truncate(quantity) : quantity != Math.Round(quantity, 3)))
            throw new DomainException("Enter a positive whole quantity for pieces or up to three decimals for kilograms.");
        if (unitCost < 0 || unitCost != Money.Round2(unitCost))
            throw new DomainException("Enter a nonnegative unit cost with up to two decimals.");

        ProductId = product.Id;
        ProductName = product.Name;
        Barcode = product.Barcode;
        Unit = product.Unit;
        if(packaged&&(product.UnitsPerPackage<=1||product.Unit!=Unit.Piece||quantity!=decimal.Truncate(quantity)||product.TrackSerial))throw new DomainException("Measure_InvalidPackage");
        PackageSize=medicineUnit is{} selected?product.SellingUnitSize(selected):packaged?product.UnitsPerPackage:1;Quantity = checked(quantity*PackageSize);
        UnitCost = unitCost/PackageSize;
        UnitCode=medicineUnit??(packaged?product.PurchaseUnit:product.Unit==Common.Unit.Kilogram?"kg":product.Unit==Common.Unit.Litre?"litre":product.SellingUnit);
        _ = Total;
    }

    public void SetBatch(InventoryBatch batch)
    {
        BatchId = batch.Id;
        BatchLot = batch.Lot;
        BatchExpiry = batch.ExpiryDate;
    }
}
