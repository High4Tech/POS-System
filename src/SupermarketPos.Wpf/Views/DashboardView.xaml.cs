using System.Windows;
using System.Windows.Controls;

namespace SupermarketPos.Wpf.Views;

public partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();

    private void DashboardSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged) return;
        var width = e.NewSize.Width;
        EarningsCards.Columns = width < 410 ? 1 : width < 760 ? 2 : 4;
        CatalogCards.Columns = width < 485 ? 1 : 3;
        var stackActions = width < 860;
        Grid.SetRow(QuickActions, stackActions ? 1 : 0);
        QuickActions.HorizontalAlignment = stackActions ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        QuickActions.Margin = new Thickness(0, stackActions ? 8 : 0, 0, 0);
    }
}
