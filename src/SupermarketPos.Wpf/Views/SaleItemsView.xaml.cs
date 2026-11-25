using System.Windows;
using System.Windows.Controls;
using SupermarketPos.Wpf.ViewModels;

namespace SupermarketPos.Wpf.Views;

public partial class SaleItemsView : UserControl
{
    public SaleItemsView()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyDeviceVisibility();
        IsVisibleChanged += (_, _) => ApplyDeviceVisibility();
    }

    private void ApplyDeviceVisibility()
    {
        var visible = Window.GetWindow(this)?.DataContext is not MainViewModel { DeviceColumnsVisible: false };
        foreach (var column in new[] { SerialColumn, Imei1Column, Imei2Column, WarrantyColumn })
            column.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }
}
