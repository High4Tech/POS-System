using System.Windows;
using System.Windows.Controls;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Wpf.Localization;

namespace SupermarketPos.Wpf.Views;

public partial class SerialRecordsWindow : Window
{
    private readonly ITradeLedger _ledger;
    private readonly int? _productId;
    private readonly bool _selecting;
    private readonly HashSet<string> _reserved;
    private IReadOnlyList<DeviceRow> _rows = [];
    private bool _registering;
    public string? SelectedSerial { get; private set; }

    public SerialRecordsWindow(ITradeLedger ledger, Product? product = null, bool selecting = false, IEnumerable<string>? reserved = null)
    {
        InitializeComponent();
        _ledger = ledger;
        _productId = product?.Id;
        _selecting = selecting;
        _reserved = reserved?.ToHashSet(StringComparer.Ordinal) ?? [];
        ProductTitle.Text = product?.Name ?? LocalizationManager.Instance["Trade_Devices"];
        RegisterButton.Visibility = product?.TrackSerial == true && !selecting ? Visibility.Visible : Visibility.Collapsed;
        SelectButton.Visibility = selecting ? Visibility.Visible : Visibility.Collapsed;
        Loaded += async (_, _) => await LoadAsync();
        Closing += (_, e) => e.Cancel = _registering;
    }

    private async Task LoadAsync()
    {
        try
        {
            _rows = (await _ledger.SerialsAsync(_productId)).Where(record => !_selecting ||
                record.Unit.SaleId is null && !_reserved.Contains(record.Unit.Serial)).Select(record => new DeviceRow(record)).ToArray();
            Filter();
        }
        catch (Exception error) { ErrorText.Text = error.Message; }
    }

    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        if (Devices is not null) Filter();
    }

    private void Filter()
    {
        var rows = _rows.Where(row => row.Matches(Search.Text.Trim())).ToArray();
        Devices.ItemsSource = rows;
        Devices.SelectedItem = rows.FirstOrDefault();
        EmptyText.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Select(object sender, RoutedEventArgs e)
    {
        if (Devices.SelectedItem is not DeviceRow row) return;
        SelectedSerial = row.Serial;
        DialogResult = true;
    }

    private async void Register(object sender, RoutedEventArgs e)
    {
        if (_productId is not { } id) return;
        var entry = new SerialEntryWindow(ProductTitle.Text) { Owner = this };
        if (entry.ShowDialog() != true) return;
        RegisterButton.IsEnabled = false;
        _registering = true;
        try
        {
            await _ledger.RegisterExistingAsync(id, entry.Units);
            ErrorText.Text = null;
            await LoadAsync();
        }
        catch (Exception error) { ErrorText.Text = error.Message; }
        finally { _registering = false; RegisterButton.IsEnabled = true; }
    }
}

public sealed class DeviceRow(SerialRecord record)
{
    public string ProductName => record.ProductName;
    public string Serial => record.Unit.Serial;
    public string? Imei1 => record.Unit.Imei1;
    public string? Imei2 => record.Unit.Imei2;
    public string? Receipt => record.ReceiptNo;
    public string? Customer => record.CustomerName;
    public string? Phone => record.CustomerPhone;
    public DateTime? SoldOn => record.Unit.SoldOn;
    public DateTime? WarrantyUntil => record.Unit.WarrantyUntil;
    public string Status => LocalizationManager.Instance[record.Unit.SaleId is null ? "Trade_Available" :
        WarrantyUntil is null ? "Trade_NoWarranty" : WarrantyUntil.Value.Date >= DateTime.Today ? "Trade_InWarranty" : "Trade_WarrantyExpired"];

    public bool Matches(string term) => new[] { ProductName, Serial, Imei1, Imei2, Receipt, Customer, Phone, record.CustomerEmail }
        .Any(value => value?.Contains(term, StringComparison.CurrentCultureIgnoreCase) == true);
}
