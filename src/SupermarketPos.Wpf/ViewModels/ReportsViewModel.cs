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
using SupermarketPos.Infrastructure.Business;

namespace SupermarketPos.Wpf.ViewModels;

public sealed partial class ReportsViewModel : ObservableObject
{
    private readonly IReportService _reports;
    private readonly ReceiptPreviewDialog _receipts;
    private readonly IReportPdfService _reportPdf;
    private readonly PosStorage _storage;
    private readonly BulkRecordService _records;
    private readonly IAuthService _auth;
    private readonly InvoiceMarginService _margin;
    private readonly SaleHistory _history;
    private readonly ITradeLedger _ledger;
    private readonly SupermarketPos.Infrastructure.Business.ReturnsService _returns;
    [ObservableProperty] private string? _returnSummary;
    private readonly SemaphoreSlim _historyGate = new(1,1);
    private int _historyPage;
    private CancellationTokenSource? _searchDelay;
    [ObservableProperty] private bool _hasNextHistoryPage;
    [ObservableProperty] private bool _hasPreviousHistoryPage;

    private SalesReport? _current;

    public ReportsViewModel(IReportService reports, ReceiptPreviewDialog receipts, IReportPdfService reportPdf,
                            PosStorage storage, BulkRecordService records, IAuthService auth, ReturnsService returns, InvoiceMarginService margin,
                            SaleHistory history, ITradeLedger ledger)
    {
        _reports = reports;
        _receipts = receipts;
        _reportPdf = reportPdf;
        _storage = storage;
        _records = records;
        _auth = auth;
        _returns = returns;
        _margin = margin;
        _history = history;
        _ledger = ledger;
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
    private Task LoadHistoryAsync() => QueryHistoryAsync(0);

    [RelayCommand]
    private Task NextHistoryPageAsync() => HasNextHistoryPage ? QueryHistoryAsync(_historyPage + 1) : Task.CompletedTask;

    [RelayCommand]
    private Task PreviousHistoryPageAsync() => HasPreviousHistoryPage ? QueryHistoryAsync(_historyPage - 1) : Task.CompletedTask;

    private async Task QueryHistoryAsync(int page)
    {
        if (HistoryFrom.Date>HistoryTo.Date || HistoryTo.Date==DateTime.MaxValue.Date)
        { HistoryError=LocalizationManager.Instance["Dash_InvalidRange"]; return; }
        HistoryError=null;
        await _historyGate.WaitAsync();
        try
        {
            var selected = SelectedSale?.Sale.Id;
            var min = decimal.TryParse(InvoiceMinimum, out var lower) ? lower : 0;
            var max = decimal.TryParse(InvoiceMaximum, out var upper) ? upper : decimal.MaxValue;
            var results = await _history.SearchAsync(HistoryFrom.Date, HistoryTo.Date.AddDays(1), InvoiceSearch, InvoicePayment, InvoiceStatus, min, max, page);
            var sales = results.Take(SaleHistory.InvoicePageSize).ToArray();
            var posted=await _returns.PostedBetweenAsync(HistoryFrom.Date,HistoryTo.Date.AddDays(1));
            ReturnSummary=posted.Count==0?null:LocalizationManager.Instance.Format("Trade_ReturnSummary",posted.Count,
                MoneyText.Format(posted.Sum(r=>r.Gross)),MoneyText.Format(posted.Sum(r=>r.CashRefund)),
                MoneyText.Format(posted.Sum(r=>r.CardRefund)),MoneyText.Format(posted.Sum(r=>r.ReceivableReduction)),MoneyText.Format(posted.Sum(r=>r.CustomerCredit)));
            var returns = await _returns.PostedTotalsAsync(sales.Select(s=>s.Id).ToArray());
            History.Clear();
            foreach (var sale in sales)
            {
                var returned = returns.GetValueOrDefault(sale.Id);
                History.Add(new SaleRowViewModel(sale, returned.Gross, ShowMargin, returned.Quantity));
            }
            _historyPage = page;
            HasPreviousHistoryPage = page > 0;
            HasNextHistoryPage = results.Count > SaleHistory.InvoicePageSize;
            SelectedSale = History.FirstOrDefault(row => row.Sale.Id == selected);
        }
        catch(Exception) { HistoryError=LocalizationManager.Instance["Reports_LoadError"]; }
        finally { _historyGate.Release(); }
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
    partial void OnInvoiceSearchChanged(string value) => QueueHistory();
    partial void OnInvoicePaymentChanged(string value) => QueueHistory();
    partial void OnInvoiceStatusChanged(string value) => QueueHistory();
    partial void OnInvoiceMinimumChanged(string value) => QueueHistory();
    partial void OnInvoiceMaximumChanged(string value) => QueueHistory();
    private async void QueueHistory()
    {
        _searchDelay?.Cancel();
        using var request = _searchDelay = new CancellationTokenSource();
        try
        {
            await Task.Delay(160, request.Token);
            await QueryHistoryAsync(0);
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_searchDelay, request)) _searchDelay = null; }
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

    [RelayCommand]
    private async Task ViewInvoiceAuditAsync()
    {
        if (!ShowMargin || InAppDialog.IsOpen || SelectedSale is not { } row) return;
        try
        {
            var sale = row.Sale;
            var events = new List<(DateTime Date, string Action, string Actor, string Details)>
            {
                (sale.CreatedAt, "Trade_InvoicePosted", sale.CashierName, MoneyText.Format(sale.GrossTotal)),
                (sale.CreatedAt, "Reports_Payment", sale.CashierName, MoneyText.Format(sale.AmountPaid))
            };
            foreach (var payment in await _ledger.PaymentsAsync(sale.Id, false))
                events.Add((payment.PaidOn, "Trade_ReceivePayment", payment.Operator, MoneyText.Format(payment.Amount)));
            foreach (var refund in await _returns.HistoryAsync(sale.Id))
                events.Add((refund.PostedAt ?? refund.CreatedAt, "Trade_Return" + refund.Status, refund.Operator,
                    MoneyText.Format(refund.Gross) + " · " + refund.Reason));
            await HistoryTable.ShowAsync("Trade_InvoiceAudit", events.OrderBy(e => e.Date).Select(e => new
            {
                Date = e.Date.ToString("g"), Action = LocalizationManager.Instance[e.Action], e.Actor, e.Details
            }).ToArray(), ("Reports_Date","Date"), ("Shortcut_Action","Action"), ("Reports_Cashier","Actor"), ("Customer_Details","Details"));
        }
        catch (Exception) { HistoryError = LocalizationManager.Instance["Reports_LoadError"]; }
    }

    [RelayCommand]
    private async Task ViewMarginAsync()
    {
        if (!ShowMargin || InAppDialog.IsOpen || SelectedSale is not { } row) return;
        try { await InvoiceMarginPanel.ShowAsync(await _margin.ForSaleAsync(row.Sale.Id)); }
        catch (Exception) { HistoryError = LocalizationManager.Instance["Reports_LoadError"]; }
    }

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
    private async Task PrintReportAsync()
    {
        if (_current is not { } report || InAppDialog.IsOpen) return;
        try
        {
            var dialog = new System.Windows.Controls.PrintDialog();
            if (dialog.ShowDialog() != true) return;
            var pages = await _reportPdf.RenderPagesAsync(report);
            await new ReceiptPrinter().PrintAsync(pages, dialog.PrintQueue.FullName, "A4", ReportTitle ?? "Sales report");
        }
        catch (Exception) { HistoryError = LocalizationManager.Instance["Invoice_PrintFailed"]; }
    }

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
