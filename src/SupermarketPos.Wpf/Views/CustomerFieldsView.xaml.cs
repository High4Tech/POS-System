using System.Windows.Controls;

namespace SupermarketPos.Wpf.Views;

public partial class CustomerFieldsView : UserControl
{
    public CustomerFieldsView() => InitializeComponent();
    internal bool ShowOptionalToggle
    {
        set => OptionalToggle.Visibility = value ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }
}
