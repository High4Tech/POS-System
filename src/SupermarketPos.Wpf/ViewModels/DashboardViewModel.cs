using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Application.Services;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Wpf.Views;

namespace SupermarketPos.Wpf.ViewModels;

/// <summary>Sales and invoice history for a date range alongside current catalog counts.</summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IProductRepository _products;
    private readonly ICategoryRepository _categories;
    private readonly ISettingsRepository _settings;
    private readonly IReportService _reports;
    private readonly IAuthService _auth;
    private readonly ReceiptPreviewDialog _receipts;
    private readonly BulkRecordService _records;
    private readonly SemaphoreSlim _salesRefresh = new(1, 1);

    public DashboardViewModel(IProductRepository products, ICategoryRepository categories,
                              ISettingsRepository settings, IReportService reports, IAuthService auth,
                              ReceiptPreviewDialog receipts, BulkRecordService records)
    {
        _products = products;
        _categories = categories;
        _settings = settings;
        _reports = reports;
        _auth = auth;
        _receipts = receipts;
        _records = records;
    }

    [ObservableProperty] private string _greeting = string.Empty;
    [ObservableProperty] private DateTime? _fromDate = DateTime.Today;
    [ObservableProperty] private DateTime? _toDate = DateTime.Today;
    [NotifyPropertyChangedFor(nameof(IsHistoryEmpty), nameof(ShowEmptyHistory))]
    [ObservableProperty] private IReadOnlyList<SaleRowViewModel> _history = Array.Empty<SaleRowViewModel>();
    [ObservableProperty] private SaleRowViewModel? _selectedSale;
    [NotifyPropertyChangedFor(nameof(ShowEmptyHistory))]
    [ObservableProperty] private string? _salesError;
    public bool IsHistoryEmpty => History.Count == 0;
    public bool ShowEmptyHistory => IsHistoryEmpty && SalesError is null;

    [ObservableProperty] private string _revenueText = MoneyText.Format(0m);
    [ObservableProperty] private string _saleCountText = "0";
    [ObservableProperty] private string _vatText = MoneyText.Format(0m);
    [ObservableProperty] private string _profitText = MoneyText.Format(0m);
    [ObservableProperty] private string? _profitNote;
    [ObservableProperty] private string _productsText = "0";
    [ObservableProperty] private string _categoriesText = "0";
    [ObservableProperty] private string _lowStockText = "0";

    public async Task RefreshAsync()
    {
        var now = DateTime.Now;
        var hour = now.Hour;
        var partOfDay = hour < 12 ? "Dash_Morning" : hour < 18 ? "Dash_Afternoon" : "Dash_Evening";
        var name = _auth.CurrentUser?.DisplayName ?? "";
        Greeting = LocalizationManager.Instance.Format(partOfDay, name);

        var settings = await _settings.GetAsync();
        var allProducts = await _products.GetAllAsync();
        var lowStock = await _products.GetLowStockAsync(settings.LowStockThreshold);
        var cats = await _categories.GetAllAsync();

        ProductsText = allProducts.Count.ToString();
        CategoriesText = cats.Count.ToString();
        LowStockText = lowStock.Count(product => product.IsActive).ToString();
        await RefreshSalesAsync();
    }

    [RelayCommand]
    private Task ApplyDateRangeAsync() => RefreshSalesAsync();

    [RelayCommand]
    private Task TodayAsync()
    {
        FromDate = ToDate = DateTime.Today;
        return RefreshSalesAsync();
    }

    partial void OnSelectedSaleChanged(SaleRowViewModel? value) => PreviewInvoiceCommand.NotifyCanExecuteChanged();

    private bool CanPreviewInvoice() => SelectedSale is not null;

    [RelayCommand(CanExecute = nameof(CanPreviewInvoice))]
    private async Task PreviewInvoiceAsync()
    {
        if (SelectedSale is { } selected)
            await _receipts.ShowAsync(selected.Sale);
    }

    [RelayCommand]
    private async Task EditCustomerAsync()
    {
        if (SelectedSale is not { } selected) return;
        var window = new RecordSaleWindow(_records, null, selected.Sale) { Owner = System.Windows.Application.Current.MainWindow };
        if (window.ShowDialog() == true) await RefreshSalesAsync();
    }

    private async Task RefreshSalesAsync()
    {
        await _salesRefresh.WaitAsync();
        var text = LocalizationManager.Instance;
        try
        {
            if (FromDate is not { } from || ToDate is not { } to || from.Date > to.Date)
            {
                ClearSales(text["Dash_InvalidRange"]);
                return;
            }

            var end = to.Date == DateTime.MaxValue.Date ? DateTime.MaxValue : to.Date.AddDays(1);
            var sales = await _reports.GetSalesAsync(from.Date, end);
            SalesError = null;
            var receipt = SelectedSale?.ReceiptNo;
            History = sales.Select(sale => new SaleRowViewModel(sale)).ToArray();
            SelectedSale = History.FirstOrDefault(sale => sale.ReceiptNo == receipt) ?? History.FirstOrDefault();

            RevenueText = MoneyText.Format(sales.Sum(sale => sale.GrossTotal));
            SaleCountText = sales.Count.ToString();
            VatText = MoneyText.Format(sales.Sum(sale => sale.VatTotal));
            var profits = sales.Select(sale => sale.GrossProfit).ToArray();
            var missing = profits.Count(value => !value.HasValue);
            ProfitText = profits.Length > 0 && missing == profits.Length ? "—" : MoneyText.Format(profits.Sum() ?? 0m);
            ProfitNote = missing > 0 ? text.Format("Dash_ProfitMissing", missing) : null;
        }
        catch (Exception)
        {
            ClearSales(text["Dash_LoadError"]);
        }
        finally { _salesRefresh.Release(); }
    }

    private void ClearSales(string error)
    {
        SalesError = error;
        History = Array.Empty<SaleRowViewModel>();
        SelectedSale = null;
        RevenueText = VatText = MoneyText.Format(0m);
        ProfitText = "—";
        ProfitNote = null;
        SaleCountText = "0";
    }
}
