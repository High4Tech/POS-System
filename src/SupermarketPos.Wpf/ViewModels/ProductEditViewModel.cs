using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Wpf.Localization;

namespace SupermarketPos.Wpf.ViewModels;

/// <summary>Editable form state for creating or updating a product.</summary>
public sealed partial class ProductEditViewModel : ObservableObject
{
    [ObservableProperty] private bool _isPharmacy;
    [ObservableProperty] private bool _showProductImages;
    public int? Id { get; private set; }
    private string? _internalBarcode;
    public string BarcodeForSave=>string.IsNullOrWhiteSpace(Barcode)&&_internalBarcode is not null?_internalBarcode:Barcode;

    [ObservableProperty] private string _barcode = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _brand = "";
    [ObservableProperty] private string _model = "";
    [ObservableProperty] private string _sku = "";
    [ObservableProperty] private string _price = string.Empty;       // KDV-inclusive
    [ObservableProperty] private string _purchaseCost = "";
    [ObservableProperty] private string _vatPercent = "20";
    [ObservableProperty] private bool _isKilogram;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(UsesPackaging))] private string _sellingMethod="piece";
    [ObservableProperty] private string _unitsPerPackage="1";
    [ObservableProperty] private string _purchaseUnit="box";
    [ObservableProperty] private string _packageBarcode="";
    [ObservableProperty] private string _packageCost="";
    [ObservableProperty] private string _packagePrice="";
    [ObservableProperty] private string _minimumStock="";
    public bool UsesPackaging=>SellingMethod=="pack";
    public bool LooseEnabled{get;set;}
    public bool PackagesEnabled{get;set;}
    public BusinessFormChoice[] SellingMethods=>new[]{"piece","weight","volume","pack"}.Where(c=>c==SellingMethod||c=="piece"||(c=="pack"?PackagesEnabled:LooseEnabled)).Select(c=>new BusinessFormChoice(c,LocalizationManager.Instance["Measure_"+c])).ToArray();
    public BusinessFormChoice[] PurchaseUnits=>new[]{"box","carton","pack"}.Select(c=>new BusinessFormChoice(c,LocalizationManager.Instance["Measure_"+c])).ToArray();
    partial void OnSellingMethodChanged(string value){IsKilogram=value=="weight";OnPropertyChanged(nameof(SellingMethods));NotifySaleUnits();}
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private string _stock = string.Empty;
    [ObservableProperty] private int _categoryId;
    [ObservableProperty] private int _supplierId;
    [ObservableProperty] private string? _imagePath;
    [ObservableProperty] private bool _trackSerial;
    [ObservableProperty] private string _warrantyMonths = "0";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAssignBatch), nameof(CanRestockNormally), nameof(IsTrackedExisting))]
    private bool _trackExpiry;
    [ObservableProperty] private string _batchLot = "";
    [ObservableProperty] private DateTime? _batchExpiry;
    [ObservableProperty] private string _batchQuantity = "";
    [ObservableProperty] private bool _hasExistingBatches;
    [ObservableProperty] private decimal _unassignedStock;

    [ObservableProperty]private IReadOnlyList<string> _retailBrands=[];
    public ObservableCollection<Category> Categories { get; } = new();
    public ObservableCollection<SupplierOption> Suppliers { get; } = new();

    public bool IsNew => Id is null;
    public bool CanAssignBatch => TrackExpiry && !IsNew && UnassignedStock > 0m;
    public bool CanRestockNormally => !TrackExpiry && !IsNew && (!IsPharmacy || StockTracked);
    public bool IsTrackedExisting => TrackExpiry && !IsNew;
    partial void OnUnassignedStockChanged(decimal value) => OnPropertyChanged(nameof(CanAssignBatch));

    public void LoadNew(int defaultCategoryId, decimal defaultVat)
    {
        LoadPharmacy(null);
        Id = null;
        OnPropertyChanged(nameof(IsNew));
        _internalBarcode=null;Barcode = Name = Price = Stock = PurchaseCost = string.Empty;
        VatPercent = (defaultVat * 100m).ToString("0.####", CultureInfo.InvariantCulture);
        IsKilogram = false;
        SellingMethod="piece";UnitsPerPackage="1";PurchaseUnit="box";PackageBarcode=PackageCost=PackagePrice=MinimumStock="";
        IsActive = true;
        CategoryId = defaultCategoryId;
        SupplierId = 0;
        ImagePath = null;
        TrackExpiry = HasExistingBatches = false;
        TrackSerial = false;
        Brand=Model=Sku="";
        WarrantyMonths = "0";
        BatchLot = BatchQuantity = "";
        BatchExpiry = null;
        UnassignedStock = 0m;
        MedicinePackStrips="";NotifyBatchOptions();DefaultSellingUnit=MedicineUnit;OpeningStockUnit=MedicineUnit;NotifySaleUnits();
    }

    public void Load(Product p)
    {
        Brand=p.Brand;Model=p.Model;Sku=p.Sku;
        LoadPharmacy(p);
        Id = p.Id;
        OnPropertyChanged(nameof(IsNew));
        _internalBarcode=p.Barcode.StartsWith("INTERNAL-",StringComparison.Ordinal)?p.Barcode:null;
        Barcode = _internalBarcode is null?p.Barcode:"";
        Name = p.Name;
        Price = IsPharmacy&&p.UnitPrice==0?"":p.UnitPrice.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        PurchaseCost = p.PurchaseCost?.ToString("0.00", CultureInfo.InvariantCulture) ?? "";
        VatPercent = (p.VatRate * 100m).ToString("0.############################", CultureInfo.InvariantCulture);
        IsKilogram = p.Unit == Domain.Common.Unit.Kilogram;
        SellingMethod=p.Unit==Domain.Common.Unit.Kilogram?"weight":p.Unit==Domain.Common.Unit.Litre?"volume":p.UnitsPerPackage>1?"pack":"piece";
        UnitsPerPackage=p.UnitsPerPackage.ToString();PurchaseUnit=p.PurchaseUnit;PackageBarcode=p.PackageBarcode??"";PackageCost=p.PackagePurchaseCost?.ToString(CultureInfo.InvariantCulture)??"";PackagePrice=p.PackageSellingPrice?.ToString(CultureInfo.InvariantCulture)??"";MinimumStock=p.MinimumStock?.ToString(CultureInfo.InvariantCulture)??"";
        IsActive = p.IsActive;
        Stock = p.StockQuantity.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        CategoryId = p.CategoryId;
        SupplierId = p.SupplierId ?? 0;
        ImagePath = p.ImagePath;
        TrackExpiry = p.TrackExpiry;
        TrackSerial = p.TrackSerial;
        WarrantyMonths = p.WarrantyMonths.ToString(CultureInfo.InvariantCulture);
        HasExistingBatches = p.Batches.Count > 0;
        BatchLot = BatchQuantity = "";
        BatchExpiry = null;
        UnassignedStock = p.UnassignedStock;
        MedicinePackStrips=UsesStrips&&p.UnitsPerPackage>1?StripsPerPack:"";NotifyBatchOptions();DefaultSellingUnit=p.DefaultSellingUnit;OpeningStockUnit=MedicineUnit;NotifySaleUnits();
    }

    private void NotifyBatchOptions()
    {
        OnPropertyChanged(nameof(SellingMethods));
        OnPropertyChanged(nameof(CanAssignBatch));
        OnPropertyChanged(nameof(CanRestockNormally));
        OnPropertyChanged(nameof(IsTrackedExisting));
    }

    [RelayCommand]
    private void BrowseImage()
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationManager.Instance["Products_ChooseImage"],
            Filter = "PNG, JPG, BMP, GIF|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
        };
        if (dialog.ShowDialog() == true)
            ImagePath = dialog.FileName;
    }

    [RelayCommand]
    private void ClearImage() => ImagePath = null;
}

public sealed record SupplierOption(int Id, string Name);
public sealed record BusinessFormChoice(string Code,string Name);
