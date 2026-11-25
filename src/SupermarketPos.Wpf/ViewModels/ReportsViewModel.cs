using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Application.Dtos;
using SupermarketPos.Application.Services;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Wpf.Views;

namespace SupermarketPos.Wpf.ViewModels;

public sealed partial class ReportsViewModel : ObservableObject
{
    private readonly IReportService _reports;
    private readonly ReceiptPreviewDialog _receipts;
    private readonly IReportPdfService _reportPdf;
    private readonly PosStorage _storage;
    private readonly BulkRecordService _records;
    private readonly IAuthService _auth;
    private readonly SupermarketPos.Infrastructure.Business.ReturnsService _returns;
    [ObservableProperty] private string? _returnSummary;
    private IReadOnlyList<SaleRowViewModel> _loaded = [];

    private SalesReport? _current;

    public ReportsViewModel(IReportService reports, ReceiptPreviewDialog receipts, IReportPdfService reportPdf,
                            PosStorage storage, BulkRecordService records, IAuthService auth, SupermarketPos.Infrastructure.Business.ReturnsService returns)
    {
        _reports = reports;
        _receipts = receipts;
        _reportPdf = reportPdf;
        _storage = storage;
        _records = records;
        _auth = auth;
        _returns = returns;
        SelectedDay = DateTime.Today;
        HistoryFrom = DateTime.Today.AddDays(-7);
        HistoryTo = DateTime.Today;
    }

    public ObservableCollection<VatLineViewModel> VatBreakdown { get; } = new();
    public ObservableCollection<SaleRowViewModel> History { get; } = new();

    [ObservableProperty] private DateTime _selectedDay;
    [ObservableProperty] private DateTime _historyFrom;
    [ObservableProperty] private DateTime _historyTo;
    [ObservableProperty] private SaleRowViewModel? _selectedSale;

    [ObservableProperty] private string? _reportTitle;
    [ObservableProperty] private string _saleCountText = "0";
    [ObservableProperty] private string _grossText = MoneyText.Format(0m);
    [ObservableProperty] private string _netText = MoneyText.Format(0m);
    [ObservableProperty] private string _vatText = MoneyText.Format(0m);
    [ObservableProperty] private string _cashText = MoneyText.Format(0m);
    [ObservableProperty] private string _cardText = MoneyText.Format(0m);
    [ObservableProperty] private string? _otherText;
    [ObservableProperty] private string? _historyMessage;
    [ObservableProperty] private string? _historyError;
    [ObservableProperty] private bool _hasReport;

    [RelayCommand]
    private Task GenerateXAsync() => GenerateAsync(true);

    [RelayCommand]
    private Task GenerateZAsync() => GenerateAsync(false);

    private async Task GenerateAsync(bool isX)
    {
        _current = isX
            ? await _reports.GetXReportAsync(SelectedDay)
            : await _reports.GetZReportAsync(SelectedDay);
        Show(_current);
    }

    private void Show(SalesReport r)
    {
        ReportTitle = r.Title switch
        {
            "X-Report" => LocalizationManager.Instance["Reports_XReport"],
            "Z-Report" => LocalizationManager.Instance["Reports_ZReport"],
            _ => r.Title
        };
        SaleCountText = r.SaleCount.ToString();
        GrossText = MoneyText.Format(r.GrossTotal);
        NetText = MoneyText.Format(r.NetTotal);
        VatText = MoneyText.Format(r.VatTotal);
        CashText = $"{MoneyText.Format(r.PaymentSplit.CashTotal)} ({r.PaymentSplit.CashCount})";
        CardText = $"{MoneyText.Format(r.PaymentSplit.CardTotal)} ({r.PaymentSplit.CardCount})";
        OtherText = r.PaymentSplit.OtherCount == 0 ? null :
            $"{MoneyText.Format(r.PaymentSplit.OtherTotal)} ({r.PaymentSplit.OtherCount})";

        VatBreakdown.Clear();
        foreach (var g in r.VatBreakdown) VatBreakdown.Add(new VatLineViewModel(g));
        HasReport = true;
    }

    [RelayCommand]
    private async Task LoadHistoryAsync()
    {
        if (HistoryFrom.Date>HistoryTo.Date || HistoryTo.Date==DateTime.MaxValue.Date)
        { HistoryError=LocalizationManager.Instance["Dash_InvalidRange"]; return; }
        HistoryError=null;
        try
        {
            var sales = await _reports.GetSalesAsync(HistoryFrom.Date, HistoryTo.Date.AddDays(1));
            var posted=await _returns.PostedBetweenAsync(HistoryFrom.Date,HistoryTo.Date.AddDays(1));
            ReturnSummary=posted.Count==0?null:LocalizationManager.Instance.Format("Trade_ReturnSummary",posted.Count,
                MoneyText.Format(posted.Sum(r=>r.Gross)),MoneyText.Format(posted.Sum(r=>r.CashRefund)),
                MoneyText.Format(posted.Sum(r=>r.CardRefund)),MoneyText.Format(posted.Sum(r=>r.ReceivableReduction)),MoneyText.Format(posted.Sum(r=>r.CustomerCredit)));
            var returns = await _returns.PostedTotalsAsync(sales.Select(s=>s.Id).ToArray());
            _loaded=sales.Select(s=>new SaleRowViewModel(s,returns.GetValueOrDefault(s.Id))).ToArray();
            FilterInvoices();
        }
        catch(Exception) { HistoryError=LocalizationManager.Instance["Reports_LoadError"]; }
    }

    [ObservableProperty] private string _invoiceSearch = "";
    [ObservableProperty] private string _invoicePayment = "";
    [ObservableProperty] private string _invoiceStatus = "";
    [ObservableProperty] private string _invoiceMinimum = "";
    [ObservableProperty] private string _invoiceMaximum = "";

    public BusinessFormChoice[] InvoicePayments => new[] { new BusinessFormChoice("", LocalizationManager.Instance["Pos_All"]),
        new("CASH", LocalizationManager.Instance["Pos_Cash"]), new("CARD", LocalizationManager.Instance["Pos_Card"]), new("CREDIT", LocalizationManager.Instance["Pos_Credit"]) };
    public BusinessFormChoice[] InvoiceStatuses => new[] { new BusinessFormChoice("", LocalizationManager.Instance["Pos_All"]),
        new("open", LocalizationManager.Instance["Trade_Outstanding"]), new("paid", LocalizationManager.Instance["Trade_TotalPaid"]), new("returned", LocalizationManager.Instance["Trade_Returns"]) };
    partial void OnInvoiceSearchChanged(string value) => FilterInvoices();
    partial void OnInvoicePaymentChanged(string value) => FilterInvoices();
    partial void OnInvoiceStatusChanged(string value) => FilterInvoices();
    partial void OnInvoiceMinimumChanged(string value) => FilterInvoices();
    partial void OnInvoiceMaximumChanged(string value) => FilterInvoices();
    private void FilterInvoices()
    {
        var selected = SelectedSale?.Sale.Id;
        var term = InvoiceSearch.Trim();
        var min = decimal.TryParse(InvoiceMinimum, out var lower) ? lower : 0;
        var max = decimal.TryParse(InvoiceMaximum, out var upper) ? upper : decimal.MaxValue;
        History.Clear();
        foreach (var row in _loaded)
        {
            var sale = row.Sale;
            if (sale.GrossTotal < min || sale.GrossTotal > max || InvoicePayment.Length > 0 && sale.PaymentMethod != InvoicePayment ||
                InvoiceStatus=="open" && sale.Outstanding<=0 || InvoiceStatus=="paid" && sale.Outstanding>0 ||
                InvoiceStatus=="returned" && row.ReturnedTotal<=0) continue;
            if (term.Length==0 || new[]{sale.ReceiptNo,sale.SourceReceiptNo,sale.CustomerName,sale.CustomerPhone,sale.CashierName}
                .Any(value=>value?.Contains(term,StringComparison.CurrentCultureIgnoreCase)==true)) History.Add(row);
        }
        SelectedSale=History.FirstOrDefault(r=>r.Sale.Id==selected);
    }

    [RelayCommand]
    private async Task RepeatSelectedSaleAsync()
    {
        if(SelectedSale is { } row && System.Windows.Application.Current.MainWindow?.DataContext is MainViewModel shell)
            await shell.RepeatSaleCommand.ExecuteAsync(row.Sale);
    }

    [RelayCommand]
    private async Task ReturnItemsAsync()
    {
        if(SelectedSale is not { } row || _auth.CurrentUser?.IsAdmin!=true ||
           System.Windows.Application.Current.MainWindow?.DataContext is MainViewModel { IsReadOnly:true }) return;
        try
        {
            await ReturnInvoicePanel.ShowAsync(_returns,row.Sale);
            await LoadHistoryAsync();
        }
        catch(Exception) { HistoryError=LocalizationManager.Instance["Trade_ReturnInvalid"]; }
    }

    public bool ShowMargin => _auth.CurrentUser?.IsAdmin==true;

    public Task RefreshAsync() => LoadHistoryAsync();

    [RelayCommand]
    private void DownloadSaleTemplate()
    {
        try { BulkFiles.SaveTemplate("POS Sales History.csv", BulkRecordService.SaleTemplate); }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException) { HistoryError = error.Message; }
    }

    [RelayCommand]
    private async Task ImportSalesAsync()
    {
        if (_auth.CurrentUser is not { } user || BulkFiles.PickCsv() is not { } path) return;
        HistoryError = HistoryMessage = null;
        try
        {
            using var reader = System.IO.File.OpenText(path);
            await ShowImportedSalesAsync(await _records.ImportSalesAsync(reader, user));
        }
        catch (Exception error) { HistoryError = error.Message; }
    }

    [RelayCommand]
    private async Task RecordSaleAsync()
    {
        if (_auth.CurrentUser is not { } user) return;
        var window = new RecordSaleWindow(_records, user) { Owner = System.Windows.Application.Current.MainWindow };
        if (window.ShowDialog() == true && window.Result is { } result)
            await ShowImportedSalesAsync(result);
    }

    private async Task ShowImportedSalesAsync(SaleImportResult result)
    {
        HistoryError = null;
        HistoryMessage = LocalizationManager.Instance.Format("Bulk_SalesImported", result.Count);
        HistoryFrom = result.FirstDate;
        HistoryTo = result.LastDate;
        await LoadHistoryAsync();
    }

    [RelayCommand]
    private async Task EditCustomerAsync()
    {
        if (SelectedSale is not { } selected) return;
        var window = new RecordSaleWindow(_records, null, selected.Sale) { Owner = System.Windows.Application.Current.MainWindow };
        if (window.ShowDialog() != true) return;
        await LoadHistoryAsync();
        SelectedSale = History.FirstOrDefault(row => row.ReceiptNo == selected.ReceiptNo);
    }

    [RelayCommand]
    private async Task OpenReceiptAsync()
    {
        if (SelectedSale is null) return;
        await _receipts.ShowAsync(SelectedSale.Sale);
    }
    [RelayCommand]private Task PrintInvoiceAsync()=>InvoiceActionAsync("print");
    [RelayCommand]private Task WhatsAppInvoiceAsync()=>InvoiceActionAsync("whatsapp");
    [RelayCommand]private Task DownloadInvoiceAsync()=>InvoiceActionAsync("download");
    private Task InvoiceActionAsync(string action)=>SelectedSale is{} row?_receipts.ShowAsync(row.Sale,initialAction:action):Task.CompletedTask;

    [RelayCommand]
    private async Task ExportReportAsync()
    {
        if (_current is null) return;
        var report = _current;
        var path = _storage.ChoosePdfPath($"{report.Title}_{report.FromInclusive:yyyyMMdd}.pdf");
        if (path is not null)
        {
            await _reportPdf.SavePdfAsync(report, path);
            TryOpen(path);
        }
    }

    private static void TryOpen(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            NoticeDialog.Show(ex.Message, LocalizationManager.Instance["Common_Error"]);
        }
    }
}
