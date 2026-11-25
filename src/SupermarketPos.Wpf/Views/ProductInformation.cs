using System.Windows;
using System.Windows.Controls;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;

namespace SupermarketPos.Wpf.Views;

internal static class ProductInformation
{
    internal static Task ShowAsync(Product product, bool pharmacy)
    {
        var text = LocalizationManager.Instance;
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = product.Name, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        Add("Products_Category", product.Category?.Name);
        Add("Products_Barcode", product.Barcode);
        Add("Pos_SalePrice", MoneyText.Format(product.UnitPrice));
        Add("Products_Stock", product.StockTracked ? product.AvailableForSale(DateOnly.FromDateTime(DateTime.Today)).ToString("0.###")
            : text["Pharmacy_StockUntracked"]);
        Add("Products_Unit", text["Measure_" + product.SellingUnit]);
        if (pharmacy)
        {
            Add("Pharmacy_Generic", product.MedicineGeneric);
            Add("Pharmacy_Strength", product.MedicineStrength);
            Add("Pharmacy_Form", product.MedicineForm);
            Add("Pharmacy_CompanyShort", product.MedicineManufacturer);
        }
        Action close = () => { };
        panel.Children.Add(BusinessForm.Button("Common_Close", (_, _) => close(), "GhostButton"));
        return InAppDialog.ShowAsync(panel, callback => close = callback, contentWidth: 460, sizeToContent: true);

        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                panel.Children.Add(new TextBlock { Text = $"{text[key]} · {value}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        }
    }
}
