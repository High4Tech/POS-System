using System.Windows.Controls;
using SupermarketPos.Wpf.ViewModels;

namespace SupermarketPos.Wpf.Views;

public partial class PosView : UserControl
{
    private async void ProductListClick(object sender,System.Windows.Input.MouseButtonEventArgs e)
    {
        var source=e.OriginalSource as System.Windows.DependencyObject;
        while(source is not null&&source is not DataGridRow)source=source is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D?System.Windows.Media.VisualTreeHelper.GetParent(source):System.Windows.LogicalTreeHelper.GetParent(source);
        if(source is DataGridRow{DataContext:RegisterProductTile tile}&&DataContext is PosViewModel vm)await vm.AddFromResultsCommand.ExecuteAsync(tile.Product);
    }
    private async void ProductListKey(object sender,System.Windows.Input.KeyEventArgs e)
    {
        if(e.Key==System.Windows.Input.Key.Enter&&RegisterListTable.SelectedItem is RegisterProductTile tile&&DataContext is PosViewModel vm){e.Handled=true;await vm.AddFromResultsCommand.ExecuteAsync(tile.Product);}
    }
    private PosViewModel? _activeModel;
    private void ModelChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e){if(e.PropertyName==nameof(PosViewModel.ShowPendingTax))PendingTaxColumn.Visibility=_activeModel?.ShowPendingTax==true?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;}
    public PosView()
    {
        InitializeComponent();
        PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape&&_activeModel?.ContextPanel is not null){_activeModel.CloseContextPanelCommand.Execute(null);e.Handled=true;SearchBox.Focus();}};
        Loaded += (_, _) =>
        {
            _activeModel = DataContext as PosViewModel;
            if(_activeModel is not null)_activeModel.PropertyChanged+=ModelChanged;ModelChanged(this,new(nameof(PosViewModel.ShowPendingTax)));
            _activeModel?.SetRegisterActive(true);
            SearchBox.Focus();
        };
        Unloaded += (_, _) =>
        {
            if(_activeModel is not null)_activeModel.PropertyChanged-=ModelChanged;
            _activeModel?.SetRegisterActive(false);
            _activeModel = null;
        };
    }
}
