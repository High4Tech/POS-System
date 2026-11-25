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

    private SalesReport? _current;

    public ReportsViewModel(IReportService reports, ReceiptPreviewDialog receipts, IReportPdfService reportPdf,
                            PosStorage storage, BulkRecordService records, IAuthService auth)
    {
        _reports = reports;
        _receipts = receipts;
        _reportPdf = reportPdf;
        _storage = storage;
        _records = records;
        _auth = auth;
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
        var sales = await _reports.GetSalesAsync(HistoryFrom.Date, HistoryTo.Date.AddDays(1));
        History.Clear();
        foreach (var s in sales) History.Add(new SaleRowViewModel(s));
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
