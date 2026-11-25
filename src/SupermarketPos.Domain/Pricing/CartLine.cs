using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;

namespace SupermarketPos.Domain.Pricing;

/// <summary>
/// A line in an in-progress sale: a product, a quantity, and an optional per-line discount.
/// All amounts are KDV-inclusive (gross), matching the shelf price.
/// </summary>
public sealed class CartLine
{
    public Product Product { get; private set; }
    public int? BatchId { get; }
    public string? BatchLot { get; }
    public DateOnly? BatchExpiry { get; }
    public string? SerialNumber { get; }
    public string? DispensingReference{get;}
    public bool PrescriptionChecked{get;}
    public string? MedicineUnit{get;private set;}
    public int? LocationId{get;}
    public decimal Quantity { get; private set; }
    public Discount? LineDiscount { get; private set; }
    private decimal? _unitPriceOverride;
    public bool IsPackage { get; private set; }
    public int PackageSize { get; private set; }
    private decimal? _packagePrice;
    public bool HasPriceOverride=>_unitPriceOverride.HasValue;
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
        if(medicineUnit is not null){PackageSize=product.SellingUnitSize(medicineUnit);IsPackage=PackageSize>1;_packagePrice=product.SellingPriceFor(medicineUnit)??0;}
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
        ValidateQuantity(quantity,PackageSize,IsPackage);
        Quantity=quantity;
    }

    private void ValidateQuantity(decimal quantity,int size,bool package)
    {
        if (quantity <= 0) throw new DomainException("Quantity must be positive.");
        if (SerialNumber is not null && quantity != (package?size:1m))
            throw new DomainException("A serialized line must contain one item.");
        if (Product.Unit == Unit.Piece && quantity != Math.Truncate(quantity))
            throw new DomainException("Piece items must have a whole-number quantity.");
        _=MeasuredGoods.BaseQuantity(quantity,Product.Unit);
        if(package&&quantity%size!=0)throw new DomainException("Measure_InvalidPackage");
    }

    public void SetSellingUnit(string unit)
    {
        var size=Product.SellingUnitSize(unit);var price=Product.SellingPriceFor(unit)??0;var quantity=checked(DisplayQuantity*size);
        if(SerialNumber is not null&&size!=PackageSize)throw new DomainException("Measure_ChangedUnit");
        ValidateQuantity(quantity,size,size>1);_=Money.Round2(price*DisplayQuantity);
        MedicineUnit=unit;PackageSize=size;IsPackage=size>1;_packagePrice=price;_unitPriceOverride=null;Quantity=quantity;
    }

    public void BindSavedProduct(Product product)
    {
        if(Product.Id!=0||product.Id<=0||BatchId is not null||SerialNumber is not null||product.Unit!=Product.Unit||product.SellingUnitSize(UnitCode)!=PackageSize)throw new DomainException("Measure_ChangedUnit");
        Product=product;
    }

    public void Increment(decimal by)
    {
        if (by <= 0) throw new DomainException("Increment must be positive.");
        SetQuantity(Quantity + by);
    }

    public void ApplyDiscount(Discount? discount) => LineDiscount = discount;
}
