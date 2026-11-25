using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Wpf.ViewModels;

namespace SupermarketPos.Wpf.Views;

public partial class BalancePaymentWindow : Window
{
    private readonly ITradeLedger _ledger;
    private readonly IAuthService _auth;
    private readonly int _invoiceId;
    private readonly bool _purchase;
    private readonly BalancePaymentFields _fields = new();
    private Guid _confirmation = Guid.NewGuid();
    private bool _saving;
    private decimal _balance;

    public BalancePaymentWindow(ITradeLedger ledger, IAuthService auth, Sale? sale, Purchase? purchase)
    {
        InitializeComponent();
        _ledger = ledger;
        _auth = auth;
        _purchase = purchase is not null;
        _invoiceId = purchase?.Id ?? sale!.Id;
        _balance = purchase?.Outstanding ?? sale!.Outstanding;
        _fields.Invoice = purchase is null ? sale!.ReceiptNo : $"#{purchase.Id} · {purchase.Reference}";
        _fields.Party = purchase?.Supplier ?? sale!.CustomerName ?? sale.CustomerPhone ?? sale.CustomerEmail ?? "";
        var initial = purchase?.InitialAmountPaid ?? (purchase is null ? sale!.AmountPaid : purchase.Total);
        var initialMethod = purchase?.InitialPaymentMethod ?? (sale!.PaymentMethod == "CREDIT" ? "CASH" : sale.PaymentMethod);
        _fields.InitialText = LocalizationManager.Instance["Trade_InitialPayment"] + " · " + MoneyText.Format(initial) +
            " · " + LocalizationManager.Instance[Payment.LabelKey(initialMethod)];
        _fields.BalanceText = MoneyText.Format(_balance);
        _fields.Amount = _balance.ToString("0.00", CultureInfo.InvariantCulture);
        DataContext = _fields;
        SaveButton.IsEnabled = _balance > 0;
        Loaded += async (_, _) => await LoadPaymentsAsync();
        Closing += (_, e) => e.Cancel = _saving;
    }

    private async Task LoadPaymentsAsync()
    {
        try
        {
            _fields.Payments = (await _ledger.PaymentsAsync(_invoiceId, _purchase)).Select(payment => new PaymentHistoryRow(payment)).ToArray();
        }
        catch (Exception error) { _fields.Error = error.Message; }
    }

    private async void Save(object sender, RoutedEventArgs e)
    {
        if (_saving || _auth.CurrentUser is not { } user) return;
        _saving = true;
        SaveButton.IsEnabled = false;
        try
        {
            if (!decimal.TryParse(_fields.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || _fields.PaidOn is not { } date)
                throw new InvalidOperationException(LocalizationManager.Instance["Common_InvalidNumber"]);
            await _ledger.RecordPaymentAsync(_invoiceId, _purchase, amount, _fields.Method, date, _fields.Reference, user, _confirmation);
            _balance -= amount;
            _fields.BalanceText = MoneyText.Format(_balance);
            _fields.Amount = _balance.ToString("0.00", CultureInfo.InvariantCulture);
            _fields.Error = null;
            _confirmation = Guid.NewGuid();
            await LoadPaymentsAsync();
        }
        catch (Exception error) { _fields.Error = error.Message; }
        finally { _saving = false; SaveButton.IsEnabled = _balance > 0; }
    }
}

public sealed partial class BalancePaymentFields : ObservableObject
{
    public string Invoice { get; set; } = "";
    public string Party { get; set; } = "";
    public string InitialText { get; set; } = "";
    [ObservableProperty] private string _balanceText = "";
    [ObservableProperty] private string _amount = "";
    [ObservableProperty] private string _method = "CASH";
    [ObservableProperty] private DateTime? _paidOn = DateTime.Today;
    [ObservableProperty] private string _reference = "";
    [ObservableProperty] private string? _error;
    [ObservableProperty] private IReadOnlyList<PaymentHistoryRow> _payments = [];
    public IReadOnlyList<SupplierKindOption> Methods => Payment.RecordedMethods.Select(code =>
        new SupplierKindOption(code, LocalizationManager.Instance[Payment.LabelKey(code)])).ToArray();
}

public sealed class PaymentHistoryRow(AccountPayment payment)
{
    public DateTime PaidOn => payment.PaidOn;
    public decimal Amount => payment.Amount;
    public string MethodText => LocalizationManager.Instance[Payment.LabelKey(payment.Method)];
    public string Reference => payment.Reference;
    public string RecordedBy => payment.Operator;
}
