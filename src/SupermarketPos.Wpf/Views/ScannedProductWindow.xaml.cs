using System.ComponentModel;
using System.Windows;
using SupermarketPos.Wpf.ViewModels;

namespace SupermarketPos.Wpf.Views;

public partial class ScannedProductWindow : Window
{
    private readonly ProductsViewModel _model;

    public ScannedProductWindow(ProductsViewModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;
        _model.PropertyChanged += CloseWhenEditorFinishes;
    }

    private void CloseWhenEditorFinishes(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ProductsViewModel.IsEditorOpen) || _model.IsEditorOpen) return;
        DialogResult = _model.ProductSaved;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_model.IsEditorOpen && _model.SaveCommand.IsRunning)
            e.Cancel = true;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _model.PropertyChanged -= CloseWhenEditorFinishes;
        if (_model.IsEditorOpen) _model.CancelEditCommand.Execute(null);
        base.OnClosed(e);
    }
}
