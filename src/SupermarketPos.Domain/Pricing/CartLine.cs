using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;

namespace SupermarketPos.Domain.Pricing;

/// <summary>
/// A line in an in-progress sale: a product, a quantity, and an optional per-line discount.
/// All amounts are KDV-inclusive (gross), matching the shelf price.
/// </summary>
public sealed class CartLine
{
    public Product Product { get; }
    public int? BatchId { get; }
    public string? BatchLot { get; }
    public DateOnly? BatchExpiry { get; }
    public string? SerialNumber { get; }
    public string? DispensingReference{get;}
    public bool PrescriptionChecked{get;}
    public string? MedicineUnit{get;}
    public int? LocationId{get;}
    public decimal Quantity { get; private set; }
    public Discount? LineDiscount { get; private set; }
    private decimal? _unitPriceOverride;
    public bool IsPackage { get; }
    public int PackageSize { get; }
    private readonly decimal? _packagePrice;
    public decimal DisplayQuantity => IsPackage ? Quantity/PackageSize:Quantity;
    public string UnitCode => MedicineUnit??(IsPackage?Product.PurchaseUnit:Product.Unit==Unit.Kilogram?"kg":Product.Unit==Unit.Litre?"litre":Product.SellingUnit);

    public CartLine(Product product, decimal quantity, InventoryBatch? batch = null, string? serial = null,bool packaged=false,string? medicineUnit=null,bool prescriptionChecked=false,string? dispensingReference=null,int? locationId=null)
    {
        Product = product ?? throw new DomainException("Product is required.");
        IsPackage=packaged;PackageSize=packaged?product.UnitsPerPackage:1;
        if(packaged&&(PackageSize<=1||product.TrackSerial||product.Unit!=Unit.Piece))throw new DomainException("Measure_InvalidPackage");
        _packagePrice=packaged?product.PackageSellingPrice??Money.Round2(product.UnitPrice*PackageSize):null;
        MedicineUnit=medicineUnit;LocationId=locationId;PrescriptionChecked=prescriptionChecked;DispensingReference=string.IsNullOrWhiteSpace(dispensingReference)?null:dispensingReference.Trim();
        if(DispensingReference?.Length>150)throw new DomainException("Pharmacy_InvalidReference");
        if(medicineUnit is not null){var selected=product.ResolveSellingUnit(medicineUnit);PackageSize=selected.Size;IsPackage=PackageSize>1;_packagePrice=selected.Price;}
        if (batch is not null && (batch.Id == 0 || batch.ProductId != product.Id))
            throw new DomainException("Select a saved batch for this product.");
        if (serial is not null && !product.TrackSerial && (batch is null || !Gs1Barcode.IsIdentifier(serial)))
            throw new DomainException("A serial number requires a valid batch.");
        if (product.TrackSerial && serial is not null) serial = UnitIdentifiers.Create(serial).Serial;
        BatchId = batch?.Id;
        BatchLot = batch?.Lot;
        BatchExpiry = batch?.ExpiryDate;
        SerialNumber = serial;
        SetQuantity(quantity);
    }

    public decimal GrossUnitPrice => _unitPriceOverride ?? _packagePrice ?? Product.UnitPrice;
    public decimal GrossSubtotal => Money.Round2(GrossUnitPrice * DisplayQuantity);
    public decimal GrossTotal => LineDiscount?.Apply(GrossSubtotal) ?? GrossSubtotal;
    public decimal VatRate => Product.VatRate;

    public void SetUnitPrice(decimal unitPrice)
    {
        if (unitPrice < 0 || unitPrice != Money.Round2(unitPrice))
            throw new DomainException("Enter a nonnegative price with up to two decimal places.");
        if(Product.Unit!=Unit.Piece&&unitPrice<=0)throw new DomainException("Measure_InvalidAmount");
        _ = Money.Round2(unitPrice * Quantity);
        _unitPriceOverride = unitPrice;
    }

    public void SetQuantity(decimal quantity)
    {
        if (quantity <= 0) throw new DomainException("Quantity must be positive.");
        if (SerialNumber is not null && quantity != (IsPackage?PackageSize:1m))
            throw new DomainException("A serialized line must contain one item.");
        if (Product.Unit == Unit.Piece && quantity != Math.Truncate(quantity))
            throw new DomainException("Piece items must have a whole-number quantity.");
        _=MeasuredGoods.BaseQuantity(quantity,Product.Unit);
        if(IsPackage&&quantity%PackageSize!=0)throw new DomainException("Measure_InvalidPackage");
        Quantity = quantity;
    }

    public void Increment(decimal by)
    {
        if (by <= 0) throw new DomainException("Increment must be positive.");
        SetQuantity(Quantity + by);
    }

    public void ApplyDiscount(Discount? discount) => LineDiscount = discount;
}
