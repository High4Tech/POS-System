using SupermarketPos.Domain.Common;

namespace SupermarketPos.Domain.Entities;

/// <summary>
/// A sellable product. <see cref="UnitPrice"/> is the KDV-inclusive shelf price (Turkish retail convention).
/// The entity owns its invariants: stock can never go negative, price/VAT/quantity must be sane.
/// </summary>
public sealed class Product
{
    public string Kind { get; private set; } = "labeled";
    public string ServiceType { get; private set; } = "SERVICE";
    public int? ServiceProviderId { get; private set; }
    public int? ServicePackageId { get; private set; }
    public string CatalogKey { get; private set; } = "";
    public bool QuickEntry { get; private set; }
    public bool IsFinancialService => Kind=="digital"&&ServiceType!="SERVICE";
    public void SetRetailKind(string kind)
    {
        if(kind is not ("labeled" or "loose" or "digital")||kind=="digital"&&(StockTracked||TrackExpiry||TrackSerial))throw new DomainException("Retail_InvalidKind");
        Kind=kind;
        if(kind!="digital"){ServiceType="SERVICE";ServiceProviderId=ServicePackageId=null;}
    }
    public void ConfigureService(string type,int? providerId=null,int? packageId=null)
    {
        if(Kind!="digital"||type is not ("SERVICE" or "CASHIN" or "CASHOUT" or "TRANSFER" or "LOAD" or "PACKAGE")||providerId<=0||packageId<=0||packageId is not null&&type!="PACKAGE")throw new DomainException("Service_Invalid");
        ServiceType=type;ServiceProviderId=providerId;ServicePackageId=packageId;
    }
    public void SetCatalogKey(string key){if(key.Length>120||key.Any(char.IsControl))throw new DomainException("Validation_Text");CatalogKey=key;}
    public void MarkQuickEntry(){if(StockTracked)throw new DomainException("Retail_InvalidKind");QuickEntry=true;}
    public void CompleteQuickEntry()=>QuickEntry=false;
    private readonly List<InventoryBatch> _batches = [];
    public bool TrackExpiry { get; private set; }
    public bool TrackSerial { get; private set; }
    public int WarrantyMonths { get; private set; }
    public string Brand { get; private set; } = "";
    public string Model { get; private set; } = "";
    public string Sku { get; private set; } = "";
    public void SetRetailDetails(string? brand,string? model,string? sku)
    {
        var values=new[]{brand?.Trim()??"",model?.Trim()??"",sku?.Trim()??""};
        if(values.Any(value=>value.Length>120||value.Any(char.IsControl)))throw new DomainException("Validation_Text");
        Brand=values[0];Model=values[1];Sku=values[2];
    }
    public int StockRevision { get; private set; }
    public void RecordLocationChange()=>StockRevision++;
    public IReadOnlyList<InventoryBatch> Batches => _batches;
    public decimal UnassignedStock => StockQuantity - _batches.Sum(batch => batch.Quantity);
    public decimal AvailableForSale(DateOnly today)=>TrackExpiry?_batches.Where(b=>b.ExpiryDate>=today&&b.HoldReason.Length==0).Sum(b=>b.Quantity):StockQuantity;
    public int Id { get; private set; }
    public int? MedicineReferenceId{get;private set;}
    public int? DefaultLocationId{get;private set;}
    public void SetDefaultLocation(int? id){if(id<=0)throw new DomainException("Pharmacy_SelectStockLocation");DefaultLocationId=id;}
    public string MedicineGeneric{get;private set;}="";
    public string MedicineStrength{get;private set;}="";
    public string MedicineForm{get;private set;}="";
    public string MedicineManufacturer{get;private set;}="";
    public void SetMedicineDetails(string generic,string strength,string form,string manufacturer)
    {
        var values=new[]{generic.Trim(),strength.Trim(),form.Trim(),manufacturer.Trim()};if(values.Any(v=>v.Length>500||v.Any(char.IsControl)))throw new DomainException("Pharmacy_InvalidProduct");
        MedicineGeneric=values[0];MedicineStrength=values[1];MedicineForm=values[2];MedicineManufacturer=values[3];
    }
    public bool StockTracked{get;private set;}=true;
    public string SellingUnit{get;private set;}="piece";
    public bool SellingUnitConfigured{get;private set;}=true;
    public void LeaveSellingUnitUnspecified(){if(StockTracked||UnitsPerStrip>1||UnitsPerPackage>1)throw new DomainException("Pharmacy_MissingUnit");SellingUnitConfigured=false;}
    public string DefaultSellingUnitCode{get;private set;}="";
    public string DefaultSellingUnit=>DefaultSellingUnitCode.Length==0?SellingUnit:DefaultSellingUnitCode;
    public void SetDefaultSellingUnit(string unit){_=SellingUnitSize(unit);DefaultSellingUnitCode=unit==SellingUnit?"":unit;}
    public decimal? SellingPriceFor(string unit){_=SellingUnitSize(unit);return unit==SellingUnit?UnitPrice:unit=="strip"?StripSellingPrice:PackageSellingPrice;}
    public int UnitsPerStrip{get;private set;}=1;
    public decimal? StripSellingPrice{get;private set;}
    public bool DispensingRulesReviewed{get;private set;}
    public bool RequiresPrescription{get;private set;}
    public bool RequiresDispensingReference{get;private set;}
    public void ReviewDispensingRules(bool prescription,bool reference)
    {DispensingRulesReviewed=true;RequiresPrescription=prescription;RequiresDispensingReference=reference;StockRevision++;}
    public void MarkStockUntracked()
    {
        if(Id!=0||BaseStockQuantity!=0||_batches.Count>0||TrackSerial)throw new DomainException("Pharmacy_CannotUntrack");
        StockTracked=false;
    }
    public void VerifyOpeningStock(decimal quantity)
    {
        if(TrackSerial||TrackExpiry&&(quantity!=0||_batches.Count>0))throw new DomainException("Pharmacy_TraceabilityRequired");
        if(quantity<0)throw new DomainException("Stock cannot be negative.");var exact=Domain.Pricing.MeasuredGoods.BaseQuantity(quantity,Unit);BaseStockQuantity=exact;StockTracked=true;StockRevision++;
    }
    public void ConfigureMedicineUnits(string unit,int unitsPerStrip=1,decimal? stripPrice=null)
    {
        if(Unit!=Unit.Piece||TrackSerial||unit is not ("piece" or "tablet" or "capsule" or "bottle" or "tube" or "sachet" or "vial" or "other")||unitsPerStrip<1||unitsPerStrip>1000000||
            unitsPerStrip>1&&unit is not ("tablet" or "capsule")||stripPrice is{} price&&(price<=0||price!=Money.Round2(price))||unitsPerStrip>1&&UnitsPerPackage>1&&UnitsPerPackage%unitsPerStrip!=0)
            throw new DomainException("Pharmacy_InvalidUnits");
        SellingUnit=unit;SellingUnitConfigured=true;UnitsPerStrip=unitsPerStrip;StripSellingPrice=stripPrice;StockRevision++;
    }
    public (int Size,decimal Price) ResolveSellingUnit(string unit)
    {
        if(unit==SellingUnit)return (1,UnitPrice);
        if(unit=="strip"&&UnitsPerStrip>1&&StripSellingPrice is{} strip)return(UnitsPerStrip,strip);
        if(unit==PurchaseUnit&&UnitsPerPackage>1&&PackageSellingPrice is{} pack)return(UnitsPerPackage,pack);
        throw new DomainException("Pharmacy_ConfirmUnitPrice");
    }
    public int SellingUnitSize(string unit)=>unit==SellingUnit?1:unit=="strip"&&UnitsPerStrip>1?UnitsPerStrip:unit==PurchaseUnit&&UnitsPerPackage>1?UnitsPerPackage:throw new DomainException("Pharmacy_InvalidUnits");
    public void SetSellingUnitPrice(string unit,decimal price)
    {
        if(price<=0||price!=Money.Round2(price))throw new DomainException("Pharmacy_ConfirmUnitPrice");
        if(unit==SellingUnit)SetPrice(price);
        else if(unit=="strip"&&UnitsPerStrip>1)StripSellingPrice=price;
        else if(unit==PurchaseUnit&&UnitsPerPackage>1)PackageSellingPrice=price;
        else throw new DomainException("Pharmacy_InvalidUnits");
    }
    public void LinkMedicine(int id){if(id<=0)throw new DomainException("Select a medicine reference.");MedicineReferenceId=id;}
    public string Barcode { get; private set; } = string.Empty;   // unique
    public string Name { get; private set; } = string.Empty;
    public decimal UnitPrice { get; private set; }                 // KDV-inclusive shelf price (TRY)
    public decimal VatRate { get; private set; }                   // e.g. 0.01, 0.10, 0.20
    public Unit Unit { get; private set; }
    public long BaseStockQuantity { get; private set; }
    public decimal StockQuantity => Domain.Pricing.MeasuredGoods.Quantity(BaseStockQuantity,Unit);
    public int UnitsPerPackage { get; private set; } = 1;
    public string PurchaseUnit { get; private set; } = "box";
    public string? PackageBarcode { get; private set; }
    public decimal? PackagePurchaseCost { get; private set; }
    public decimal? PackageSellingPrice { get; private set; }
    public decimal? MinimumStock { get; private set; }
    public int CategoryId { get; private set; }
    public Category? Category { get; private set; }
    public string? ImagePath { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Product() { } // EF

    public Product(string barcode, string name, decimal unitPrice, decimal vatRate,
                   Unit unit, decimal stockQuantity, int categoryId)
    {
        Unit=unit;
        SetBarcode(barcode);
        SetName(name);
        SetPrice(unitPrice);
        SetVatRate(vatRate);
        Unit = unit;
        if (stockQuantity < 0) throw new DomainException("Stock cannot start negative.");
        BaseStockQuantity = Domain.Pricing.MeasuredGoods.BaseQuantity(stockQuantity,unit);
        CategoryId = categoryId;
    }

    public void SetSerialTracking(bool enabled, int warrantyMonths)
    {
        if (warrantyMonths is < 0 or > 120) throw new DomainException("Warranty must be between 0 and 120 months.");
        if (enabled && (Unit != Unit.Piece || TrackExpiry))
            throw new DomainException("Serial tracking is available for piece products without expiry batches.");
        if(enabled&&UnitsPerPackage>1)throw new DomainException("Measure_InvalidPackage");
        TrackSerial = enabled;
        WarrantyMonths = warrantyMonths;
    }

    public void DecreaseStock(decimal qty)
    {
        if(!StockTracked)throw new DomainException("Pharmacy_VerifyStockFirst");
        if (TrackExpiry) throw new DomainException("Select a batch before selling expiry-tracked stock.");
        ValidateStockDecrease(qty);
        BaseStockQuantity -= Domain.Pricing.MeasuredGoods.BaseQuantity(qty,Unit);
    }

    private void ValidateStockDecrease(decimal qty)
    {
        if (qty <= 0) throw new DomainException("Quantity must be positive.");
        if (qty > StockQuantity) throw new InsufficientStockException(Name, StockQuantity, qty);
    }

    public void Restock(decimal qty)
    {
        if(!StockTracked)throw new DomainException("Pharmacy_VerifyStockFirst");
        if (TrackExpiry) throw new DomainException("Record the batch and expiry before receiving stock.");
        if (qty <= 0) throw new DomainException("Restock quantity must be positive.");
        BaseStockQuantity = checked(BaseStockQuantity+Domain.Pricing.MeasuredGoods.BaseQuantity(qty,Unit));
    }

    /// <summary>Absolute stock correction used for admin adjustments (e.g. shrinkage, recount).</summary>
    public void SetStock(decimal qty)
    {
        if(!StockTracked)throw new DomainException("Pharmacy_VerifyStockFirst");
        if (TrackExpiry && qty != StockQuantity)
            throw new DomainException("Edit stock through its batches.");
        if (qty < 0) throw new DomainException("Stock cannot be negative.");
        BaseStockQuantity = Domain.Pricing.MeasuredGoods.BaseQuantity(qty,Unit);
    }

    public void SetExpiryTracking(bool enabled)
    {
        if(enabled&&!StockTracked&&(BaseStockQuantity!=0||_batches.Count>0))throw new DomainException("Pharmacy_VerifyStockFirst");
        if (TrackExpiry == enabled) return;
        if (!enabled && _batches.Count > 0)
            throw new DomainException("Expiry tracking cannot be disabled after batches have been recorded.");
        TrackExpiry = enabled;
        StockRevision++;
    }

    public InventoryBatch RecordBatch(string lot, DateOnly expiry, decimal quantity, int? supplier, DateTime now, bool fromExistingStock = false)
    {
        if(!StockTracked)throw new DomainException("Pharmacy_VerifyStockFirst");
        if (!TrackExpiry) throw new DomainException("Enable expiry tracking before recording a batch.");
        lot = lot.Trim();
        if (!Gs1Barcode.IsIdentifier(lot) || expiry == default)
            throw new DomainException("Enter a batch number of up to 20 GS1 characters and a valid expiry date.");
        ValidateBatchQuantity(quantity);
        if (fromExistingStock && quantity > UnassignedStock)
            throw new InsufficientStockException(Name, UnassignedStock, quantity);

        var batch = _batches.SingleOrDefault(item => item.Lot == lot);
        if (batch is null)
        {
            batch = new InventoryBatch(lot, expiry, quantity, supplier, now,Unit);
            _batches.Add(batch);
        }
        else
        {
            if (batch.ExpiryDate != expiry) throw new DomainException($"Expiry does not match batch {lot}.");
            batch.Add(quantity, now);
        }
        if (!fromExistingStock) BaseStockQuantity = checked(BaseStockQuantity+Domain.Pricing.MeasuredGoods.BaseQuantity(quantity,Unit));
        StockRevision++;
        return batch;
    }

    public InventoryBatch SellBatch(int batchId, DateOnly? expiry, decimal quantity, DateTime now)
    {
        if (!TrackExpiry) throw new DomainException("This product is no longer batch tracked.");
        var batch = _batches.SingleOrDefault(item => item.Id == batchId)
            ?? throw new DomainException($"Batch not found for {Name}.");
        if (batch.ExpiryDate != expiry) throw new DomainException($"Expiry does not match batch {batch.Lot}.");
        ValidateStockDecrease(quantity);
        ValidateBatchQuantity(quantity);
        batch.Take(quantity, now);
        BaseStockQuantity -= Domain.Pricing.MeasuredGoods.BaseQuantity(quantity,Unit);
        StockRevision++;
        return batch;
    }

    private void ValidateBatchQuantity(decimal quantity)
    {
        if (quantity <= 0 || quantity != Math.Round(quantity, Unit == Unit.Piece ? 0 : 3))
            throw new DomainException("Enter a positive whole quantity for pieces or up to three decimals for kilograms.");
    }

    public void SetBarcode(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {barcode="INTERNAL-"+Guid.NewGuid().ToString("N");}
        if(barcode.Trim().Length>64||barcode.Any(char.IsControl)||barcode.StartsWith("POSLOC:",StringComparison.OrdinalIgnoreCase))throw new DomainException("Pharmacy_InvalidBarcode");
        Barcode = barcode.Trim();
    }

    public void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Product name cannot be empty.");
        Name = name.Trim();
    }

    public void SetPrice(decimal unitPrice)
    {
        if (unitPrice < 0) throw new DomainException("Price cannot be negative.");
        if(Unit!=Unit.Piece&&unitPrice<=0)throw new DomainException("Measure_InvalidAmount");
        UnitPrice = Money.Round2(unitPrice);
    }

    public void SetVatRate(decimal vatRate)
    {
        if (vatRate < 0 || vatRate > 1)
            throw new DomainException("VAT rate must be between 0 and 1 (e.g. 0.20 for 20%).");
        VatRate = vatRate;
    }

    public void SetUnit(Unit unit)
    {
        if(!Enum.IsDefined(unit))throw new DomainException("Measure_InvalidQuantity");
        if(unit!=Unit&&BaseStockQuantity!=0)throw new DomainException("Measure_ClearStockFirst");
        if (unit != Unit && _batches.Count > 0)
            throw new DomainException("The unit cannot be changed after batches have been recorded.");
        Unit = unit;
    }

    public void ConfigurePackaging(string purchaseUnit,int units,string? barcode,decimal? cost,decimal? price,decimal? minimum)
    {
        if(units<1||units>1000000||purchaseUnit is not ("box" or "carton" or "pack"))throw new DomainException("Measure_InvalidPackage");
        if(units>1&&(Unit!=Unit.Piece||TrackSerial))throw new DomainException("Measure_InvalidPackage");
        barcode=string.IsNullOrWhiteSpace(barcode)?null:barcode.Trim();
        if(barcode?.Length>64||barcode==Barcode)throw new DomainException("Measure_InvalidPackage");
        if(cost is{} c&&(c<0||c!=Money.Round2(c))||price is{} p&&(p<=0||p!=Money.Round2(p))||minimum<0)throw new DomainException("Measure_InvalidPackage");
        UnitsPerPackage=units;PurchaseUnit=purchaseUnit;PackageBarcode=barcode;PackagePurchaseCost=cost;PackageSellingPrice=price;MinimumStock=minimum;
        if(cost.HasValue&&units>1)PurchaseCost=cost.Value/units; // Exact per-piece cost; currency rounds at line totals.
    }

    public void SetCategory(int categoryId) => CategoryId = categoryId;
    public void SetActive(bool active) => IsActive = active;

    public decimal? PurchaseCost { get; private set; }
    public int? SupplierId { get; private set; }

    public void SetSupplier(int? supplierId) => SupplierId = supplierId;

    public void SetPurchaseCost(decimal? cost)
    {
        if (cost is { } value && (value < 0 || value != Money.Round2(value)))
            throw new DomainException("Enter a nonnegative purchase cost with up to two decimals.");
        PurchaseCost = cost;
    }
    public void SetConvertedPurchaseCost(decimal cost)
    {
        if(cost<0)throw new DomainException("Measure_InvalidPackage");PurchaseCost=cost;
    }

    public void SetImagePath(string? imagePath) =>
        ImagePath = string.IsNullOrWhiteSpace(imagePath) ? null : imagePath.Trim();
}
