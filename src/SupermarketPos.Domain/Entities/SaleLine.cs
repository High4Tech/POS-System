using SupermarketPos.Domain.Common;

namespace SupermarketPos.Domain.Entities;

/// <summary>
/// An immutable snapshot of one sold line, frozen at checkout time. Prices/names are copied so
/// later product edits never rewrite history. <see cref="LineGross"/> is the final charged amount
/// (after line- and cart-level discounts), KDV-inclusive.
/// </summary>
public sealed class SaleLine
{
    public int Id { get; private set; }
    public int SaleId { get; private set; }
    public int ProductId { get; private set; }
    public bool StockUntracked{get;private set;}
    public bool PrescriptionChecked{get;private set;}
    public string? DispensingReference{get;private set;}
    public string? StockLocation{get;private set;}
    public void SetLocation(string? location)=>StockLocation=location;
    public void SetDispensing(bool untracked,bool prescription,string? reference){StockUntracked=untracked;PrescriptionChecked=prescription;DispensingReference=reference;}
    public string Barcode { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public decimal UnitPrice { get; private set; }            // KDV-inclusive shelf price at sale time
    public decimal? UnitCost { get; private set; }
    public decimal VatRate { get; private set; }
    public decimal Quantity { get; private set; }
    public string UnitCode { get; private set; } = "";
    public long BaseQuantity { get; private set; }
    public void SetMeasurement(string unit,long baseQuantity){UnitCode=unit;BaseQuantity=baseQuantity;}
    public int? BatchId { get; private set; }
    public string? BatchLot { get; private set; }
    public DateOnly? BatchExpiry { get; private set; }
    public string? SerialNumber { get; private set; }
    public string? Imei1 { get; private set; }
    public string? Imei2 { get; private set; }
    public int WarrantyMonths { get; private set; }
    public DateTime? WarrantyUntil { get; private set; }
    public string? DiscountDescription { get; private set; }
    public decimal LineGross { get; private set; }            // final charged gross (incl. VAT)

    private SaleLine() { } // EF

    public SaleLine(int productId, string barcode, string name, decimal unitPrice, decimal vatRate,
                    decimal quantity, string? discountDescription, decimal lineGross, decimal? unitCost = null,
                    InventoryBatch? batch = null, string? serialNumber = null)
    {
        ProductId = productId;
        Barcode = barcode;
        Name = name;
        UnitPrice = Money.Round2(unitPrice);
        UnitCost = unitCost;
        VatRate = vatRate;
        Quantity = quantity;
        DiscountDescription = discountDescription;
        LineGross = Money.Round2(lineGross);
        BatchId = batch?.Id;
        BatchLot = batch?.Lot;
        BatchExpiry = batch?.ExpiryDate;
        SerialNumber = serialNumber;
    }

    public decimal NetAmount => Money.Round2(LineGross / (1 + VatRate));
    public decimal VatAmount => Money.Round2(LineGross - NetAmount);

    public void SetWarranty(int months, DateTime soldOn, SerializedUnit? unit)
    {
        WarrantyMonths = months;
        WarrantyUntil = months > 0 ? soldOn.Date.AddMonths(months) : null;
        Imei1 = unit?.Imei1;
        Imei2 = unit?.Imei2;
    }
}
