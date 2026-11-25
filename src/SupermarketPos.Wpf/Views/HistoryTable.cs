using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;

namespace SupermarketPos.Wpf.Views;

internal static class HistoryTable
{
    public static async Task ShowAsync(string title, System.Collections.IEnumerable rows, params (string Header, string Property)[] columns)
    {
        var table = new DataGrid { ItemsSource = rows, IsReadOnly = true, Height = 370 };
        foreach (var (header, property) in columns)
            table.Columns.Add(new DataGridTextColumn
            {
                Header = LocalizationManager.Instance[header],
                Binding = new Binding(property),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                ElementStyle = (Style)System.Windows.Application.Current.FindResource("TableName")
            });
        Action close = () => { };
        var panel = new StackPanel();
        panel.Children.Add(BusinessForm.Label(title));
        panel.Children.Add(table);
        panel.Children.Add(BusinessForm.Button("Common_Close", (_, _) => close(), "GhostButton"));
        await InAppDialog.ShowAsync(panel, callback => close = callback, contentWidth: 1040);
    }
}
