using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Collections;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Infrastructure.Business;

namespace SupermarketPos.Wpf.Views;

internal static class HistoryTable
{
    internal static async Task ShowProductSalesAsync(SaleHistory history, int id, bool pharmacy, bool devices, bool includeCustomer)
    {
        var invoices = await history.ForProductAsync(id);
        var rows = invoices.SelectMany(s => s.Lines.Where(l => l.ProductId == id).Select(l => new
        {
            Date = s.CreatedAt.ToString("g"), Receipt = s.ReceiptNo, Customer = includeCustomer ? s.CustomerName : null,
            l.Quantity, Unit = LocalizationManager.Instance["Measure_" + l.UnitCode], Price = MoneyText.Format(l.UnitPrice),
            Total = MoneyText.Format(l.LineGross), l.BatchLot, l.BatchExpiry, l.SerialNumber, l.Imei1, l.Imei2, l.WarrantyUntil
        })).ToArray();
        var columns = new List<(string, string)>
        {
            ("Reports_Date","Date"), ("Reports_ReceiptNo","Receipt"), ("Pos_Qty","Quantity"), ("Products_Unit","Unit"),
            ("Pos_SalePrice","Price"), ("Pos_LineTotal","Total")
        };
        if (includeCustomer) columns.Insert(2, ("Customer_Name", "Customer"));
        if (pharmacy && rows.Any(row => row.BatchLot is not null))
            columns.AddRange([("Batch_Lot","BatchLot"),("Batch_Expiry","BatchExpiry")]);
        if (devices && rows.Any(row => row.SerialNumber is not null || row.Imei1 is not null || row.Imei2 is not null || row.WarrantyUntil is not null))
            columns.AddRange([("Trade_Serial","SerialNumber"),("Trade_Imei1","Imei1"),("Trade_Imei2","Imei2"),("Trade_WarrantyUntil","WarrantyUntil")]);
        await ShowAsync("Trade_ProductSales", rows, columns.ToArray());
    }

    public static Task ShowAsync(string title, IEnumerable rows, params (string Header, string Property)[] columns) =>
        ShowCoreAsync(title, rows, null, null, columns);

    internal static Task ShowLedgerAsync(string title, IEnumerable rows, Func<object,string,bool> matches, Func<object,Task> openInvoice,
        params (string Header, string Property)[] columns) => ShowCoreAsync(title, rows, matches, openInvoice, columns);

    private static async Task ShowCoreAsync(string title, IEnumerable rows, Func<object,string,bool>? matches, Func<object,Task>? openInvoice,
        (string Header, string Property)[] columns)
    {
        var view = new ListCollectionView(rows.Cast<object>().ToList());
        var table = new DataGrid { ItemsSource = view, IsReadOnly = true, MaxHeight = 370 };
        foreach (var (header, property) in columns)
            table.Columns.Add(new DataGridTextColumn
            {
                Header = LocalizationManager.Instance[header],
                Binding = new Binding(property),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                ElementStyle = (Style)System.Windows.Application.Current.FindResource("TableName")
            });
        Action close = () => { };
        var panel = new DockPanel();
        var heading = BusinessForm.Label(title);
        DockPanel.SetDock(heading, Dock.Top);
        panel.Children.Add(heading);
        if (matches is not null)
        {
            var search = new TextBox { Margin = new Thickness(0, 6, 0, 8) };
            var label = LocalizationManager.Instance["Trade_LedgerFilter"];
            search.ToolTip = label;
            System.Windows.Automation.AutomationProperties.SetName(search, label);
            search.TextChanged += (_, _) =>
            {
                var term = search.Text.Trim();
                view.Filter = term.Length == 0 ? null : row => matches(row, term);
            };
            DockPanel.SetDock(search, Dock.Top);
            panel.Children.Add(search);
        }
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,10,0,0) };
        DockPanel.SetDock(actions, Dock.Bottom);
        panel.Children.Add(actions);
        object? invoice = null;
        if (openInvoice is not null)
        {
            var open = BusinessForm.Button("Reports_OpenReceipt", (_, _) =>
            {
                if (table.SelectedItem is not { } selected) return;
                invoice = selected;
                close();
            });
            open.IsEnabled = false;
            table.SelectionChanged += (_, _) => open.IsEnabled = table.SelectedItem is not null;
            actions.Children.Add(open);
        }
        actions.Children.Add(BusinessForm.Button("Common_Close", (_, _) => close(), "GhostButton"));
        panel.Children.Add(table);
        await InAppDialog.ShowAsync(panel, callback => close = callback, contentWidth: 1040, sizeToContent: true);
        if (invoice is not null && openInvoice is not null) await openInvoice(invoice);
    }
}
