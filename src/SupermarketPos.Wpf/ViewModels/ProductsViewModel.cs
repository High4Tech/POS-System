using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Application.Services;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Scanning;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;

namespace SupermarketPos.Wpf.ViewModels;

public sealed partial class ProductsViewModel : ObservableObject
{
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowProductImages),nameof(ShowMedicineCards),nameof(ShowInventoryTable))] private bool _isPharmacy;
    public bool ShowProductImages=>!IsPharmacy;
    private readonly IProductRepository _products;
    private readonly ICategoryRepository _categories;
    private readonly ISettingsRepository _settings;
    private readonly IShelfLabelService _labels;
    private readonly IUnitOfWork _uow;
    private readonly PosStorage _storage;
    private readonly IInventoryService _inventory;
    private readonly IBarcodeService _barcodes;
    private readonly RegisterBarcodeLookup _lookup;
    private readonly ScannerController _scanner;
    private readonly ScannerFeedback _sound;
    private readonly IPurchaseService _purchases;
    private readonly BulkRecordService _bulk;
    private readonly ITradeLedger _ledger;
    private bool _readingCapture;
    private bool _inventoryActive;
    private bool _importDialogOpen;

    private decimal _lowStockThreshold = 5m;
    private decimal _defaultVat;
    private List<Category> _categoryList = new();
    private bool _categoryImageChanged;
    private string? _categoryImageSource;

    public ProductsViewModel(IProductRepository products, ICategoryRepository categories,
                             ISettingsRepository settings, IShelfLabelService labels, IUnitOfWork uow,
                             PosStorage storage, IInventoryService inventory, IBarcodeService barcodes,
                             ScannerController scanner, ScannerFeedback sound, IPurchaseService purchases, BulkRecordService bulk,
                             RegisterBarcodeLookup lookup, ITradeLedger ledger,IBusinessSetup businessSetup,SupermarketPos.Infrastructure.Business.MedicineCatalogService medicineCatalog,SupermarketPos.Infrastructure.Business.PharmacyInventory pharmacyInventory,SupermarketPos.Infrastructure.Business.StockLocationService stockLocations)
    {
        _products = products;
        _categories = categories;
        _settings = settings;
        _labels = labels;
        _uow = uow;
        _storage = storage;
        _inventory = inventory;
        _barcodes = barcodes;
        _lookup = lookup;
        _scanner = scanner;
        _sound = sound;
        _purchases = purchases;
        _bulk = bulk;
        _ledger = ledger;
        _businessSetup=businessSetup;
        _medicineCatalog=medicineCatalog;_pharmacyInventory=pharmacyInventory;_stockLocations=stockLocations;_inventoryPreferences=new(storage,"pharmacy-inventory-view.json");_medicineCards=_inventoryPreferences.Read()?.Cards??false;
        VisibleProducts = CollectionViewSource.GetDefaultView(Products);
        VisibleProducts.Filter = item => MatchesFilter((ProductRowViewModel)item);
    }

    public ObservableCollection<ProductRowViewModel> Products { get; } = new();
    private readonly IBusinessSetup _businessSetup;
    public ICollectionView VisibleProducts { get; }
    public ProductEditViewModel Editor { get; } = new();
    public int SelectedCount => Products.Count(row => row.IsSelected);
    public bool HasSelection => SelectedCount > 0;
    public bool HasBulkSelection => SelectedCount > 1;
    public bool? VisibleSelectionState => !HasSelection ? false :
        VisibleProducts.Cast<ProductRowViewModel>().All(row => row.IsSelected) ? true : null;
    public string SelectionText => LocalizationManager.Instance.Format("Bulk_Selected", SelectedCount);
    public IReadOnlyList<Category> BulkCategories =>
        new[] { new Category(LocalizationManager.Instance["Bulk_NoChange"]) }.Concat(_categoryList).ToArray();

    [ObservableProperty] private bool _lowStockOnly;
    [ObservableProperty] private bool _isEditorOpen;
    [ObservableProperty] private bool _isScannerEntry;
    [ObservableProperty] private bool _isPurchaseEntry;
    [ObservableProperty] private bool _isCaptureArmed;
    [ObservableProperty] private Product? _matchingProduct;
    [ObservableProperty] private string? _productError;
    public bool ProductSaved { get; private set; }
    public int? SavedProductId { get; private set; }
    [ObservableProperty] private string? _importMessage;
    [ObservableProperty] private string? _importError;
    [ObservableProperty] private string _restockAmount = string.Empty;
    [ObservableProperty] private string _filter = string.Empty;
    [ObservableProperty] private bool _isBulkEditorOpen;
    [ObservableProperty] private string _bulkPrice = string.Empty;
    [ObservableProperty] private string _bulkStock = string.Empty;
    [ObservableProperty] private string _bulkVatPercent = string.Empty;
    [ObservableProperty] private int _bulkCategoryId;
    [ObservableProperty] private bool _isCategoryEditorOpen;
    [ObservableProperty] private Category? _selectedCategory;
    [ObservableProperty] private string _categoryName = string.Empty;
    [ObservableProperty] private string _categoryColor = "#2563EB";
    [ObservableProperty] private BitmapSource? _categoryImagePreview;
    [ObservableProperty] private string? _categoryError;

    partial void OnLowStockOnlyChanged(bool value) => _ = RefreshAsync();
    partial void OnFilterChanged(string value)
    {
        foreach (var row in Products.Where(row => row.IsSelected && !MatchesFilter(row)))
            row.IsSelected = false;
        VisibleProducts.Refresh();
        OnPropertyChanged(nameof(FilterSummary));
        UpdateSelection();
    }

    private bool MatchesFilter(ProductRowViewModel row)
    {
        var query = Filter.Trim();
        if(IsPharmacy&&!MatchesPharmacyFilters(row))return false;
        return (!IsPharmacy||!NeedsSetupOnly||row.NeedsSetup)&&(row.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               row.Details.Contains(query,StringComparison.CurrentCultureIgnoreCase)||
               row.Barcode.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               row.CategoryName.Contains(query, StringComparison.CurrentCultureIgnoreCase));
    }

    public async Task RefreshAsync()
    {
        var settings = await _settings.GetAsync();
        _lowStockThreshold = settings.LowStockThreshold;
        await LoadEditorOptionsAsync(settings);

        var items = LowStockOnly
            ? await _products.GetLowStockAsync(_lowStockThreshold)
            : await _products.GetAllAsync();
        var medicineDetails=IsPharmacy?await _medicineCatalog.DescribeAsync(items):new Dictionary<int,MedicineMatch>();

        foreach (var row in Products)
            row.PropertyChanged -= SelectionChanged;
        Products.Clear();
        foreach (var p in items)
        {
            var row = new ProductRowViewModel(p, _lowStockThreshold,medicineDetails.GetValueOrDefault(p.Id));
            row.PropertyChanged += SelectionChanged;
            Products.Add(row);
        }
        UpdateSelection();
        OnPropertyChanged(nameof(BatchFilters));OnPropertyChanged(nameof(ManufacturerFilters));OnPropertyChanged(nameof(FormFilters));OnPropertyChanged(nameof(FilterSummary));
        if(IsPharmacy){var locations=await _stockLocations.LocationsAsync();LocationFilters=new[]{new ProductEditViewModel.ProductLocationOption(0,LocalizationManager.Instance["Pharmacy_AllLocations"]),new(-1,LocalizationManager.Instance["Pharmacy_Unassigned"])}.Concat(locations.Select(l=>new ProductEditViewModel.ProductLocationOption(l.Id,(l.ParentId is{} parent?locations.FirstOrDefault(p=>p.Id==parent)?.Name+" / ":"")+l.Name))).ToArray();await ApplyLocationFilterAsync(SelectedLocationFilter);}
    }

    private async Task LoadEditorOptionsAsync(AppSettings settings)
    {
        var business=(await _businessSetup.LoadAsync()).Profile;IsPharmacy=Editor.IsPharmacy=business.IsPharmacy;Editor.LooseEnabled=business.LooseGoodsAvailable;Editor.PackagesEnabled=business.PackagingAvailable;
        PrepareMedicineSearch();
        _categoryList = (await _categories.GetAllAsync()).ToList();
        OnPropertyChanged(nameof(FilterCategories));
        OnPropertyChanged(nameof(BulkCategories));
        Editor.Categories.Clear();
        foreach (var c in _categoryList) Editor.Categories.Add(c);
        Editor.Suppliers.Clear();
        Editor.Suppliers.Add(new SupplierOption(0, LocalizationManager.Instance["Supplier_None"]));
        foreach (var supplier in await _purchases.GetSuppliersAsync())
            Editor.Suppliers.Add(new SupplierOption(supplier.Id, supplier.Name));
        Editor.Locations.Clear();Editor.Locations.Add(new(0,LocalizationManager.Instance["Pharmacy_Unassigned"]));
        if(IsPharmacy){var locations=await _stockLocations.LocationsAsync();foreach(var location in locations)Editor.Locations.Add(new(location.Id,(location.ParentId is{} parent?locations.FirstOrDefault(l=>l.Id==parent)?.Name+" / ":"")+location.Name));}
        _defaultVat = IsPharmacy?0:settings.VatRates.Contains(0.20m) ? 0.20m : settings.VatRates.FirstOrDefault();
    }

    public async Task BeginScannerEntryAsync(string? barcode, bool purchasing = false, int? supplierId = null,
        string? productName = null, bool trackExpiry = false)
    {
        await LoadEditorOptionsAsync(await _settings.GetAsync());
        OpenNewProduct(barcode, scannerEntry: true, purchasing: purchasing);
        Editor.SupplierId = supplierId ?? 0;
        Editor.TrackExpiry = trackExpiry;
        if (productName is not null) Editor.Name = productName;
    }

    public async Task OpenProductAsync(int productId)
    {
        if (await _products.GetCurrentForRegisterAsync(productId) is { } product)
            Edit(new ProductRowViewModel(product, _lowStockThreshold));
    }

    [RelayCommand]
    private async Task NewAsync(){if(IsPharmacy)await QuickAddMedicineAsync("");else OpenNewProduct(null);}

    private void OpenNewProduct(string? barcode, bool scannerEntry = false, bool purchasing = false)
    {
        IsScannerEntry = scannerEntry;
        IsPurchaseEntry = purchasing;
        MatchingProduct = null;
        ProductSaved = false;
        SavedProductId = null;
        ProductError = null;
        Editor.LoadNew(_categoryList.FirstOrDefault()?.Id ?? 0, _defaultVat);
        if(IsPharmacy)Editor.VatPercent="";
        if (purchasing) Editor.Stock = "0";
        if (barcode is not null) Editor.Barcode = barcode;
        IsCaptureArmed = barcode is null && !scannerEntry;
        IsEditorOpen = true;
    }

    [RelayCommand]
    private void Edit(ProductRowViewModel? row)
    {
        if (row is null) return;
        IsCaptureArmed = false;
        IsScannerEntry = false;
        IsPurchaseEntry = false;
        MatchingProduct = null;
        ProductError = null;
        Editor.Load(row.Product);
        RestockAmount = string.Empty;
        IsEditorOpen = true;
    }

    [RelayCommand]
    private void CancelEdit() => IsEditorOpen = false;

    partial void OnIsEditorOpenChanged(bool value)
    {
        if (!value)
        {
            IsCaptureArmed = false;
            MatchingProduct = null;
            ProductError = null;
        }
        UpdateInventoryWorkflow();
    }

    partial void OnIsCaptureArmedChanged(bool value) => UpdateInventoryWorkflow();
    partial void OnIsCategoryEditorOpenChanged(bool value) => UpdateInventoryWorkflow();
    partial void OnIsBulkEditorOpenChanged(bool value) => UpdateInventoryWorkflow();

    public void SetInventoryActive(bool active)
    {
        _inventoryActive = active;
        UpdateInventoryWorkflow();
    }

    private bool CanCapturePhone => _inventoryActive && !_quickAddOpen && !_importDialogOpen && !IsBulkEditorOpen && !IsCategoryEditorOpen
        && (!IsEditorOpen || (IsCaptureArmed && !IsScannerEntry));

    private void UpdateInventoryWorkflow() =>
        _scanner.SetWorkflow(this, CanCapturePhone ? ScanMode.ProductCapture : ScanMode.Idle, CapturePhoneAsync);

    [RelayCommand]
    private void ToggleCapture()
    {
        if (!IsEditorOpen || IsScannerEntry || Editor.IsNew) return;
        IsCaptureArmed = !IsCaptureArmed;
        ProductError = null;
        if (IsCaptureArmed && !_scanner.IsPaired) _scanner.OpenPanelCommand.Execute(null);
    }

    private Task<ScanOutcome> CapturePhoneAsync(ScanRequest request) =>
        CaptureBarcodeAsync(request.Gs1?.Gtin ?? request.Barcode!, request.Epoch, request.Gs1);

    [RelayCommand]
    private async Task ValidateBarcodeAsync() => await CaptureBarcodeAsync(Editor.Barcode);

    public void AttachBarcodeField(System.Windows.Controls.TextBox field,bool package=false)=>_scanner.AttachBarcodeField(field,
        (request,current)=>CaptureBarcodeAsync(request.Gs1?.Gtin??request.Barcode!,request.Epoch,request.Gs1,current,package));

    private async Task<ScanOutcome> CaptureBarcodeAsync(string raw, Guid? epoch = null, Gs1Barcode? gs1 = null,Func<bool>? fieldCurrent=null,bool package=false)
    {
        if (_readingCapture || SaveCommand.IsRunning ||
            (fieldCurrent is not null ? !fieldCurrent()||!IsEditorOpen : epoch is null ? !IsEditorOpen || IsScannerEntry : !CanCapturePhone))
            return new ScanOutcome("paused");
        var code = _barcodes.Normalize(raw);
        if (code.Length is 0 or > 64)
        {
            ProductError = LocalizationManager.Instance["Scanner_ProductFields"];
            if (epoch is null) _sound.PlayWarning();
            return new ScanOutcome("invalid");
        }

        _readingCapture = true;
        var editorWasOpen = IsEditorOpen;
        try
        {
            var match = gs1 is null ? await _products.GetByBarcodeAsync(code) : await _lookup.FindTradeItemAsync(gs1.Gtin);
            if (IsEditorOpen != editorWasOpen ||
                (fieldCurrent is not null ? !fieldCurrent() : epoch is { } active && (!CanCapturePhone || !_scanner.CanApply(this, active))))
                return new ScanOutcome("paused");
            if(code.StartsWith("POSLOC:",StringComparison.OrdinalIgnoreCase))return fieldCurrent is not null?new ScanOutcome("invalid"):await FilterLocationAsync(code,epoch);

            MatchingProduct = !editorWasOpen || match?.Id != Editor.Id ? match : null;
            if (MatchingProduct is not null)
            {
                ProductError = LocalizationManager.Instance.Format("Scanner_DuplicateNamed", MatchingProduct.Name);
                if (epoch is null) _sound.PlayWarning();
                return new ScanOutcome("duplicate", MatchingProduct.Name);
            }
            if (editorWasOpen)
            {
                if(package)Editor.PackageBarcode=code;else Editor.Barcode = code;
                IsCaptureArmed = false;
            }
            else if(IsPharmacy&&gs1?.HasTrackingData()!=true){await QuickAddMedicineAsync(code);return new ScanOutcome("captured");}
            else OpenNewProduct(code);
            if (gs1?.HasTrackingData() == true && Editor.IsNew)
            {
                Editor.TrackExpiry = true;
                Editor.BatchLot = gs1.Batch ?? "";
                Editor.BatchExpiry = gs1.ExpiryDate?.ToDateTime(TimeOnly.MinValue);
            }
            ProductError = null;
            return new ScanOutcome("captured");
        }
        catch (Exception)
        {
            ProductError = LocalizationManager.Instance["Scanner_LookupError"];
            if (epoch is null) _sound.PlayWarning();
            return new ScanOutcome("error");
        }
        finally { _readingCapture = false; }
    }

    [RelayCommand]
    private void ViewMatchingProduct()
    {
        if (MatchingProduct is not { } match) return;
        Edit(new ProductRowViewModel(match, _lowStockThreshold));
    }

    [RelayCommand]
    private async Task ShowStockHistoryAsync()
    {
        if (Editor.Id is not { } id) return;
        try
        {
            IsCaptureArmed = false;
            var rows = (await _products.GetMovementsAsync(id)).Select(m => new
            {
                m.OccurredAt, m.QuantityChange, m.Reference, m.BatchLot,
                Kind = LocalizationManager.Instance["Stock_" + m.Kind]
            }).ToArray();
            new Views.StockHistoryWindow
            {
                Owner = System.Windows.Application.Current.MainWindow,
                Title = LocalizationManager.Instance["Stock_History"] + " · " + Editor.Name,
                DataContext = rows
            }.ShowDialog();
        }
        catch (Exception)
        {
            ProductError = LocalizationManager.Instance["Stock_LoadError"];
            _sound.PlayWarning();
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsCaptureArmed = false;
        ProductError = null;
        var text = LocalizationManager.Instance;
        try
        {
            var price = ParseDecimal(Editor.Price);
            var stock = IsPurchaseEntry ? 0m : ParseOptional(Editor.Stock) ?? 0m;
            var assignment = Editor.TrackExpiry && !Editor.IsNew ? ParseOptional(Editor.BatchQuantity) ?? 0m : 0m;
            if (assignment < 0m) throw new DomainException(text["Batch_PositiveQuantity"]);
            var purchaseCost = ParseOptional(Editor.PurchaseCost);
            var code = _barcodes.Normalize(Editor.BarcodeForSave);
            var unit = Editor.SellingMethod=="weight"?Unit.Kilogram:Editor.SellingMethod=="volume"?Unit.Litre:Unit.Piece;
            if(code.Length==0)code="INTERNAL-"+Guid.NewGuid().ToString("N");
            if (code.Length is 0 or > 64 || string.IsNullOrWhiteSpace(Editor.Name) || Editor.CategoryId == 0&&!IsPharmacy)
                throw new DomainException(text["Scanner_ProductFields"]);
            var vatRate = ParseOptional(Editor.VatPercent) / 100m;
            if (vatRate is null or < 0 or > 1)
                throw new DomainException(text["Products_InvalidVat"]);
            if (!int.TryParse(Editor.WarrantyMonths, out var warrantyMonths)) throw new DomainException("Enter warranty months as a whole number.");
            Editor.ImagePath = _storage.ImportImage(Editor.ImagePath);

            SavedProductId = await _uow.ExecuteInTransactionAsync(async token =>
            {
                var categoryId=Editor.CategoryId;
                if(IsPharmacy&&categoryId==0){var category=(await _categories.GetAllAsync(token)).FirstOrDefault(c=>c.Name=="Medicines");if(category is null){category=new Category("Medicines");await _categories.AddAsync(category,token);await _uow.SaveChangesAsync(token);}categoryId=category.Id;}
                if (await _products.GetByBarcodeAsync(code, token) is { } duplicate && duplicate.Id != Editor.Id)
                    throw new DomainException(text["Scanner_DuplicateBarcode"]);
                if (Editor.TrackExpiry && await _lookup.FindTradeItemAsync(code, token) is { } tradeItem && tradeItem.Id != Editor.Id)
                    throw new DomainException(text["Scanner_DuplicateBarcode"]);

                Product product;
                InventoryBatch? assigned = null;
                var now = DateTime.Now;
                if (Editor.IsNew)
                {
                    product = new Product(code, Editor.Name, price, vatRate.Value, unit, Editor.TrackExpiry ? 0m : stock, categoryId);
                    if(IsPharmacy&&!IsPurchaseEntry&&string.IsNullOrWhiteSpace(Editor.Stock))product.MarkStockUntracked();
                    product.SetExpiryTracking(Editor.TrackExpiry);
                    product.SetSerialTracking(Editor.TrackSerial, warrantyMonths);
                    product.SetImagePath(Editor.ImagePath);
                    product.SetActive(Editor.IsActive);
                    product.SetPurchaseCost(purchaseCost);
                    product.SetSupplier(Editor.SupplierId == 0 ? null : Editor.SupplierId);
                    if (Editor.TrackExpiry && stock != 0m)
                        assigned = product.RecordBatch(Editor.BatchLot, RequireBatchExpiry(), stock, product.SupplierId, now);
                    await _products.AddAsync(product, token);
                    await _uow.SaveChangesAsync(token);
                    if (stock != 0m)
                        _uow.RecordMovement(new StockMovement(product.Id, product.Name, stock,
                            StockMovementKind.OpeningStock, now, batch: assigned));
                }
                else
                {
                    product = await _products.GetByIdAsync(Editor.Id!.Value, token)
                                  ?? throw new DomainException("Product not found.");
                    product.SetBarcode(code);
                    product.SetName(Editor.Name);
                    product.SetPrice(price);
                    product.SetVatRate(vatRate.Value);
                    product.SetUnit(unit);
                    var adjustment = Editor.TrackExpiry ? 0m : stock - product.StockQuantity;
                    product.SetExpiryTracking(Editor.TrackExpiry);
                    product.SetSerialTracking(Editor.TrackSerial, warrantyMonths);
                    if(!product.StockTracked&&stock!=0)throw new DomainException("Pharmacy_VerifyStockFirst");
                    if (!Editor.TrackExpiry&&product.StockTracked) product.SetStock(stock);
                    if (adjustment != 0m)
                        _uow.RecordMovement(new StockMovement(product.Id, product.Name, adjustment,
                            StockMovementKind.ManualAdjustment, DateTime.Now));
                    product.SetCategory(categoryId);
                    product.SetImagePath(Editor.ImagePath);
                    product.SetActive(Editor.IsActive);
                    product.SetPurchaseCost(purchaseCost);
                    product.SetSupplier(Editor.SupplierId == 0 ? null : Editor.SupplierId);
                    if (assignment > 0m)
                    {
                        assigned = product.RecordBatch(Editor.BatchLot, RequireBatchExpiry(), assignment, product.SupplierId, now, fromExistingStock: true);
                        await _uow.SaveChangesAsync(token);
                        _uow.RecordMovement(new StockMovement(product.Id, product.Name, 0m, StockMovementKind.BatchAssignment,
                            now, assignment.ToString("0.###", CultureInfo.InvariantCulture), assigned));
                    }
                }
                if(!int.TryParse(Editor.UnitsPerPackage,out var packageUnits))throw new DomainException(text["Measure_InvalidPackage"]);
                if(Editor.PackageBarcode.Length>0&&await _products.GetByBarcodeAsync(Editor.PackageBarcode,token) is{} other&&other.Id!=product.Id)throw new DomainException(text["Scanner_DuplicateBarcode"]);
                product.ConfigurePackaging(Editor.PurchaseUnit,Editor.UsesPackaging?packageUnits:1,Editor.UsesPackaging?Editor.PackageBarcode:null,Editor.UsesPackaging?ParseOptional(Editor.PackageCost):null,Editor.UsesPackaging?ParseOptional(Editor.PackagePrice):null,ParseOptional(Editor.MinimumStock));
                if(IsPharmacy)
                {
                    if(product.TrackSerial||product.WarrantyMonths>0)throw new DomainException("Pharmacy_Only");
                    if(!int.TryParse(Editor.UnitsPerStrip,out var stripUnits))throw new DomainException("Pharmacy_InvalidUnits");
                    product.ConfigureMedicineUnits(Editor.MedicineUnit,stripUnits,ParseOptional(Editor.StripPrice));
                    product.SetDefaultLocation(Editor.DefaultLocationId==0?null:Editor.DefaultLocationId);
                    product.SetMedicineDetails(Editor.MedicineGeneric,Editor.MedicineStrength,Editor.MedicineForm,Editor.MedicineManufacturer);
                    if(Editor.MedicineReferenceId is{} referenceId)product.LinkMedicine(referenceId);
                    if(Editor.DispensingRulesReviewed)product.ReviewDispensingRules(Editor.RequiresPrescription,Editor.RequiresDispensingReference);
                }
                await _ledger.ValidateProductTrackingAsync(product, token);
                await _uow.SaveChangesAsync(token);
                return product.Id;
            });
            ProductSaved = true;
            IsEditorOpen = false;
            if (!IsScannerEntry) await RefreshAsync();
        }
        catch (Exception ex)
        {
            ProductError = ex is DomainException ? ex.Message : text["Scanner_ProductSaveError"];
            _sound.PlayWarning();
        }
    }

    [RelayCommand]
    private void ShowDevices() => new Views.SerialRecordsWindow(_ledger)
        { Owner = System.Windows.Application.Current.MainWindow }.ShowDialog();

    [RelayCommand]
    private async Task ShowProductDevicesAsync()
    {
        if (Editor.Id is not { } id) return;
        if (await _products.GetByIdAsync(id) is { } product)
            new Views.SerialRecordsWindow(_ledger, product) { Owner = System.Windows.Application.Current.MainWindow }.ShowDialog();
    }

    private DateOnly RequireBatchExpiry() => Editor.BatchExpiry is { } expiry
        ? DateOnly.FromDateTime(expiry) : throw new DomainException(LocalizationManager.Instance["Batch_ExpiryRequired"]);

    [RelayCommand]
    private async Task ViewBatchesAsync()
    {
        if (Editor.Id is not { } id || await _products.GetCurrentForRegisterAsync(id) is not { } product) return;
        _scanner.WhileScanPaused(() => new Views.InventoryBatchesWindow(product,IsPharmacy?async(id,reason,hold)=>{await _pharmacyInventory.SetBatchHoldAsync(id,reason,hold);product.Batches.Single(b=>b.Id==id).SetHold(hold?reason:"");}:null)
            { Owner = System.Windows.Application.Current.MainWindow }.ShowDialog());
        await RefreshAsync();
    }

    [RelayCommand]
    private void DownloadProductTemplate()
    {
        if (_importDialogOpen) return;
        _importDialogOpen = true;
        UpdateInventoryWorkflow();
        try { BulkFiles.SaveTemplate("POS Products.csv", BulkRecordService.ProductTemplate); }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException) { ImportError = error.Message; }
        finally
        {
            _importDialogOpen = false;
            UpdateInventoryWorkflow();
        }
    }

    [RelayCommand]
    private async Task ImportProductsAsync()
    {
        if (_importDialogOpen) return;
        _importDialogOpen = true;
        UpdateInventoryWorkflow();
        try
        {
            if (BulkFiles.PickCsv() is not { } path) return;
            ImportError = ImportMessage = null;
            using var reader = System.IO.File.OpenText(path);
            var count = await _bulk.ImportProductsAsync(reader, photo => _storage.ImportTemplatePhoto(photo, path));
            ImportMessage = LocalizationManager.Instance.Format("Bulk_ProductsImported", count);
            await RefreshAsync();
        }
        catch (Exception error) { ImportError = error.Message; }
        finally
        {
            _importDialogOpen = false;
            UpdateInventoryWorkflow();
        }
    }

    [RelayCommand]
    private void EditCategories()
    {
        NewCategory();
        IsCategoryEditorOpen = true;
    }

    [RelayCommand]
    private void NewCategory()
    {
        SelectedCategory = null;
        LoadCategory(null);
    }

    partial void OnSelectedCategoryChanged(Category? value) => LoadCategory(value);

    private void LoadCategory(Category? category)
    {
        CategoryName = category?.Name ?? string.Empty;
        var color = CategoryColorConverter.Brush(category?.ColorHex, category?.Id ?? 5).Color;
        CategoryColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        CategoryImagePreview = null;
        _categoryImageSource = null;
        _categoryImageChanged = false;
        if (_storage.CategoryImagePath(category?.ImageFileName) is { } path)
        {
            try { CategoryImagePreview = PosStorage.ReadCategoryThumbnail(path); }
            catch (Exception) { }
        }
        CategoryError = null;
    }

    [RelayCommand]
    private void BrowseCategoryImage()
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationManager.Instance["Category_ChooseImage"],
            Filter = "PNG, JPG, BMP, GIF|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            CategoryImagePreview = PosStorage.ReadCategoryThumbnail(dialog.FileName);
            _categoryImageSource = dialog.FileName;
            _categoryImageChanged = true;
            CategoryError = null;
        }
        catch (Exception)
        {
            CategoryError = LocalizationManager.Instance["Category_ImageError"];
        }
    }

    [RelayCommand]
    private void RemoveCategoryImage()
    {
        CategoryImagePreview = null;
        _categoryImageSource = null;
        _categoryImageChanged = true;
        CategoryError = null;
    }

    [RelayCommand]
    private void CloseCategoryEditor() => IsCategoryEditorOpen = false;

    [RelayCommand]
    private async Task SaveCategoryAsync()
    {
        var text = LocalizationManager.Instance;
        var name = CategoryName.Trim();
        var id = SelectedCategory?.Id;
        var color = CategoryColor;
        var imageChanged = _categoryImageChanged;
        var imageSource = _categoryImageSource;
        if (name.Length == 0)
        {
            CategoryError = text["Category_NameRequired"];
            return;
        }

        try
        {
            var current = await _categories.GetAllAsync();
            if (current.Any(c => c.Id != id && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                CategoryError = text["Category_Duplicate"];
                return;
            }

            var importedFile = imageChanged ? _storage.ImportImage(imageSource) : null;
            await _uow.ExecuteInTransactionAsync(async token =>
            {
                var category = id is { } key
                    ? await _categories.GetByIdAsync(key, token) ?? throw new DomainException(text["Category_SaveError"])
                    : new Category(name);
                category.Rename(name);
                category.SetColor(color);
                if (imageChanged) category.SetImage(importedFile);
                if (id is null) await _categories.AddAsync(category, token);
                await _uow.SaveChangesAsync(token);
                return category;
            });
        }
        catch (Exception)
        {
            CategoryError = text["Category_SaveError"];
            return;
        }
        IsCategoryEditorOpen = false;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RestockAsync()
    {
        if (Editor.IsNew || !decimal.TryParse(RestockAmount.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            return;
        try
        {
            var product = await _products.GetByIdAsync(Editor.Id!.Value)
                          ?? throw new DomainException("Product not found.");
            await _inventory.RestockAsync(product.Id, amount);
            Editor.Load(product);
            RestockAmount = string.Empty;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(ProductRowViewModel? row)
    {
        if (row is null) return;
        if (!Views.NoticeDialog.ConfirmDelete($"{row.Name}?", LocalizationManager.Instance["Common_Delete"])) return;
        try
        {
            await _inventory.DeleteProductsAsync(new[] { row.Id });
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
    }

    [RelayCommand]
    private void SelectVisible()
    {
        foreach (var row in VisibleProducts.Cast<ProductRowViewModel>())
            row.IsSelected = true;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ClearSelection()
    {
        foreach (var row in Products)
            row.IsSelected = false;
    }

    private bool HasVisibleProducts() => !VisibleProducts.IsEmpty;

    [RelayCommand(CanExecute = nameof(HasVisibleProducts))]
    private void ToggleVisibleSelection()
    {
        if (VisibleSelectionState == true) ClearSelection();
        else SelectVisible();
    }

    [RelayCommand(CanExecute = nameof(HasBulkSelection))]
    private void OpenBulkEdit()
    {
        BulkPrice = BulkStock = BulkVatPercent = string.Empty;
        BulkCategoryId = 0;
        IsBulkEditorOpen = true;
    }

    [RelayCommand]
    private void CancelBulkEdit() => IsBulkEditorOpen = false;

    [RelayCommand]
    private async Task ApplyBulkEditAsync()
    {
        try
        {
            var change = new ProductBatchUpdate(
                ParseOptional(BulkPrice), ParseOptional(BulkStock),
                ParseOptional(BulkVatPercent) / 100m,
                BulkCategoryId == 0 ? null : BulkCategoryId);
            if (change is { Price: null, Stock: null, VatRate: null, CategoryId: null })
            {
                Error(LocalizationManager.Instance["Bulk_ChangeRequired"]);
                return;
            }

            await _inventory.UpdateProductsAsync(SelectedIds(), change);
            IsBulkEditorOpen = false;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(HasBulkSelection))]
    private async Task DeleteSelectedAsync()
    {
        var ids = SelectedIds();
        var text = LocalizationManager.Instance;
        if (!Views.NoticeDialog.ConfirmDelete(text.Format("Bulk_ConfirmDelete", ids.Length), text["Bulk_Delete"])) return;

        try
        {
            await _inventory.DeleteProductsAsync(ids);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
    }

    private void SelectionChanged(object? sender, PropertyChangedEventArgs e) => UpdateSelection();

    private void UpdateSelection()
    {
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasBulkSelection));
        OnPropertyChanged(nameof(VisibleSelectionState));
        ToggleVisibleSelectionCommand.NotifyCanExecuteChanged();
        ClearSelectionCommand.NotifyCanExecuteChanged();
        OpenBulkEditCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    private int[] SelectedIds() => Products.Where(row => row.IsSelected).Select(row => row.Id).ToArray();

    private static decimal? ParseOptional(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return decimal.TryParse(value, NumberStyles.Number & ~NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number)
            ? number : throw new DomainException(LocalizationManager.Instance["Bulk_NumberFormat"]);
    }

    [RelayCommand]
    private async Task PrintLabelsAsync()
    {
        if (Products.Count == 0) return;
        try
        {
            var items = Products.Select(r => r.Product).ToList();
            var path = _storage.ChoosePdfPath($"ShelfLabels_{DateTime.Now:yyyyMMdd}.pdf");
            if (path is null) return;

            await _labels.SavePdfAsync(items, path);
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
            catch { /* no viewer */ }
        }
        catch (Exception ex)
        {
            Error(ex.Message);
        }
    }

    private static decimal ParseDecimal(string s) =>
        decimal.TryParse((s ?? string.Empty).Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
            ? v : throw new DomainException(LocalizationManager.Instance["Common_InvalidNumber"]);

    private void Error(string msg)
    {
        _sound.PlayWarning();
        Views.NoticeDialog.Show(msg, LocalizationManager.Instance["Common_Error"]);
    }
}
