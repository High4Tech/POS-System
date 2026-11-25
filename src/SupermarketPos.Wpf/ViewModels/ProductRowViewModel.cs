using CommunityToolkit.Mvvm.ComponentModel;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Domain.Common;

namespace SupermarketPos.Wpf.ViewModels;

/// <summary>Read-only row in the products grid; flags low stock for highlighting.</summary>
public sealed partial class ProductRowViewModel : ObservableObject
{
    public Product Product { get; }
    public MedicineMatch? Medicine{get;}
    private readonly decimal _lowStockThreshold;
    [ObservableProperty] private bool _isSelected;

    public ProductRowViewModel(Product product, decimal lowStockThreshold,MedicineMatch? medicine=null)
    {
        Product = product;
        Medicine=medicine;
        _lowStockThreshold = lowStockThreshold;
        Details=medicine?.Details??"";Locations=medicine?.Locations??"";
        Manufacturer=medicine?.Manufacturer??"";Formulation=medicine?.Formulation??"";
    }
    public string Details{get;}
    public string Locations{get;}
    public string Manufacturer{get;}
    public string Formulation{get;}

    public int Id => Product.Id;
    public string Barcode => Product.Barcode.StartsWith("INTERNAL-",StringComparison.Ordinal)?"":Product.Barcode;
    public string Name => Product.Name;
    public string CategoryName => Product.Category?.Name ?? "-";
    public string PriceText => Medicine is not null && Product.UnitPrice<=0 ? "—" : MoneyText.Format(Product.UnitPrice);
    public string VatText => $"{Product.VatRate * 100:0.####}%";
    public string UnitText => !Product.SellingUnitConfigured ? "—" : LocalizationManager.Instance[Product.Unit==Domain.Common.Unit.Litre?"Measure_litre":Product.Unit==Domain.Common.Unit.Kilogram?"Unit_Kg":"Measure_"+Product.SellingUnit];
    public string StockText => !Product.StockTracked?LocalizationManager.Instance["Pharmacy_StockUntracked"]:Product.TrackExpiry?LocalizationManager.Instance.Format("Pharmacy_OnHandAvailable",Product.StockQuantity,Product.AvailableForSale(DateOnly.FromDateTime(DateTime.Today))):MoneyText.Qty(Product.StockQuantity, Product.Unit != Domain.Common.Unit.Piece);
    public bool IsLowStock => Product.StockTracked&&Product.AvailableForSale(DateOnly.FromDateTime(DateTime.Today)) <= (Product.MinimumStock??_lowStockThreshold);
    public bool NeedsSetup=>!Product.SellingUnitConfigured||Product.UnitPrice<=0||Product.PurchaseCost is null||!Product.StockTracked||Locations.Length==0&&Product.DefaultLocationId is null||Product.TrackExpiry&&Product.UnassignedStock>0||Product.UnitsPerStrip>1&&Product.StripSellingPrice is null||Product.UnitsPerPackage>1&&Product.PackageSellingPrice is null;
    public string SetupDetails=>string.Join(" · ",new[]{!Product.SellingUnitConfigured?"Pharmacy_MissingUnit":null,Product.UnitPrice<=0?"Pharmacy_MissingPrice":null,Product.PurchaseCost is null?"Pharmacy_MissingCost":null,!Product.StockTracked?"Pharmacy_UnverifiedStock":null,Locations.Length==0&&Product.DefaultLocationId is null?"Pharmacy_MissingRack":null,Product.TrackExpiry&&Product.UnassignedStock>0?"Pharmacy_MissingBatch":null,Product.UnitsPerStrip>1&&Product.StripSellingPrice is null||Product.UnitsPerPackage>1&&Product.PackageSellingPrice is null?"Pharmacy_ConfirmUnitPrice":null}.Where(k=>k is not null).Select(k=>LocalizationManager.Instance[k!]));
    public string StatusText => LocalizationManager.Instance[!Product.SellingUnitConfigured||!Product.StockTracked&&Product.UnitPrice<=0?"Pharmacy_NeedsSetup":!Product.StockTracked?"Pharmacy_StockUntracked":IsLowStock ? "Products_LowStock" : "Products_InStock"];
}
