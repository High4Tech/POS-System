using System.Windows;
using System.Windows.Controls;
using SupermarketPos.Application.Services;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;

namespace SupermarketPos.Wpf.Views;

internal static class InvoiceMarginPanel
{
    internal static Task ShowAsync(InvoiceMargin margin)
    {
        var text = LocalizationManager.Instance;
        var panel = new StackPanel();
        var title = BusinessForm.Label("Trade_ViewMargin");
        title.SetResourceReference(FrameworkElement.StyleProperty, "H1");
        panel.Children.Add(title);
        Add("Reports_Net", MoneyText.Format(margin.NetSales));
        Add("Trade_InvoiceCost", margin.Cost is { } cost ? MoneyText.Format(cost) : "—");
        Add("Dash_GrossProfit", margin.Profit is { } profit ? MoneyText.Format(profit) : "—");
        Add("Trade_MarginPercent", margin.Percent is { } percent ? $"{percent:0.##}%" : "—");
        if (margin.MissingCosts > 0)
            panel.Children.Add(new TextBlock { Text = text.Format("Trade_MissingMarginCosts", margin.MissingCosts), TextWrapping = TextWrapping.Wrap });
        Action close = () => { };
        panel.Children.Add(BusinessForm.Button("Common_Close", (_, _) => close(), "GhostButton"));
        return InAppDialog.ShowAsync(panel, callback => close = callback, contentWidth: 450, sizeToContent: true);

        void Add(string label, string value) => panel.Children.Add(new TextBlock
        {
            Text = $"{text[label]} · {value}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0)
        });
    }
}
