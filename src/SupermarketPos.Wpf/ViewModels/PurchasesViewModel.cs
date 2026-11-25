using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Application.Services;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Infrastructure.Scanning;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Wpf.Views;

namespace SupermarketPos.Wpf.ViewModels;

public sealed partial class PurchasesViewModel(
    IProductRepository products, IPurchaseService purchases, IAuthService auth, RegisterBarcodeLookup lookup,
    IBarcodeService barcodes, ProductsViewModel productEditor, ScannerController scanner, ScannerFeedback sound, ITradeLedger ledger,IBusinessSetup businessSetup,SupermarketPos.Infrastructure.Business.MedicineCatalogService medicineCatalog,SupermarketPos.Infrastructure.Business.StockLocationService stockLocations) : ObservableObject
{
    private readonly LocalizationManager _text = LocalizationManager.Instance;
    private Guid _confirmation = Guid.NewGuid();
    private bool _active;
    private bool _lookingUp;
    private bool _creatingProduct;
    private bool _receiving;
    private bool _awaitingBatchLine;
    private readonly UnknownBarcodeNotices _notices = new();

    public ObservableCollection<PurchaseDraftRow> Draft { get; } = new();
    public bool HasPendingEntry=>Draft.Count>0||SelectedProduct is not null||_creatingProduct||_receiving||Reference.Length>0||Notes.Length>0;
    public string Total
    {
        get
        {
            try { return MoneyText.Format(Draft.Sum(row => row.Amount)); }
            catch (OverflowException) { return "—"; }
        }
    }
    public bool IsHistoryEmpty => VisibleHistory.Count == 0;
    public string ScanLabel => _text[IsScanning ? "Scanner_Stop" : "Purchase_ScanProducts"];
    public IReadOnlyList<PurchaseHistoryRow> VisibleHistory =>
        HistorySupplier is null ? History : History.Where(row => row.SupplierId == HistorySupplier.Id).ToArray();

    [ObservableProperty] private IReadOnlyList<Product> _products = Array.Empty<Product>();
    [ObservableProperty] private IReadOnlyList<Supplier> _suppliers = Array.Empty<Supplier>();
    [ObservableProperty] private IReadOnlyList<PurchaseHistoryRow> _history = Array.Empty<PurchaseHistoryRow>();
    [ObservableProperty] private PurchaseHistoryRow? _selectedPurchase;
    [ObservableProperty] private Supplier? _selectedSupplier;
    [ObservableProperty] private Supplier? _historySupplier;
    [ObservableProperty] private Product? _selectedProduct;
    [ObservableProperty] private PurchaseDraftRow? _selectedLine;
    [ObservableProperty] private string _supplierName = "";
    [ObservableProperty] private int? _editingSupplierId;
    [ObservableProperty] private bool _isSupplierEditorOpen;
    [ObservableProperty] private string _supplierKind = "";
    [ObservableProperty] private string? _supplierContact;
    [ObservableProperty] private string? _supplierPhone;
    [ObservableProperty] private string? _supplierEmail;
    [ObservableProperty] private string? _supplierAddress;
    [ObservableProperty] private string _reference = "";
    [ObservableProperty] private DateTime? _purchaseDate = DateTime.Today;
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private string _quantity = "1";
    [ObservableProperty] private string _unitCost = "";
    [ObservableProperty] private bool _buyPackages;
    private bool _packagingEnabled;
    [ObservableProperty]private bool _isPharmacy;
    public bool IsNotPharmacy=>!IsPharmacy;
    [ObservableProperty]private System.Windows.FrameworkElement? _medicineSearch;
    public bool CanBuyPackages=>!IsPharmacy&&_packagingEnabled&&SelectedProduct?.UnitsPerPackage>1;
    partial void OnBuyPackagesChanged(bool value){if(SelectedProduct is{} p)UnitCost=FormatCost(value?p.PackagePurchaseCost:p.PurchaseCost);}
    [ObservableProperty] private string _batchLot = "";
    [ObservableProperty] private DateTime? _batchExpiry;
    public bool ShowBatchEntry => SelectedProduct?.TrackExpiry == true;
    [ObservableProperty] private string _productSearch = "";
    public SupplierKindOption[] SupplierKinds =>
    [
        new("", _text["Supplier_Unspecified"]),
        new("COMPANY", _text["Supplier_Company"]),
        new("INDIVIDUAL", _text["Supplier_Individual"])
    ];
    public IReadOnlyList<Product> ProductMatches => string.IsNullOrWhiteSpace(ProductSearch) ? []
        : Products.Where(p => p.Name.Contains(ProductSearch.Trim(), StringComparison.CurrentCultureIgnoreCase) ||
            p.Barcode.Contains(ProductSearch.Trim(), StringComparison.OrdinalIgnoreCase)).Take(30).ToArray();
    public bool ShowProductMatches => !IsPharmacy&&SelectedProduct is null && ProductMatches.Count > 0;
    public bool CanAddNewProduct => SelectedProduct is null && ProductMatches.Count == 0;
    [ObservableProperty] private string? _unknownBarcode;
    [ObservableProperty] private string? _moreBarcodeNotices;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private int _activeTab;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _payLater;
    [ObservableProperty] private string _paidNow = "0";
    [ObservableProperty] private string _initialPaymentMethod = "CASH";
    [ObservableProperty] private IReadOnlyList<SupplierBalanceRow> _supplierBalances = [];
    [ObservableProperty] private SupplierBalanceRow? _selectedBalanceSupplier;
    [ObservableProperty] private PurchaseHistoryRow? _selectedBalanceInvoice;
    public IReadOnlyList<SupplierKindOption> PaymentMethods => Payment.RecordedMethods.Select(code =>
        new SupplierKindOption(code, _text[Payment.LabelKey(code)])).ToArray();

    public async Task RefreshAsync()
    {
        try
        {
            var business=(await businessSetup.LoadAsync()).Profile;_packagingEnabled=business.PackagingAvailable;IsPharmacy=business.IsPharmacy;OnPropertyChanged(nameof(IsNotPharmacy));
            if(IsPharmacy&&MedicineSearch is null)
            {
                var search=new MedicineSearchBox(medicineCatalog);
                search.Input.SetBinding(System.Windows.Controls.TextBox.TextProperty,new System.Windows.Data.Binding(nameof(ProductSearch)){Source=this,Mode=System.Windows.Data.BindingMode.TwoWay,UpdateSourceTrigger=System.Windows.Data.UpdateSourceTrigger.PropertyChanged});
                search.Selected+=async match=>{try{if(match.ProductId is{} id)SelectedProduct=await products.GetCurrentForRegisterAsync(id);else{ProductSearch=match.Name;await OpenNewProductAsync(null,match.Name,rescan:true,referenceId:match.ReferenceId);}}catch{Warn("Pharmacy_SearchError");}};
                search.BarcodeSubmitted+=async code=>{await scanner.DispatchPhysicalAsync(code);search.Input.Clear();};
                MedicineSearch=search;
            }
            if(IsPharmacy)await LoadLocationsAsync();
            var productId = SelectedProduct?.Id;
            Products = await products.GetAllAsync();
            SelectedProduct = Products.FirstOrDefault(product => product.Id == productId);
            var supplierId = SelectedSupplier?.Id;
            var historySupplierId = HistorySupplier?.Id;
            var purchaseId = SelectedPurchase?.Id;
            Suppliers = await purchases.GetSuppliersAsync();
            SelectedSupplier = Suppliers.FirstOrDefault(supplier => supplier.Id == supplierId);
            HistorySupplier = Suppliers.FirstOrDefault(supplier => supplier.Id == historySupplierId);
            History = (await purchases.GetHistoryAsync()).Select(purchase => new PurchaseHistoryRow(purchase)).ToArray();
            var balanceSupplier = SelectedBalanceSupplier?.Id;
            SupplierBalances = History.GroupBy(row => (row.SupplierId, row.Supplier))
                .Select(group => new SupplierBalanceRow(group.Key.SupplierId, group.Key.Supplier, group.ToArray())).ToArray();
            SelectedBalanceSupplier = SupplierBalances.FirstOrDefault(s => s.Id == balanceSupplier) ?? SupplierBalances.FirstOrDefault();
            SelectedBalanceInvoice = SelectedBalanceSupplier?.Invoices.FirstOrDefault();
            SelectedPurchase = VisibleHistory.FirstOrDefault(purchase => purchase.Id == purchaseId);
        }
        catch (Exception) { Warn("Purchase_LoadError"); }
    }

    public void SetViewActive(bool active)
    {
        _active = active;
        UpdateWorkflow();
    }

    partial void OnActiveTabChanged(int value) => UpdateWorkflow();
    partial void OnSelectedSupplierChanged(Supplier? oldValue, Supplier? newValue)
    {
        IsScanning=newValue is not null;OnPropertyChanged(nameof(ScanLabel));
        UpdateWorkflow();
    }
    partial void OnSelectedLineChanged(PurchaseDraftRow? value) => RemoveItemCommand.NotifyCanExecuteChanged();
    partial void OnSelectedProductChanged(Product? value)
    {
        BatchLot = "";
        BatchExpiry = null;
        _awaitingBatchLine = false;
        OnPropertyChanged(nameof(ShowBatchEntry));
        UnitCost = FormatCost(value?.PurchaseCost);
        BuyPackages=false;OnPropertyChanged(nameof(CanBuyPackages));
        MedicinePurchaseUnit=value?.SellingUnit??"piece";OnPropertyChanged(nameof(MedicinePurchaseUnits));OnPropertyChanged(nameof(ShowMedicinePurchaseUnit));
        ApplyProductDestination(value);
        if (value is not null) ProductSearch = value.Name;
        NotifySearch();
        UpdateWorkflow();
    }

    partial void OnProductsChanged(IReadOnlyList<Product> value) => NotifySearch();
    partial void OnSelectedBalanceSupplierChanged(SupplierBalanceRow? value) => SelectedBalanceInvoice = value?.Invoices.FirstOrDefault();

    private bool CanPaySupplier() => (ActiveTab == 1 ? SelectedPurchase : SelectedBalanceInvoice) is not null;
    partial void OnSelectedPurchaseChanged(PurchaseHistoryRow? value) => PaySupplierCommand.NotifyCanExecuteChanged();
    partial void OnSelectedBalanceInvoiceChanged(PurchaseHistoryRow? value) => PaySupplierCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanPaySupplier))]
    private async Task PaySupplierAsync()
    {
        var invoice = ActiveTab == 1 ? SelectedPurchase : SelectedBalanceInvoice;
        if (invoice is null) return;
        new BalancePaymentWindow(ledger, auth, null, invoice.Purchase) { Owner = System.Windows.Application.Current.MainWindow }.ShowDialog();
        await RefreshAsync();
    }
    partial void OnProductSearchChanged(string value)
    {
        if (SelectedProduct is { } selected && value != selected.Name) SelectedProduct = null;
        NotifySearch();
    }
    partial void OnIsSupplierEditorOpenChanged(bool value) => UpdateWorkflow();
    private void NotifySearch()
    {
        OnPropertyChanged(nameof(ProductMatches));
        OnPropertyChanged(nameof(ShowProductMatches));
        OnPropertyChanged(nameof(CanAddNewProduct));
    }

    [RelayCommand]
    private void ChooseProduct(Product? product)
    {
        if (product is not null) SelectedProduct = product;
    }
    partial void OnHistoryChanged(IReadOnlyList<PurchaseHistoryRow> value) => NotifyHistory();
    partial void OnHistorySupplierChanged(Supplier? value)
    {
        SelectedPurchase = null;
        NotifyHistory();
    }

    private void NotifyHistory()
    {
        OnPropertyChanged(nameof(VisibleHistory));
        OnPropertyChanged(nameof(IsHistoryEmpty));
    }

    private void UpdateWorkflow()
    {
        var mode = !_active || ActiveTab != 0 || !auth.IsLoggedIn ? ScanMode.Away
            : _creatingProduct || IsSupplierEditorOpen || _awaitingBatchLine ? ScanMode.ProductEntry
            : _receiving ? ScanMode.Receiving
            : SelectedSupplier is not null ? ScanMode.Purchasing : ScanMode.Idle;
        scanner.SetWorkflow(this, mode, DispatchPhoneAsync);
        RecordCommand.NotifyCanExecuteChanged();
        PaySupplierCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ToggleScan()
    {
        if (SelectedSupplier is null) { Warn("Purchase_SupplierRequired"); return; }
        IsScanning = !IsScanning;
        OnPropertyChanged(nameof(ScanLabel));
        UpdateWorkflow();
        if (IsScanning && !scanner.IsPaired) scanner.OpenPanelCommand.Execute(null);
    }

    [RelayCommand]
    private void AddSupplier() => BeginSupplier(null);

    [RelayCommand]
    private void EditSupplier()
    {
        if (SelectedSupplier is { } supplier) BeginSupplier(supplier);
    }

    private void BeginSupplier(Supplier? supplier)
    {
        Error = null;
        EditingSupplierId = supplier?.Id;
        SupplierName = supplier?.Name ?? "";
        SupplierKind = supplier?.Kind ?? "";
        SupplierContact = supplier?.ContactName;
        SupplierPhone = supplier?.Phone;
        SupplierEmail = supplier?.Email;
        SupplierAddress = supplier?.Address;
        IsSupplierEditorOpen = true;
        try
        {
            new SupplierEditorWindow(this)
            {
                Owner = System.Windows.Application.Current.MainWindow,
                Title = _text[supplier is null ? "Purchase_AddSupplier" : "Supplier_Edit"]
            }.ShowDialog();
        }
        finally { IsSupplierEditorOpen = false; }
    }

    [RelayCommand]
    private async Task SaveSupplierAsync()
    {
        if (string.IsNullOrWhiteSpace(SupplierName)) { Warn("Purchase_SupplierRequired"); return; }
        try
        {
            Error = null;
            var supplier = await purchases.SaveSupplierAsync(EditingSupplierId,
                new SupplierDetails(SupplierName, SupplierKind, SupplierContact, SupplierPhone, SupplierEmail, SupplierAddress));
            Suppliers = await purchases.GetSuppliersAsync();
            SelectedSupplier = Suppliers.FirstOrDefault(item => item.Id == supplier.Id);
            IsSupplierEditorOpen = false;
        }
        catch (DomainException e) { Error = e.Message; sound.PlayWarning(); }
        catch (Exception) { Warn("Purchase_SupplierError"); }
    }

    [RelayCommand]
    private void ClearSupplierFilter() => HistorySupplier = null;

    [RelayCommand]
    private async Task SubmitProductSearchAsync()
    {
        var query = ProductSearch.Trim();
        if (query.Length == 0 || SelectedProduct is not null || _lookingUp) return;
        _lookingUp = true;
        RecordCommand.NotifyCanExecuteChanged();
        try
        {
            var matches = ProductMatches;
            var exact = matches.Where(product => product.Barcode.Equals(query, StringComparison.OrdinalIgnoreCase)
                || product.Name.Equals(query, StringComparison.CurrentCultureIgnoreCase)).Take(2).ToArray();
            if (exact.Length == 1) SelectedProduct = exact[0];
            else if (await lookup.FindAsync(query) is { } match && ProductSearch.Trim() == query)
            {
                SelectedProduct = match.Product;
                if (match.Quantity is { } quantity) Quantity = quantity.ToString(CultureInfo.InvariantCulture);
            }
            else if (matches.Count == 1 && ProductSearch.Trim() == query) SelectedProduct = matches[0];
        }
        catch (Exception) { Warn("Scanner_LookupError"); }
        finally
        {
            _lookingUp = false;
            RecordCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanAccept() => _active && ActiveTab == 0 && auth.IsLoggedIn && SelectedSupplier is not null &&
        !_creatingProduct && !_receiving && !IsSupplierEditorOpen;

    private async Task<ScanOutcome> DispatchPhoneAsync(ScanRequest request)
    {
        if (!CanAccept() || _lookingUp)
            return new ScanOutcome("paused");
        var gs1 = request.Gs1;
        var code = gs1?.Gtin ?? barcodes.Normalize(request.Barcode!);
        if (code.Length is 0 or > 64)
        {
            Error = _text["Scanner_ProductFields"];
            return new ScanOutcome("invalid");
        }
        Error = null;
        _lookingUp = true;
        RecordCommand.NotifyCanExecuteChanged();
        try
        {
            if(code.StartsWith("POSLOC:",StringComparison.OrdinalIgnoreCase))return await SelectDestinationAsync(code,request.Epoch);
            var match = gs1 is null ? await lookup.FindAsync(code) : await lookup.FindGs1Async(gs1);
            if (!CanAccept() || !scanner.CanApply(this, request.Epoch))
                return new ScanOutcome("paused");
            if (match is null)
            {
                if (_notices.Add(code, gs1?.HasTrackingData() == true))
                    PublishBarcodeNotices();
                return new ScanOutcome("unknown");
            }

            var product = match.Product;
            var quantity = match.Packaged?1m:match.Quantity??1m;
            if(match.Packaged&&!_packagingEnabled){Error=_text["Measure_FeatureDisabled"];return new ScanOutcome("paused");}
            if (product.TrackExpiry)
            {
                SelectedProduct = product;
                BuyPackages=match.Packaged;
                Quantity = quantity.ToString(CultureInfo.InvariantCulture);
                BatchLot = gs1?.Batch ?? "";
                BatchExpiry = gs1?.ExpiryDate?.ToDateTime(TimeOnly.MinValue);
                _awaitingBatchLine = true;
                UpdateWorkflow();
                Status = _text.Format("Batch_PurchaseEntry", product.Name);
                return new ScanOutcome("batch_required", product.Name);
            }
            if (gs1?.HasTrackingData() == true && !product.TrackSerial)
            {
                Error = _text["Batch_EnableFirst"];
                return new ScanOutcome("batch_discrepancy", product.Name);
            }
            var existing = Draft.FirstOrDefault(row => row.Product.Id == product.Id&&row.Packaged==match.Packaged);
            if (existing is null || product.TrackSerial)
            {
                if (!AddDraftRow(product, quantity,match.Packaged?product.PackagePurchaseCost:product.PurchaseCost,packaged:match.Packaged)) return new ScanOutcome("paused");
            }
            else existing.Quantity += quantity;
            Status = _text.Format("Purchase_ScanAdded", product.Name);
            return new ScanOutcome("purchase_added", product.Name);
        }
        catch (Exception)
        {
            Error = _text["Scanner_LookupError"];
            return new ScanOutcome("error");
        }
        finally
        {
            _lookingUp = false;
            RecordCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void DismissUnknown()
    {
        _notices.Dismiss();
        PublishBarcodeNotices();
    }

    private void PublishBarcodeNotices()
    {
        UnknownBarcode = _notices.Current;
        MoreBarcodeNotices = _notices.Remaining > 0
            ? _text.Format("Scanner_AdditionalNotices", _notices.Remaining) : null;
    }

    [RelayCommand]
    private async Task AddUnknownProductAsync()
    {
        if (UnknownBarcode is { } barcode && await OpenNewProductAsync(barcode, null, rescan: true, trackExpiry: _notices.CurrentIsGs1))
            DismissUnknown();
    }

    [RelayCommand]
    private async Task AddNewProductAsync()
    {
        var query = ProductSearch.Trim();
        var barcode = query.Length is >= 4 and <= 64 && query.Any(char.IsAsciiDigit) &&
                      !query.Any(char.IsWhiteSpace) ? query : null;
        await OpenNewProductAsync(barcode, barcode is null && query.Length > 0 ? query : null);
    }

    private async Task<bool> OpenNewProductAsync(string? barcode, string? name, bool rescan = false, bool trackExpiry = false,int? referenceId=null)
    {
        if (!CanAccept() || _lookingUp)
        {
            if (SelectedSupplier is null) Warn("Purchase_SupplierRequired");
            return false;
        }
        _creatingProduct = true;
        UpdateWorkflow();
        try
        {
            if(IsPharmacy&&!trackExpiry)
            {
                var createdId=await productEditor.CreateMedicineForPurchaseAsync(barcode??"",name??"",referenceId,SelectedSupplier?.Id);
                if(createdId is null)return false;Products=await products.GetAllAsync();ResetProductEntry();Status=_text["Scanner_Rescan"];return true;
            }
            await productEditor.BeginScannerEntryAsync(barcode, purchasing: true, supplierId: SelectedSupplier?.Id,
                productName: name, trackExpiry: trackExpiry);
            var window = new ScannedProductWindow(productEditor) { Owner = System.Windows.Application.Current.MainWindow };
            if (window.ShowDialog() == true && productEditor.SavedProductId is { } id)
            {
                Products = await products.GetAllAsync();
                var created = Products.Single(p => p.Id == id);
                if (rescan||IsPharmacy)
                {
                    ResetProductEntry();
                    Status = _text["Scanner_Rescan"];
                    return true;
                }
                if (created.TrackExpiry)
                {
                    SelectedProduct = created;
                    Status = _text.Format("Batch_PurchaseEntry", created.Name);
                    return true;
                }
                var quantity = TryNumber(Quantity, out var entered) && PurchaseDraftRow.ValidQuantity(created, entered) ? entered : 1m;
                var cost = TryNumber(UnitCost, out var typedCost) && PurchaseDraftRow.ValidCost(typedCost)
                    ? typedCost : created.PurchaseCost;
                AddDraftRow(created, quantity, cost);
                ResetProductEntry();
                Status = _text.Format("Purchase_ScanAdded", created.Name);
                return true;
            }
        }
        catch (Exception) { Warn("Scanner_ProductSaveError"); }
        finally
        {
            _creatingProduct = false;
            UpdateWorkflow();
        }
        return false;
    }

    [RelayCommand]
    private async Task AddItemAsync()
    {
        if (_receiving || _creatingProduct || IsSupplierEditorOpen || _lookingUp) return;
        Error = Status = null;
        if (SelectedProduct is null) await SubmitProductSearchAsync();
        if (SelectedProduct is not { } product) { Warn("Purchase_ProductRequired"); return; }
        if (!TryNumber(Quantity, out var quantity) || !PurchaseDraftRow.ValidQuantity(product, quantity))
        {
            Warn("Purchase_QuantityInvalid");
            return;
        }
        if (!TryNumber(UnitCost, out var cost) || !PurchaseDraftRow.ValidCost(cost))
        {
            Warn("Purchase_CostInvalid");
            return;
        }
        var lot = product.TrackExpiry ? BatchLot.Trim() : null;
        var expiry = product.TrackExpiry && BatchExpiry is { } date ? DateOnly.FromDateTime(date) : (DateOnly?)null;
        if (product.TrackExpiry && (lot is null || !Gs1Barcode.IsIdentifier(lot) || expiry is null))
        {
            Warn("Batch_ExpiryRequired");
            return;
        }
        try
        {
            _ = new PurchaseLine(product, quantity, cost,BuyPackages,IsPharmacy?MedicinePurchaseUnit:null);
            if (!AddDraftRow(product, quantity, cost, lot, expiry,BuyPackages,IsPharmacy?MedicinePurchaseUnit:null)) return;
        }
        catch (Exception) { Warn("Common_InvalidNumber"); return; }
        ResetProductEntry();
    }

    [RelayCommand]
    private void CancelBatchEntry()
    {
        ResetProductEntry();
        Status = Error = null;
    }

    private void ResetProductEntry()
    {
        SelectedProduct = null;
        ProductSearch = "";
        Quantity = "1";
        UnitCost = "";
    }

    private bool AddDraftRow(Product product, decimal quantity, decimal? cost, string? lot = null, DateOnly? expiry = null,bool packaged=false,string? medicineUnit=null)
    {
        IReadOnlyList<UnitIdentifiers>? units = null;
        if (product.TrackSerial)
        {
            var entry = new SerialEntryWindow(product.Name) { Owner = System.Windows.Application.Current.MainWindow };
            if (entry.ShowDialog() != true) return false;
            if (entry.Units.Count != quantity) { Error = _text["Trade_SerialsPrompt"]; return false; }
            units = entry.Units;
        }
        var row = new PurchaseDraftRow(product, quantity, FormatCost(cost), lot, expiry, units,packaged,IsPharmacy?medicineUnit??(packaged?product.PurchaseUnit:product.SellingUnit):null,IsPharmacy?DestinationLocationId:null);
        row.PropertyChanged += OnDraftRowChanged;
        Draft.Add(row);
        UpdateDraft();
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanRemoveItem))]
    private void RemoveItem()
    {
        if (SelectedLine is not { } line) return;
        line.PropertyChanged -= OnDraftRowChanged;
        Draft.Remove(line);
        UpdateDraft();
    }

    private bool CanRemoveItem() => SelectedLine is not null && !_receiving;
    private bool CanRecord() => Draft.Count > 0 && SelectedSupplier is not null &&
        !_receiving && !_lookingUp && !_creatingProduct && !IsSupplierEditorOpen;

    [RelayCommand(CanExecute = nameof(CanRecord))]
    private async Task RecordAsync()
    {
        if (!CanRecord() || auth.CurrentUser is not { } user || SelectedSupplier is not { } supplier) return;
        if (PurchaseDate is not { } date) { Warn("Purchase_DateRequired"); return; }
        Error = Status = null;
        var items = new List<PurchaseItem>();
        foreach (var row in Draft)
        {
            if (!PurchaseDraftRow.ValidQuantity(row.Product, row.Quantity)) { Warn("Purchase_QuantityInvalid"); return; }
            if (!TryNumber(row.Cost, out var cost) || !PurchaseDraftRow.ValidCost(cost)) { Warn("Purchase_CostInvalid"); return; }
            if (row.Product.TrackSerial && row.Units?.Count != row.Quantity) { Error = _text["Trade_SerialsPrompt"]; return; }
            items.Add(new PurchaseItem(row.Product.Id, row.Quantity, cost, row.BatchLot, row.BatchExpiry, row.Units,row.Packaged,row.MedicineUnit,row.LocationId,row.PackageSize,row.Product.Unit));
        }

        decimal? paid = null;
        if (PayLater)
        {
            if (!TryNumber(PaidNow, out var amount) || amount < 0 || amount != Money.Round2(amount) || amount > Draft.Sum(row => row.Amount))
            { Error = "Enter an amount paid between zero and the purchase total, with up to two decimals."; return; }
            paid = amount;
        }

        _receiving = true;
        UpdateWorkflow();
        try
        {
            var receipt = await purchases.ReceiveAsync(supplier.Id, Reference, date, Notes, items, user, _confirmation,
                initialAmountPaid: paid, paymentMethod: InitialPaymentMethod);
            _confirmation = Guid.NewGuid();
            foreach (var line in Draft) line.PropertyChanged -= OnDraftRowChanged;
            Draft.Clear();
            SelectedSupplier = null;
            Reference = Notes = "";
            PayLater = false;
            PaidNow = "0";
            PurchaseDate = DateTime.Today;
            ResetDestination();
            IsScanning = false;
            OnPropertyChanged(nameof(ScanLabel));
            UpdateDraft();
            Status = _text.Format("Purchase_Recorded", receipt.Id);
            await RefreshAsync();
            HistorySupplier = null;
            SelectedPurchase = History.FirstOrDefault(row => row.Id == receipt.Id);
        }
        catch (DomainException error) { Error = error.Message; sound.PlayWarning(); }
        catch (Exception) { Warn("Purchase_SaveError"); }
        finally
        {
            _receiving = false;
            UpdateWorkflow();
        }
    }

    private void OnDraftRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateDraft();

    private void UpdateDraft()
    {
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(MarginWarning));
        RecordCommand.NotifyCanExecuteChanged();
    }

    private void Warn(string key)
    {
        Error = _text[key];
        sound.PlayWarning();
    }

    internal static bool TryNumber(string text, out decimal number) =>
        decimal.TryParse(text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out number);

    private static string FormatCost(decimal? cost) => cost?.ToString("0.00", CultureInfo.InvariantCulture) ?? "";
}

public sealed record SupplierKindOption(string Code, string Name);

public sealed partial class PurchaseDraftRow : ObservableObject
{
    public PurchaseDraftRow(Product product, decimal quantity, string cost, string? lot = null, DateOnly? expiry = null,
        IReadOnlyList<UnitIdentifiers>? units = null,bool packaged=false,string? medicineUnit=null,int? locationId=null)
    {
        Product = product;
        _quantity = quantity;
        _cost = cost;
        BatchLot = lot;
        BatchExpiry = expiry;
        Units = units;
        Packaged=packaged;
        MedicineUnit=medicineUnit;LocationId=locationId;
        PackageSize=medicineUnit is{} selected?product.SellingUnitSize(selected):packaged?product.UnitsPerPackage:1;
    }

    public Product Product { get; }
    public bool Packaged{get;}
    public string? MedicineUnit{get;}
    public int? LocationId{get;}
    public int PackageSize{get;}
    public string UnitLabel=>LocalizationManager.Instance["Measure_"+(MedicineUnit??(Packaged?Product.PurchaseUnit:Product.Unit==Unit.Kilogram?"kg":Product.Unit==Unit.Litre?"litre":Product.SellingUnit))];
    public IReadOnlyList<UnitIdentifiers>? Units { get; }
    public int DeviceCount => Units?.Count ?? 0;
    public string? BatchLot { get; }
    public DateOnly? BatchExpiry { get; }
    public string Name => BatchLot is { } lot ? $"{Product.Name} · {lot} · {BatchExpiry:yyyy-MM-dd}" : Product.Name;
    public string Barcode => Product.Barcode;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Total))] private decimal _quantity;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Total))] private string _cost;
    public decimal Amount => PurchasesViewModel.TryNumber(Cost, out var cost) && ValidCost(cost) && ValidQuantity(Product, Quantity)
        ? Money.Round2(Quantity * cost) : 0m;
    public string Total
    {
        get
        {
            try { return MoneyText.Format(Amount); }
            catch (OverflowException) { return "—"; }
        }
    }

    public static bool ValidCost(decimal cost) => cost >= 0m && cost == Money.Round2(cost);
    public static bool ValidQuantity(Product product, decimal quantity) => quantity > 0m &&
        (product.Unit == Unit.Piece ? quantity == decimal.Truncate(quantity) : quantity == Math.Round(quantity, 3));
}

public sealed record PurchaseItemRow(PurchaseLine Item)
{
    public string Name => Item.BatchLot is { } lot ? $"{Item.ProductName} · {lot} · {Item.BatchExpiry:yyyy-MM-dd}" : Item.ProductName;
    public string Barcode => Item.Barcode;
    public string Quantity =>
        $"{Item.DisplayQuantity:0.###} " +LocalizationManager.Instance["Measure_"+(Item.UnitCode.Length>0?Item.UnitCode:Item.Unit==Unit.Kilogram?"kg":"piece")];
    public string UnitCost => MoneyText.Format(Item.DisplayUnitCost);
    public string Total => MoneyText.Format(Item.Total);
}

public sealed class PurchaseHistoryRow
{
    public PurchaseHistoryRow(Purchase purchase)
    {
        Purchase = purchase;
        Id = purchase.Id;
        SupplierId = purchase.SupplierId;
        Supplier = purchase.Supplier;
        Reference = purchase.Reference;
        ReceivedAt = purchase.ReceivedAt;
        PurchasedOn = purchase.PurchasedOn ?? purchase.ReceivedAt.Date;
        ReceivedBy = purchase.ReceivedBy;
        Notes = purchase.Notes;
        Total = MoneyText.Format(purchase.Total);
        Items = purchase.Lines.Select(item => new PurchaseItemRow(item)).ToArray();
    }

    public int Id { get; }
    public Purchase Purchase { get; }
    public string Paid => MoneyText.Format((Purchase.InitialAmountPaid ?? Purchase.Total) + Purchase.BalancePaid);
    public string Balance => MoneyText.Format(Purchase.Outstanding);
    public int? SupplierId { get; }
    public string Supplier { get; }
    public string Reference { get; }
    public DateTime ReceivedAt { get; }
    public DateTime PurchasedOn { get; }
    public string ReceivedBy { get; }
    public string Notes { get; }
    public string Status => LocalizationManager.Instance["Purchase_Received"];
    public string Total { get; }
    public IReadOnlyList<PurchaseItemRow> Items { get; }
}

public sealed record SupplierBalanceRow(int? Id, string Name, IReadOnlyList<PurchaseHistoryRow> Invoices)
{
    public string Total => MoneyText.Format(Invoices.Sum(row => row.Purchase.Total));
    public string Paid => MoneyText.Format(Invoices.Sum(row => (row.Purchase.InitialAmountPaid ?? row.Purchase.Total) + row.Purchase.BalancePaid));
    public string Balance => MoneyText.Format(Invoices.Sum(row => row.Purchase.Outstanding));
}
