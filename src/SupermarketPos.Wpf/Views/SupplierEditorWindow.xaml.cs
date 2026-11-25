using System.ComponentModel;
using System.Windows;
using SupermarketPos.Wpf.ViewModels;

namespace SupermarketPos.Wpf.Views;

public partial class SupplierEditorWindow : Window
{
    private readonly PurchasesViewModel _model;

    public SupplierEditorWindow(PurchasesViewModel model)
    {
        _model = model;
        InitializeComponent();
        DataContext = model;
        _model.PropertyChanged += OnFormChanged;
    }

    private void OnFormChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PurchasesViewModel.IsSupplierEditorOpen) && !_model.IsSupplierEditorOpen)
            DialogResult = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_model.IsSupplierEditorOpen && _model.SaveSupplierCommand.IsRunning) e.Cancel = true;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _model.PropertyChanged -= OnFormChanged;
        base.OnClosed(e);
    }
}
