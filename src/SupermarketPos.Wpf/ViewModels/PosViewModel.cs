using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Application.Services;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Infrastructure.Scanning;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Wpf.Views;

namespace SupermarketPos.Wpf.ViewModels;

public sealed partial class PosViewModel : ObservableObject
{
    private readonly IProductRepository _products;
    private readonly ICategoryRepository _categoryRepo;
    private readonly IBarcodeService _barcode;
    private readonly ISaleService _sales;
    private readonly ReceiptPreviewDialog _receipts;
    private readonly IAuthService _auth;
    private readonly RegisterBarcodeLookup _lookup;
    private readonly ScannerController _scanner;
    private readonly ProductsViewModel _productEditor;
    private readonly ScannerFeedback _sound;
    private readonly PosStorage _storage;
    private readonly StockAlertsViewModel _alerts;
    private readonly StoreProfile _profile;
    private readonly ISaleRepository _saleHistory;
    private readonly ITradeLedger _ledger;
    private readonly IBusinessSetup _businessSetup;
    private readonly MedicineCatalogService _medicineCatalog;
    private readonly PharmacyInventory _pharmacyInventory;
    private readonly StockLocationService _stockLocations;

    private readonly Cart _cart = new();

    public PosViewModel(IProductRepository products, ICategoryRepository categoryRepo, IBarcodeService barcode,
                        ISaleService sales, ReceiptPreviewDialog receipts, IAuthService auth, RegisterBarcodeLookup lookup,
                        ScannerController scanner, ProductsViewModel productEditor, ScannerFeedback sound, PosStorage storage,
                        StockAlertsViewModel alerts, StoreProfile profile, ISaleRepository saleHistory, ITradeLedger ledger,IBusinessSetup businessSetup,MedicineCatalogService medicineCatalog,PharmacyInventory pharmacyInventory,StockLocationService stockLocations)
    {
        _products = products;
        _categoryRepo = categoryRepo;
        _barcode = barcode;
        _sales = sales;
        _receipts = receipts;
        _auth = auth;
        _lookup = lookup;
        _scanner = scanner;
        _productEditor = productEditor;
        _sound = sound;
        _storage = storage;
        _alerts = alerts;
        _profile = profile;
        _saleHistory = saleHistory;
        _ledger = ledger;
        _businessSetup=businessSetup;
        _medicineCatalog=medicineCatalog;_pharmacyInventory=pharmacyInventory;_stockLocations=stockLocations;
        Recalculate();
    }

    public ObservableCollection<CartLineViewModel> Lines { get; } = new();
    public ObservableCollection<RegisterProductTile> ProductTiles { get; } = new();
    public ObservableCollection<CategoryTileViewModel> Categories { get; } = new();
    public ObservableCollection<VatLineViewModel> VatBreakdown { get; } = new();

    [ObservableProperty] private int _selectedCategoryId;

    partial void OnSelectedCategoryIdChanged(int value)
    {
        foreach (var category in Categories)
            category.IsSelected = category.Category.Id == value;
    }

    /// <summary>Loads the colorful category tiles; called when navigating to the POS screen.</summary>
    public async Task InitializeAsync()
    {
        Customer.SetExisting(await _saleHistory.GetCustomerSalesAsync());
        _business=(await _businessSetup.LoadAsync()).Profile;OnPropertyChanged(nameof(IsPharmacy));OnPropertyChanged(nameof(ShowProductImages));OnPropertyChanged(nameof(EnableLooseGoods));OnPropertyChanged(nameof(EnablePackagedUnits));OnPropertyChanged(nameof(EnableGoodsPanel));OnPropertyChanged(nameof(EnableDigitalServices));OnPropertyChanged(nameof(EnableMobileRecharge));
        if(IsPharmacy&&PharmacyWorkspace is null)
        {
            PharmacyWorkspace=new PharmacySalePanel(_medicineCatalog,_pharmacyInventory,_products,_stockLocations,AddMedicineAsync);
            PharmacyWorkspace.Search.BarcodeSubmitted+=async code=>{await _scanner.DispatchPhysicalAsync(code);PharmacyWorkspace.Search.Input.Clear();};
        }
        var cats = await _categoryRepo.GetAllAsync();
        Categories.Clear();
        var i = 0;
        foreach (var c in cats)
            Categories.Add(new CategoryTileViewModel(c, i++, _storage.CategoryImagePath(c.ImageFileName))
                { IsSelected = c.Id == SelectedCategoryId });
        foreach (var line in Lines)
            line.UpdateAvailability(await _products.GetCurrentForRegisterAsync(line.Line.Product.Id));
        if (SelectedCategoryId == 0) return;
        if (Categories.Any(tile => tile.IsSelected))
            ShowProductTiles(await _products.GetByCategoryAsync(SelectedCategoryId));
        else
        {
            SelectedCategoryId = 0;
            ShowProductTiles([]);
        }
    }

    [RelayCommand]
    private async Task SelectCategory(CategoryTileViewModel? tile)
    {
        if (tile is null || !CanReadBarcode()) return;
        if (SelectedCategoryId == tile.Category.Id)
        {
            SelectedCategoryId = 0;
            ShowProductTiles([]);
            Status = null;
            return;
        }

        _readingBarcode = true;
        NotifyPaymentAvailability();
        try
        {
            var items = await _products.GetByCategoryAsync(tile.Category.Id);
            if (!CanKeepScan()) return;
            SelectedCategoryId = tile.Category.Id;
            SearchText = string.Empty;
            ShowProductTiles(items);
            Status = items.Count == 0 ? Text["Pos_NotFound"] : null;
        }
        finally
        {
            _readingBarcode = false;
            NotifyPaymentAvailability();
        }
    }

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _showResults;
    [ObservableProperty] private string? _status;

    [ObservableProperty] private string _grossTotalText = MoneyText.Format(0m);
    [ObservableProperty] private string _netTotalText = MoneyText.Format(0m);
    [ObservableProperty] private string _vatTotalText = MoneyText.Format(0m);
    [ObservableProperty] private string _itemCountText = "0";

    // Cart discount inputs
    [ObservableProperty] private string _cartDiscountPercent = string.Empty;
    [ObservableProperty] private string _cartDiscountAmount = string.Empty;
    [ObservableProperty] private string? _cartDiscountText;

    // Payment overlay
    [ObservableProperty] private bool _isPaymentOpen;
    public CustomerFieldsViewModel Customer { get; } = new();
    [ObservableProperty] private bool _isCash = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCardInstructions))]
    private bool _isCredit;
    [ObservableProperty] private string _creditPaidNow = "0";
    [ObservableProperty] private DateTime? _creditDueDate;
    [ObservableProperty] private string _creditNote="";
    public bool ShowCardInstructions => !IsCash && !IsCredit;
    partial void OnIsCashChanged(bool value) => OnPropertyChanged(nameof(ShowCardInstructions));
    [ObservableProperty] private string _tendered = string.Empty;
    [ObservableProperty] private string _changeText = MoneyText.Format(0m);

    private decimal CurrentGross => PricingService.GrossTotal(_cart);

    private void ShowProductTiles(IReadOnlyList<Product> products)
    {
        ProductTiles.Clear();
        foreach (var product in products)
            ProductTiles.Add(new RegisterProductTile(product, QuantityInCart(product.Id)));
        ShowResults = ProductTiles.Count > 0;
    }

    // ---------- adding products ----------

    [RelayCommand]
    private async Task SubmitSearchAsync()
    {
        var input = _barcode.Normalize(SearchText);
        if (input.Length == 0 || !CanReadBarcode()) return;
        _readingBarcode = true;
        NotifyPaymentAvailability();
        try
        {
            var gs1 = Gs1Barcode.IsMarked(SearchText, null) ? Gs1Barcode.Parse(SearchText, Today) : null;
            if (gs1 is null && Gs1Barcode.IsMarked(SearchText, null))
            {
                Status = Text["Batch_InvalidBarcode"];
                _sound.PlayWarning();
                return;
            }
            var match = gs1 is null ? await _lookup.FindAsync(input) : await _lookup.FindGs1Async(gs1);
            if (!CanKeepScan()) return;
            if (match is null && gs1 is null && await _ledger.FindUnitAsync(input) is not null)
            {
                await ApplyBarcodeAsync(null, input);
                return;
            }
            if (match is not null || gs1 is not null)
            {
                await ApplyBarcodeAsync(match, gs1?.Gtin ?? input, gs1: gs1);
                return;
            }

            var matches = await _products.SearchByNameAsync(input);
            if (!CanKeepScan()) return;
            if (matches.Count == 1)
            {
                var selected = await _products.GetCurrentForRegisterAsync(matches[0].Id);
                if (CanKeepScan())
                    await ApplyBarcodeAsync(selected is null ? null : new BarcodeMatch(selected, null), input);
            }
            else
            {
                if (matches.Count > 0 || SelectedCategoryId == 0)
                {
                    SelectedCategoryId = 0;
                    ShowProductTiles(matches);
                }
                if (matches.Count == 0 && input.Length is >= 4 and <= 64 &&
                    input.Any(char.IsAsciiDigit) && !input.Any(char.IsWhiteSpace))
                    await ApplyBarcodeAsync(null, input);
                else
                    Status = matches.Count == 0 ? Text["Pos_NotFound"] : null;
            }
        }
        finally
        {
            _readingBarcode = false;
            NotifyPaymentAvailability();
        }
    }

    [RelayCommand]
    private async Task AddFromResultsAsync(Product? product)
    {
        if (product is null || !CanReadBarcode()) return;
        _readingBarcode = true;
        NotifyPaymentAvailability();
        try
        {
            var current = await _products.GetCurrentForRegisterAsync(product.Id);
            if (CanKeepScan())
            {
                if (current is null)
                {
                    Status = StockWarning = Text.Format("Scanner_Inactive", product.Name);
                    _sound.PlayWarning();
                }
                else await ApplyBarcodeAsync(new BarcodeMatch(current, null), product.Barcode);
            }
        }
        finally
        {
            _readingBarcode = false;
            NotifyPaymentAvailability();
        }
    }

    [RelayCommand]
    private void DecreaseFromResults(Product? product)
    {
        if (product is null || !CanReadBarcode() || FindCartLine(product.Id) is not { } line) return;
        if (line.Quantity <= 1m) RemoveLine(line);
        else line.Quantity -= 1m;
        Status = null;
    }

    private void AddProduct(Product product, decimal? quantity, InventoryBatch? batch, string? serial,bool packaged=false)
    {
        var qty = quantity ?? (product.Unit == Domain.Common.Unit.Kilogram ? 1.000m : 1m);
        var added = _cart.AddOrIncrement(product, qty, batch, serial,packaged);
        if (Lines.FirstOrDefault(item => ReferenceEquals(item.Line, added)) is { } existing)
            existing.Refresh();
        else Lines.Add(new CartLineViewModel(added, Recalculate, ApproveCartQuantity));

        Status = null;
        Recalculate();
    }

    private bool ApproveCartQuantity(CartLineViewModel line, decimal quantity)
    {
        if (quantity <= 0m || line.Line.SerialNumber is not null && quantity != (line.Line.IsPackage?line.Line.PackageSize:1m)) return false;
        if(!line.Line.Product.StockTracked&&line.IsAvailable)return true;
        if (quantity < line.Line.Quantity)
        {
            StockWarning = null;
            return true;
        }
        var other = QuantityInCart(line.Line.Product.Id, line.Line.BatchId) - line.Line.Quantity;
        if (line.IsAvailable && CartStock.CanAdd(line.AvailableStock, other, quantity))
        {
            StockWarning = other + quantity == line.AvailableStock ? Text.Format("Scanner_LastStock", line.Name) : null;
            return true;
        }
        StockWarning = !line.IsAvailable ? Text.Format("Scanner_Inactive", line.Name)
            : Text.Format("Scanner_StockQuantity", line.Name, line.AvailableStock);
        _sound.PlayWarning();
        return false;
    }

    [RelayCommand]
    private void RemoveLine(CartLineViewModel? line)
    {
        if (line is null) return;
        _cart.Remove(line.Line);
        Lines.Remove(line);
        if (_cart.IsEmpty) Customer.Load(null);
        StockWarning = null;
        Recalculate();
    }

    // ---------- discounts ----------

    [RelayCommand]
    private void ApplyCartDiscount()
    {
        Discount? discount = null;
        if (TryParse(CartDiscountPercent, out var pct) && pct > 0)
            discount = new PercentageDiscount(pct / 100m);
        else if (TryParse(CartDiscountAmount, out var amt) && amt > 0)
            discount = new FixedAmountDiscount(amt);

        _cart.ApplyCartDiscount(discount);
        CartDiscountText = discount?.Describe();
        Recalculate();
    }

    private bool _clearConfirmation;
    private bool CanClearCart() => !_cart.IsEmpty&&!_clearConfirmation&&!_checkoutBusy && !IsPaymentOpen && !_readingBarcode && !IsAddingScannedProduct;

    [RelayCommand(CanExecute = nameof(CanClearCart))]
    private async Task ClearCartAsync()
    {
        if(!CanClearCart())return;_clearConfirmation=true;UpdateScanWorkflow();NotifyPaymentAvailability();Action close=()=>{};
        try
        {
            var body=new System.Windows.Controls.StackPanel{VerticalAlignment=System.Windows.VerticalAlignment.Center};body.Children.Add(new System.Windows.Controls.TextBlock{Text=Text["Pos_ConfirmClear"],TextWrapping=System.Windows.TextWrapping.Wrap,Margin=new System.Windows.Thickness(8)});
            var actions=new System.Windows.Controls.WrapPanel();actions.Children.Add(BusinessForm.Button("Common_Cancel",(_,_)=>close(),"GhostButton"));actions.Children.Add(BusinessForm.Button("Pos_Clear",(_,_)=>{CancelBatch();Status=null;ResetCart();close();},"DangerButton"));body.Children.Add(actions);
            await InAppDialog.ShowAsync(body,c=>close=c);
        }
        finally{_clearConfirmation=false;UpdateScanWorkflow();NotifyPaymentAvailability();}
    }

    private void ResetCart()
    {
        _cart.Clear();
        Lines.Clear();
        Customer.Load(null);
        StockWarning = null;
        CartDiscountPercent = string.Empty;
        CartDiscountAmount = string.Empty;
        CartDiscountText = null;
        Recalculate();
    }

    // ---------- payment ----------

    private bool CanOpenPayment() => !_clearConfirmation&&!_cart.IsEmpty && !_readingBarcode && !IsAddingScannedProduct && BatchPrompt is null &&
        !_checkoutBusy && !Lines.Any(line => line.HasInvalidPrice);
    private bool CanStartPayment()=>!IsPaymentOpen&&CanOpenPayment();

    [RelayCommand(CanExecute = nameof(CanStartPayment))]
    private void OpenCash()
    {
        if (!CanStartPayment()) return;
        IsCash = true;
        IsCredit = false;
        Tendered = CurrentGross.ToString("0.00", CultureInfo.InvariantCulture);
        UpdateChange();
        IsPaymentOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanStartPayment))]
    private void OpenCard()
    {
        if (!CanStartPayment()) return;
        IsCash = false;
        IsCredit = false;
        IsPaymentOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanStartPayment))]
    private void OpenCredit()
    {
        if (!CanStartPayment()) return;
        IsCash = false;
        IsCredit = true;
        CreditPaidNow = "0";
        CreditDueDate=null;CreditNote="";
        Customer.IncludeDetails = true;
        IsPaymentOpen = true;
    }

    [RelayCommand]
    private void CancelPayment() => IsPaymentOpen = false;

    partial void OnTenderedChanged(string value) => UpdateChange();

    private void UpdateChange()
    {
        var change = TryParse(Tendered, out var t) ? t - CurrentGross : 0m;
        ChangeText = MoneyText.Format(change < 0 ? 0m : change);
    }

    [RelayCommand]
    private async Task CompletePaymentAsync()
    {
        if (!CanOpenPayment() || _auth.CurrentUser is null) return;
        var total = CurrentGross;
        _checkoutBusy = true;
        UpdateScanWorkflow();

        try
        {
            Payment payment = IsCredit ? new CreditPayment(total, ParseOrThrow(CreditPaidNow),CreditDueDate,CreditNote) : IsCash
                ? new CashPayment(total, ParseOrThrow(Tendered))
                : new CardPayment(total);

            var sale = await _sales.CheckoutAsync(_cart, payment, _auth.CurrentUser, customer: Customer.Build());
            IsPaymentOpen = false;
            Status = LocalizationManager.Instance.Format("Msg_SaleComplete", sale.ReceiptNo);
            ResetCart();

            await _alerts.RefreshAsync();
            Customer.SetExisting(await _saleHistory.GetCustomerSalesAsync());
            await _receipts.ShowAsync(sale);
        }
        catch (Exception ex)
        {
            _sound.PlayWarning();
            NoticeDialog.Show(ex is Domain.Business.BusinessValidationException or Infrastructure.Commercial.CommercialException ? Text[ex.Message] : ex.Message, Text["Common_Error"]);
        }
        finally
        {
            _checkoutBusy = false;
            UpdateScanWorkflow();
        }
    }

    // ---------- recompute ----------

    private void Recalculate()
    {
        var summary = PricingService.Summarize(_cart);
        GrossTotalText = MoneyText.Format(summary.GrossTotal);
        NetTotalText = MoneyText.Format(summary.NetTotal);
        VatTotalText = MoneyText.Format(summary.VatTotal);
        ItemCountText = _cart.ItemCount.ToString("0.###", CultureInfo.InvariantCulture);
        foreach (var tile in ProductTiles)
            tile.CartQuantity = QuantityInCart(tile.Product.Id);

        VatBreakdown.Clear();
        foreach (var g in summary.VatBreakdown)
            VatBreakdown.Add(new VatLineViewModel(g));

        NotifyPaymentAvailability();
        UpdateChange();
    }

    private static bool TryParse(string? s, out decimal value) =>
        decimal.TryParse((s ?? string.Empty).Replace(',', '.'), NumberStyles.Any,
            CultureInfo.InvariantCulture, out value);

    private static decimal ParseOrThrow(string s) =>
        TryParse(s, out var v) ? v : throw new SupermarketPos.Domain.Common.DomainException(LocalizationManager.Instance["Common_InvalidNumber"]);
}

public sealed class VatLineViewModel
{
    public VatLineViewModel(VatGroup g)
    {
        RateText = $"{g.Rate * 100:0.####}%";
        NetText = MoneyText.Format(g.Net);
        VatText = MoneyText.Format(g.Vat);
    }

    public string RateText { get; }
    public string NetText { get; }
    public string VatText { get; }
}

public sealed partial class RegisterProductTile(Product product, decimal cartQuantity) : ObservableObject
{
    public Product Product { get; } = product;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCartQuantity))]
    private decimal _cartQuantity = cartQuantity;

    public bool HasCartQuantity => CartQuantity > 0;
}

public sealed partial class CategoryTileViewModel : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    public Category Category { get; }
    public System.Windows.Media.Brush Color { get; }
    public string? ImagePath { get; }

    public CategoryTileViewModel(Category category, int index, string? imagePath)
    {
        Category = category;
        Color = CategoryColorConverter.TileBrush(category.ColorHex, index);
        ImagePath = imagePath;
    }

    public string Name => Category.Name;
}
