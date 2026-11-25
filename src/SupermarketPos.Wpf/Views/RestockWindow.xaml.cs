using System.ComponentModel;
using System.Windows;
using SupermarketPos.Wpf.ViewModels;

namespace SupermarketPos.Wpf.Views;

public partial class RestockWindow : Window
{
    public RestockWindow() => InitializeComponent();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (DataContext is StockAlertsViewModel model && model.RestockCommand.IsRunning) e.Cancel = true;
        base.OnClosing(e);
    }
}
