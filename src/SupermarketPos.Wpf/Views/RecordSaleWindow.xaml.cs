using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SupermarketPos.Application.Services;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Wpf.ViewModels;

namespace SupermarketPos.Wpf.Views;

public partial class RecordSaleWindow : Window
{
    private readonly BulkRecordService _records;
    private readonly User? _operator;
    private readonly Sale? _sale;
    private readonly CustomerFieldsViewModel _customer = new();
    private readonly ObservableCollection<HistoricalItemFields> _items = [new()];
    private bool _saving;

    public RecordSaleWindow(BulkRecordService records, User? operatorUser, Sale? existingSale = null)
    {
        _records = records;
        _operator = operatorUser;
        _sale = existingSale;
        InitializeComponent();
        CustomerFields.DataContext = _customer;
        if (existingSale is not null)
        {
            Title = LocalizationManager.Instance["Customer_Details"];
            Form.SetResourceReference(WidthProperty, "CompactDialogWidth");
            SaleEntry.Visibility = Visibility.Collapsed;
            _customer.Load(existingSale.Customer);
            return;
        }
        SoldDate.SelectedDate = DateTime.Today;
        SoldTime.Text = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
        PaymentMethod.ItemsSource = Payment.RecordedMethods.Select(code =>
            new SupplierKindOption(code, LocalizationManager.Instance[Payment.LabelKey(code)])).ToArray();
        PaymentMethod.SelectedValue = "CASH";
        ItemsTable.ItemsSource = _items;
    }

    public SaleImportResult? Result { get; private set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_saving) e.Cancel = true;
        base.OnClosing(e);
    }

    private async void Save(object sender, RoutedEventArgs e)
    {
        if (_saving) return;
        _saving = true;
        SaveButton.IsEnabled = false;
        ErrorText.Text = "";
        try
        {
            if (_sale is { } existing) await _records.SaveCustomerAsync(existing.Id, _customer.Build());
            else if (_operator is { } user)
            {
                ItemsTable.CommitEdit(DataGridEditingUnit.Cell, true);
                ItemsTable.CommitEdit(DataGridEditingUnit.Row, true);
                if (SoldDate.SelectedDate is not { } day || !TimeOnly.TryParseExact(SoldTime.Text.Trim(),
                        ["HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                    throw new DomainException(LocalizationManager.Instance["History_DateTimeInvalid"]);
                var items = _items.Where(item => !item.IsEmpty).Select(item => item.Build()).ToArray();
                var method = (string)PaymentMethod.SelectedValue;
                Result = await _records.RecordSalesAsync([new HistoricalSale(ReceiptReference.Text,
                    day.Date + time.ToTimeSpan(), method,
                    method == "CASH" ? HistoricalItemFields.OptionalNumber(CashTendered.Text, "Cash tendered") : null,
                    _customer.Build(), items)], user);
            }
            _saving = false;
            DialogResult = true;
        }
        catch (Exception error) { ErrorText.Text = error.Message; }
        finally
        {
            _saving = false;
            SaveButton.IsEnabled = true;
        }
    }
}

public sealed class HistoricalItemFields
{
    public string Barcode { get; set; } = "";
    public string Name { get; set; } = "";
    public string Quantity { get; set; } = "";
    public string Price { get; set; } = "";
    public string Vat { get; set; } = "";
    public string UnitCost { get; set; } = "";
    public string ChargedGross { get; set; } = "";

    public bool IsEmpty => new[] { Barcode, Name, Quantity, Price, Vat, UnitCost, ChargedGross }.All(string.IsNullOrWhiteSpace);
    public HistoricalSaleItem Build() => new(Barcode, Name, BulkRecordService.Number(Quantity, "Qty"),
        BulkRecordService.Number(Price, "Selling price"), BulkRecordService.Number(Vat, "VAT percent"),
        OptionalNumber(UnitCost, "Unit cost"), OptionalNumber(ChargedGross, "Charged line total"));

    public static decimal? OptionalNumber(string text, string label) =>
        string.IsNullOrWhiteSpace(text) ? null : BulkRecordService.Number(text, label);
}
