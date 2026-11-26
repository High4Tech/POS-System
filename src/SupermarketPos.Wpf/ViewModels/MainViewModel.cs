using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Views;
using SupermarketPos.Infrastructure.Commercial;

namespace SupermarketPos.Wpf.ViewModels;

/// <summary>
/// Shell view model: shows the login screen until authenticated, then hosts the navigable screens.
/// Both existing account types use the same store workspace.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IServiceProvider _provider;
    private readonly IAuthService _auth;
    private readonly LicenseClient _license;
    private readonly SoftwareUpdates _updates;

    private DashboardViewModel? _dashboard;
    private PosViewModel? _pos;
    private ProductsViewModel? _products;
    private PurchasesViewModel? _purchases;
    private ReportsViewModel? _reports;
    private CustomersViewModel? _customers;
    private SettingsViewModel? _settings;
    private DigitalServicesView? _digital;
    [ObservableProperty] private bool _digitalServicesVisible;
    [ObservableProperty] private bool _deviceColumnsVisible = true;
    internal bool IsPharmacy { get; private set; }
    [RelayCommand]private async Task ShowDigitalServicesAsync(string? kind)
    {
        if(!(await _provider.GetRequiredService<IBusinessSetup>().LoadAsync()).Profile.DigitalServicesAvailable)return;
        if(!_auth.IsLoggedIn)return;_digital??=_provider.GetRequiredService<DigitalServicesView>();_digital.ChooseType(kind);if(_pos is null||!ReferenceEquals(CurrentViewModel,_pos))await ShowPosAsync();_pos!.OpenDigitalPanel(_digital);
    }
    public async Task RefreshFeaturesAsync()
    {
        var business=(await _provider.GetRequiredService<IBusinessSetup>().LoadAsync()).Profile;
        IsPharmacy=business.IsPharmacy;
        DeviceColumnsVisible=business.DeviceTrackingAvailable;
        DigitalServicesVisible=business.DigitalServicesAvailable;
    }

    public MainViewModel(IServiceProvider provider, IAuthService auth, LoginViewModel login, StoreProfile storeProfile,
        ScannerController scanner, StockAlertsViewModel stockAlerts, LicenseClient license, SoftwareUpdates updates)
    {
        _provider = provider;
        _auth = auth;
        _license = license;
        _updates=updates;
        updates.Changed+=()=>System.Windows.Application.Current.Dispatcher.BeginInvoke(()=>{UpdateAvailable=updates.Available is not null;UpdateMessage=Localization.LocalizationManager.Instance[updates.Downloaded?"Updates_Downloaded":"Updates_Notice"];});
        Login = login;
        StoreProfile = storeProfile;
        Scanner = scanner;
        StockAlerts = stockAlerts;
        Login.LoggedIn += OnLoggedIn;
        CurrentViewModel = login;
    }

    public LoginViewModel Login { get; }
    public StoreProfile StoreProfile { get; }
    public bool CanManageStock => _auth.CurrentUser?.IsAdmin == true;
    public ScannerController Scanner { get; }
    public StockAlertsViewModel StockAlerts { get; }
    public bool IsReadOnly { get { try { return _license.Options.Commercial && _license.Current is null; } catch { return true; } } }
    public void RefreshLicense()=>OnPropertyChanged(nameof(IsReadOnly));
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string _updateMessage="";
    [RelayCommand] private void RemindUpdateLater()=>UpdateAvailable=false;
    [RelayCommand] private async Task DownloadSoftwareUpdateAsync()
    {
        try{await _updates.DownloadAsync();UpdateMessage=Localization.LocalizationManager.Instance["Updates_Downloaded"];}
        catch{UpdateMessage=Localization.LocalizationManager.Instance["Updates_CheckFailed"];}
    }
    public bool HasPendingWork => _pos is { } p && (p.Lines.Count > 0 || p.PharmacyWorkspace?.HasPendingEntry==true || p.IsPaymentOpen || p.CompletePaymentCommand.IsRunning) ||
        _purchases is { } buying && (buying.HasPendingEntry || buying.RecordCommand.IsRunning)||_digital?.HasDraft==true||ReceiptPrinter.IsPrinting;

    public async Task EnterConfiguredStoreAsync()
    {
        if (!_auth.IsLoggedIn) return;
        OnPropertyChanged(nameof(CanManageStock));
        IsLoggedIn = true;
        await ShowDashboardAsync();
        await StockAlerts.StartAsync();
    }

    [ObservableProperty] private object _currentViewModel;
    [ObservableProperty] private bool _isLoggedIn;
    [ObservableProperty] private string? _shortcutHint;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLowStockNotifications))]
    private string _activeScreen = "pos";

    public bool ShowLowStockNotifications => ActiveScreen is "dashboard" or "settings";

    partial void OnCurrentViewModelChanging(object value)
    {
        ShortcutHint = null;
        if (CurrentViewModel is ProductsViewModel inventory && !ReferenceEquals(inventory, value))
            inventory.SetInventoryActive(false);
        Scanner.Activate(ReferenceEquals(value, Login) ? null : value);
    }

    partial void OnCurrentViewModelChanged(object value)
    {
        if (value is ProductsViewModel inventory)
            inventory.SetInventoryActive(true);
    }

    private async void OnLoggedIn(User user)
    {
        OnPropertyChanged(nameof(CanManageStock));
        IsLoggedIn = true;
        if (IsReadOnly) await ShowDashboardAsync(); else await ShowPosAsync();
        if (_auth.IsLoggedIn) await StockAlerts.StartAsync();
    }

    [RelayCommand]
    private async Task ShowDashboardAsync()
    {
        if (!_auth.IsLoggedIn) return;
        await RefreshFeaturesAsync();
        _dashboard ??= _provider.GetRequiredService<DashboardViewModel>();
        await _dashboard.RefreshAsync();
        CurrentViewModel = _dashboard;
        ActiveScreen = "dashboard";
    }

    [RelayCommand]
    private async Task ShowPosAsync()
    {
        if (!_auth.IsLoggedIn) return;
        await RefreshFeaturesAsync();
        _pos ??= _provider.GetRequiredService<PosViewModel>();
        _pos.ReviewInventory = ReviewProductAsync;
        await _pos.InitializeAsync();
        CurrentViewModel = _pos;
        ActiveScreen = "pos";
    }

    [RelayCommand]
    private async Task RepeatSaleAsync(Sale? sale)
    {
        if (sale is null || IsReadOnly || !_auth.IsLoggedIn || InAppDialog.IsOpen) return;
        if (CurrentViewModel != _pos) await ShowPosAsync();
        if (_pos is not null) await _pos.RepeatAsync(sale);
    }

    private async Task ReviewProductAsync(int productId)
    {
        await ShowProductsAsync();
        if (_products is not null) await _products.OpenProductAsync(productId);
    }

    [RelayCommand]
    private async Task ShowProductsAsync()
    {
        if (!_auth.IsLoggedIn) return;
        _products ??= _provider.GetRequiredService<ProductsViewModel>();
        await _products.RefreshAsync();
        CurrentViewModel = _products;
        ActiveScreen = "products";
    }

    [RelayCommand]
    private async Task ShowPurchasesAsync()
    {
        if (!CanManageStock) return;
        _purchases ??= _provider.GetRequiredService<PurchasesViewModel>();
        await _purchases.RefreshAsync();
        CurrentViewModel = _purchases;
        ActiveScreen = "purchases";
    }

    [RelayCommand]
    private async Task ShowReportsAsync()
    {
        if (!_auth.IsLoggedIn) return;
        _reports ??= _provider.GetRequiredService<ReportsViewModel>();
        await _reports.RefreshAsync();
        CurrentViewModel = _reports;
        ActiveScreen = "reports";
    }

    [RelayCommand]
    private async Task ShowCustomersAsync()
    {
        if (!_auth.IsLoggedIn) return;
        _customers ??= _provider.GetRequiredService<CustomersViewModel>();
        await _customers.RefreshAsync();
        CurrentViewModel = _customers;
        ActiveScreen = "customers";
    }

    [RelayCommand]
    private async Task ShowRestockAsync(Product? product)
    {
        if (!_auth.IsLoggedIn) return;
        await StockAlerts.RefreshAsync();
        if (product is not null)
            StockAlerts.Selected = StockAlerts.Items.FirstOrDefault(item => item.Id == product.Id);
        Scanner.WhileScanPaused(() =>
            new RestockWindow { Owner = System.Windows.Application.Current.MainWindow, DataContext = StockAlerts }.ShowDialog());
        if (CurrentViewModel is ProductsViewModel inventory) await inventory.RefreshAsync();
        if (CurrentViewModel is DashboardViewModel dashboard) await dashboard.RefreshAsync();
        if (CurrentViewModel is PosViewModel register) await register.InitializeAsync();
    }

    [RelayCommand]
    private async Task ShowSettingsAsync()
    {
        if (!_auth.IsLoggedIn) return;
        _settings ??= _provider.GetRequiredService<SettingsViewModel>();
        await _settings.LoadAsync();
        await RefreshFeaturesAsync();
        CurrentViewModel = _settings;
        ActiveScreen = "settings";
    }

    [RelayCommand]
    private void Logout()
    {
        _auth.Logout();
        OnPropertyChanged(nameof(CanManageStock));
        IsLoggedIn = false;
        _dashboard = null;
        _pos = null;
        _products = null;
        _purchases = null;
        _reports = null;
        _customers = null;
        _settings = null;
        StockAlerts.Stop();
        CurrentViewModel = Login;
    }
}
