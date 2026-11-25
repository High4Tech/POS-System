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
        var shell = Window.GetWindow(this)?.DataContext as MainViewModel;
        UnitCostColumn.Visibility = shell?.CanManageStock == true ? Visibility.Visible : Visibility.Collapsed;
        var visible = shell is not { DeviceColumnsVisible: false };
        foreach (var column in new[] { SerialColumn, Imei1Column, Imei2Column, WarrantyColumn })
            column.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }
}
