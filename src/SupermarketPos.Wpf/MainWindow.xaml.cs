using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SupermarketPos.Wpf.Common;

namespace SupermarketPos.Wpf;

public partial class MainWindow : Window
{
    private const double DefaultSidebarWidth = 248;
    private const double MinimumSidebarWidth = 176;
    private const double CollapsedSidebarWidth = 72;
    private readonly LayoutPreferences<SidebarLayout> _sidebarPreferences;
    private double _preferredSidebarWidth;
    private bool _collapseSidebar;

    private static readonly DependencyPropertyKey IsSidebarCollapsedPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsSidebarCollapsed), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    public static readonly DependencyProperty IsSidebarCollapsedProperty = IsSidebarCollapsedPropertyKey.DependencyProperty;
    public bool IsSidebarCollapsed => (bool)GetValue(IsSidebarCollapsedProperty);

    public MainWindow(PosStorage storage,ViewModels.ScannerController? scanner=null)
    {
        _sidebarPreferences = new(storage, "sidebar-layout.json");
        var saved = _sidebarPreferences.Read();
        _preferredSidebarWidth = saved is { Width: >= MinimumSidebarWidth } && double.IsFinite(saved.Width)
            ? saved.Width : DefaultSidebarWidth;
        _collapseSidebar = saved?.Collapsed ?? false;
        SetValue(IsSidebarCollapsedPropertyKey, _collapseSidebar);
        InitializeComponent();
        if(scanner is not null)PhysicalBarcodeInput.Attach(this,scanner);
    }

    private void ShellSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged) ApplySidebarWidth();
    }

    private void ToggleSidebar(object sender, RoutedEventArgs e)
    {
        _collapseSidebar = !IsSidebarCollapsed;
        ApplySidebarWidth();
        SaveSidebar();
    }

    private void ApplySidebarWidth()
    {
        var available = Shell.ActualWidth;
        if (available <= 0) return;
        var maximum = available / 2;
        SetValue(IsSidebarCollapsedPropertyKey, _collapseSidebar || maximum < MinimumSidebarWidth);
        SidebarToggle.IsEnabled = maximum >= MinimumSidebarWidth;
        SidebarColumn.MinWidth = Math.Min(CollapsedSidebarWidth, maximum);
        SidebarColumn.MaxWidth = maximum;
        SidebarColumn.Width = new GridLength(IsSidebarCollapsed ? SidebarColumn.MinWidth
            : Math.Clamp(_preferredSidebarWidth, MinimumSidebarWidth, maximum));
    }

    private void SidebarDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!e.Canceled && Math.Abs(e.HorizontalChange) >= 1) RememberSidebarWidth();
    }

    private void SidebarResizeKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right) RememberSidebarWidth();
    }

    private void RememberSidebarWidth()
    {
        var width = SidebarColumn.Width.Value;
        if (width < MinimumSidebarWidth)
        {
            _collapseSidebar = true;
            ApplySidebarWidth();
            SidebarToggle.Focus();
        }
        else if (width == _preferredSidebarWidth) return;
        else _preferredSidebarWidth = width;
        SaveSidebar();
    }

    private void SaveSidebar() => _sidebarPreferences.Write(new SidebarLayout(_preferredSidebarWidth, _collapseSidebar));

    private sealed record SidebarLayout(double Width, bool Collapsed);
}
