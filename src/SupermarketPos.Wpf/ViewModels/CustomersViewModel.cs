using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Application.Services;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Wpf.Views;

namespace SupermarketPos.Wpf.ViewModels;

public sealed partial class CustomersViewModel(ISaleRepository sales, BulkRecordService records,
    ReceiptPreviewDialog receipts, ITradeLedger ledger, IAuthService auth, SupermarketPos.Infrastructure.Business.ReturnsService returns) : ObservableObject
{
    private IReadOnlyList<CustomerRow> _all = [];
    [ObservableProperty] private IReadOnlyList<CustomerRow> _customers = [];
    [ObservableProperty] private CustomerRow? _selected;
    [ObservableProperty] private SaleRowViewModel? _selectedSale;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string? _error;

    public async Task RefreshAsync()
    {
        try
        {
            _all = (await sales.GetCustomerSalesAsync()).GroupBy(Identity)
                .Select(group => new CustomerRow(group.Key, group.ToArray())).ToArray();
            Filter();
            Error = null;
        }
        catch (Exception) { Error = LocalizationManager.Instance["Customers_LoadError"]; }
    }

    partial void OnSearchChanged(string value) => Filter();
    partial void OnSelectedChanged(CustomerRow? value) => SelectedSale = value?.Sales.FirstOrDefault();
    partial void OnSelectedSaleChanged(SaleRowViewModel? value)
    {
        OpenReceiptCommand.NotifyCanExecuteChanged();
        EditCustomerCommand.NotifyCanExecuteChanged();
        ReceivePaymentCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedSale))]
    private async Task ReceivePaymentAsync()
    {
        if (SelectedSale is not { } row) return;
        new BalancePaymentWindow(ledger, auth, row.Sale, null) { Owner = System.Windows.Application.Current.MainWindow }.ShowDialog();
        await RefreshAsync();
    }

    private void Filter()
    {
        var key = Selected?.Key;
        var search = Search.Trim();
        Customers = _all.Where(row => row.Matches(search)).ToArray();
        Selected = Customers.FirstOrDefault(row => row.Key == key) ?? Customers.FirstOrDefault();
    }

    [RelayCommand]
    private async Task ViewLedgerAsync()
    {
        if (Selected is not { } customer || InAppDialog.IsOpen) return;
        try
        {
            var events = new List<(DateTime Date, string Reference, string Kind, decimal Debit, decimal Credit)>();
            foreach (var row in customer.Sales)
            {
                var sale = row.Sale;
                events.Add((sale.CreatedAt, sale.ReceiptNo, "Reports_History", sale.GrossTotal, 0));
                if (sale.AmountPaid > 0) events.Add((sale.CreatedAt, sale.ReceiptNo, "Reports_Payment", 0, sale.AmountPaid));
                foreach (var payment in await ledger.PaymentsAsync(sale.Id, false))
                    events.Add((payment.PaidOn, sale.ReceiptNo, "Reports_Payment", 0, payment.Amount));
                foreach (var refund in (await returns.HistoryAsync(sale.Id)).Where(r=>r.Status=="Posted"))
                {
                    var refunded=refund.CashRefund+refund.CardRefund;
                    events.Add((refund.PostedAt??refund.CreatedAt,sale.ReceiptNo,"Trade_Returns",0,refund.Gross));
                    if(refunded>0)events.Add((refund.PostedAt??refund.CreatedAt,sale.ReceiptNo,"Trade_RefundPaid",refunded,0));
                }
            }
            decimal balance = 0;
            var rows = events.OrderBy(e=>e.Date.Date).ThenBy(e=>e.Date).ThenByDescending(e=>e.Debit).Select(e =>
            {
                balance += e.Debit - e.Credit;
                return new { Date=e.Date.ToString("g"), e.Reference, Type=LocalizationManager.Instance[e.Kind], Debit=MoneyText.Format(e.Debit), Credit=MoneyText.Format(e.Credit), Balance=MoneyText.Format(balance) };
            }).ToArray();
            await HistoryTable.ShowAsync("Trade_CustomerLedger",rows,("Reports_Date","Date"),("Reports_ReceiptNo","Reference"),("Reports_Payment","Type"),("Trade_Debit","Debit"),("Trade_CreditAmount","Credit"),("Trade_Balance","Balance"));
        }
        catch (Exception) { Error = LocalizationManager.Instance["Customers_LoadError"]; }
    }

    [RelayCommand]
    private async Task RepeatSelectedSaleAsync()
    {
        if (SelectedSale is not { } row || System.Windows.Application.Current.MainWindow?.DataContext is not MainViewModel shell) return;
        try { await shell.RepeatSaleCommand.ExecuteAsync(await sales.GetByIdAsync(row.Sale.Id)); }
        catch (Exception) { Error = LocalizationManager.Instance["Trade_RepeatError"]; }
    }

    private bool HasSelectedSale() => SelectedSale is not null;

    [RelayCommand(CanExecute = nameof(HasSelectedSale))]
    private async Task OpenReceiptAsync()
    {
        if (SelectedSale is { } row && await sales.GetByIdAsync(row.Sale.Id) is { } sale)
            await receipts.ShowAsync(sale);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedSale))]
    private async Task EditCustomerAsync()
    {
        if (SelectedSale is not { } row) return;
        var window = new RecordSaleWindow(records, null, row.Sale) { Owner = System.Windows.Application.Current.MainWindow };
        if (window.ShowDialog() == true) await RefreshAsync();
    }

    private static string Identity(Sale sale)
    {
        if (sale.CustomerAccountKey is { } account) return account;
        if (!string.IsNullOrWhiteSpace(sale.CustomerEmail))
            return "email:" + sale.CustomerEmail.Trim().ToUpperInvariant();
        var digits = sale.CustomerPhone?.Where(char.IsAsciiDigit).ToArray() ?? [];
        return digits.Length >= 7 ? "phone:" + new string(digits) : "sale:" + sale.Id;
    }
}

public sealed class CustomerRow
{
    public CustomerRow(string key, IReadOnlyList<Sale> sales)
    {
        Key = key;
        Name = sales.Select(s => s.CustomerName).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
        Phone = sales.Select(s => s.CustomerPhone).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
        Email = sales.Select(s => s.CustomerEmail).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
        Address = sales.Select(s => s.CustomerAddress).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
        Sales = sales.Select(sale => new SaleRowViewModel(sale)).ToArray();
        Total = MoneyText.Format(sales.Sum(sale => sale.GrossTotal));
        Balance = MoneyText.Format(sales.Sum(sale => sale.Outstanding));
        Paid = MoneyText.Format(sales.Sum(sale => sale.AmountPaid + sale.BalancePaid));
    }

    public string Key { get; }
    public string? Name { get; }
    public string? Phone { get; }
    public string? Email { get; }
    public string? Address { get; }
    public int SaleCount => Sales.Count;
    public string Total { get; }
    public string Balance { get; }
    public string Paid { get; }
    public IReadOnlyList<SaleRowViewModel> Sales { get; }

    public bool Matches(string query) => query.Length == 0 ||
        new[] { Name, Phone, Email, Address }.Any(s => s?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true) ||
        Sales.Any(sale => sale.ReceiptNo.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                          sale.SourceReceiptNo?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);
}
