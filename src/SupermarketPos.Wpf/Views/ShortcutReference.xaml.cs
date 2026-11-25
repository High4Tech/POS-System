using System.Windows;
using System.Windows.Controls;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;

namespace SupermarketPos.Wpf.Views;

public partial class ShortcutReference : UserControl
{
    private Action? _close;

    internal ShortcutReference(string page, Shortcuts.ReferenceRow[] current, Shortcuts.ReferenceRow[] other)
    {
        InitializeComponent();
        PageSubtitle.Text = LocalizationManager.Instance.Format("Shortcut_Subtitle", page);
        CurrentTable.ItemsSource = current;
        OtherTable.ItemsSource = other;
        CurrentTable.Visibility = current.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        CurrentEmpty.Visibility = current.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        OtherTable.Visibility = other.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        OtherEmpty.Visibility = other.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void SetClose(Action close) => _close = close;
    private void Close(object sender, RoutedEventArgs e) => _close?.Invoke();
}
