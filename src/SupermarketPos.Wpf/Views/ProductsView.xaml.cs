using System.Windows.Controls;
using System.Windows;
using SupermarketPos.Wpf.ViewModels;
using Forms = System.Windows.Forms;

namespace SupermarketPos.Wpf.Views;

public partial class ProductsView : UserControl
{
    private void OpenMore(object sender,System.Windows.RoutedEventArgs e){if(sender is Button{ContextMenu:{} menu} button){menu.PlacementTarget=button;menu.IsOpen=true;}}
    public ProductsView() => InitializeComponent();

    private void ChooseCategoryColor(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProductsViewModel model) return;
        using var picker = new Forms.ColorDialog
        {
            FullOpen = true,
            Color = System.Drawing.ColorTranslator.FromHtml(model.CategoryColor)
        };
        if (picker.ShowDialog() == Forms.DialogResult.OK)
            model.CategoryColor = $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}";
    }
}
