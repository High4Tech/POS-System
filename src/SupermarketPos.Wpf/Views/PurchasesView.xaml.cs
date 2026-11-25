using System.Windows.Controls;
using SupermarketPos.Wpf.ViewModels;

namespace SupermarketPos.Wpf.Views;

public partial class PurchasesView : UserControl
{
    private PurchasesViewModel? _active;

    public PurchasesView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _active = DataContext as PurchasesViewModel;
            _active?.SetViewActive(true);
        };
        Unloaded += (_, _) =>
        {
            _active?.SetViewActive(false);
            _active = null;
        };
    }
}
