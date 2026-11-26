using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Wpf.ViewModels;

namespace SupermarketPos.Wpf;

public partial class MainWindow : Window
{
    private bool _fitQueued;

    public MainWindow(PosStorage storage, ScannerController? scanner = null)
    {
        InitializeComponent();
        if (scanner is not null) PhysicalBarcodeInput.Attach(this, scanner);
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is MainViewModel oldModel) oldModel.PropertyChanged -= ModelChanged;
            if (e.NewValue is MainViewModel model) model.PropertyChanged += ModelChanged;
            QueueFit();
        };
        Loaded += (_, _) => QueueFit();
        TopActions.SizeChanged += (_, _) => QueueFit();
        MoreButton.SizeChanged += (_, _) => QueueFit();
        LocalizationManager.Instance.PropertyChanged += LanguageChanged;
        Closed += (_, _) => LocalizationManager.Instance.PropertyChanged -= LanguageChanged;
        PreviewKeyDown += RouteShortcut;
        KeyDown += CloseInlineEditor;
    }

    private void CloseInlineEditor(object sender,KeyEventArgs e)
    {
        if(e.Key!=Key.Escape||InAppDialog.IsOpen||
            DataContext is not MainViewModel { CurrentViewModel: ProductsViewModel products } shell)return;
        if(products.IsEditorOpen&&!products.SaveCommand.IsRunning)products.CancelEditCommand.Execute(null);
        else if(products.IsCategoryEditorOpen&&!products.SaveCategoryCommand.IsRunning)products.CloseCategoryEditorCommand.Execute(null);
        else if(products.IsBulkEditorOpen&&!products.ApplyBulkEditCommand.IsRunning)products.CancelBulkEditCommand.Execute(null);
        else return;
        e.Handled=true;
        Shortcuts.TryRun(this,shell,Key.F,ModifierKeys.Control);
    }

    private void ModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.DigitalServicesVisible) or nameof(MainViewModel.IsLoggedIn) or nameof(MainViewModel.ActiveScreen) or nameof(MainViewModel.CanManageStock)) QueueFit();
    }

    private void LanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => QueueFit();
    private void FitNavigation(object sender, SizeChangedEventArgs e) => QueueFit();
    private void QueueFit()
    {
        if (_fitQueued) return;
        _fitQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            _fitQueued = false;
            if (!IsLoaded || NavigationBar.ActualWidth <= 0) return;
            var available = NavigationBar.ActualWidth - MoreButton.ActualWidth - TopActions.ActualWidth - 28;
            var active = (DataContext as MainViewModel)?.ActiveScreen;
            MoreButton.FontWeight = FontWeights.SemiBold;
            foreach (var button in Destinations.Children.OfType<Button>())
            {
                var eligible = CanShowDestination(button, DataContext as MainViewModel);
                button.Visibility = eligible ? Visibility.Visible : Visibility.Collapsed;
                if (!eligible) continue;
                button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                available -= button.DesiredSize.Width;
                if (available < 0)
                {
                    button.Visibility = Visibility.Collapsed;
                    if (Equals(button.Tag, active)) MoreButton.FontWeight = FontWeights.ExtraBold;
                }
            }
        }));
    }

    private bool CanShowDestination(Button button, MainViewModel? model) => button == DigitalDestination
        ? model?.DigitalServicesVisible == true : !Equals(button.Tag, "purchases") || model?.CanManageStock == true;

    private void OpenMore(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel model) return;
        var menu = new ContextMenu { PlacementTarget = MoreButton, Placement = PlacementMode.Bottom, FlowDirection = LocalizationManager.Instance.FlowDirection };
        foreach (var destination in Destinations.Children.OfType<Button>().Where(b => b.Visibility == Visibility.Collapsed))
        {
            if (!CanShowDestination(destination, model)) continue;
            menu.Items.Add(new MenuItem { Header = destination.Content, Command = destination.Command, IsCheckable = destination.Tag is string, IsChecked = Equals(destination.Tag, model.ActiveScreen) });
        }
        var help = new MenuItem { Header = LocalizationManager.Instance["Nav_Shortcuts"] };
        help.Click += async (_, _) =>
        {
            menu.IsOpen = false;
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            await ShowShortcutsAsync();
        };
        menu.Items.Add(help);
        menu.Items.Add(new MenuItem { Header = LocalizationManager.Instance["Nav_Logout"], Command = model.LogoutCommand });
        menu.IsOpen = true;
    }

    internal Task ShowShortcutsAsync()
    {
        if (DataContext is not MainViewModel { IsLoggedIn: true } model || InAppDialog.IsOpen ||
            model.CurrentViewModel is PosViewModel { IsPaymentOpen: true }) return Task.CompletedTask;
        var (page, current, other) = Shortcuts.Describe(this, model);
        var reference = new Views.ShortcutReference(page, current, other);
        return InAppDialog.ShowAsync(reference, reference.SetClose, owner: this, contentWidth: 620,
            sizeToContent: true, returnFocus: MoreButton);
    }

    private void RouteShortcut(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel { IsLoggedIn: true } model || InAppDialog.IsOpen) return;
        if (e.Key == Key.Escape && model.CurrentViewModel is PosViewModel { IsPaymentOpen: true } register)
        {
            if (register.CompletePaymentCommand.IsRunning) return;
            register.CancelPaymentCommand.Execute(null);
            e.Handled = true;
            return;
        }
        e.Handled = Shortcuts.TryRun(this, model, e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers);
    }
}
