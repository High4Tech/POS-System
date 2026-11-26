using System.Windows.Controls;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Wpf.ViewModels;

namespace SupermarketPos.Wpf.Views;

public partial class PosView : UserControl
{
    private int _searchIndex = -1;
    private void OpenCartActions(object sender,RoutedEventArgs e)
    {
        if(sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget=button;
            menu.IsOpen=true;
        }
    }
    private static System.Collections.Generic.IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);
            if(child is T match)yield return match;
            foreach(var descendant in Children<T>(child))yield return descendant;
        }
    }
    private TextBox ProductSearch => _activeModel?.IsPharmacy==true
        ? _activeModel.PharmacyWorkspace!.Search.Input : SearchBox;
    private void FocusSearch()
    {
        ProductSearch.Focus();
        ProductSearch.SelectAll();
    }
    private void ManualProductAdded(CartLineViewModel line)
    {
        Dispatcher.InvokeAsync(() => FocusCartEditor(line,"quantity"),DispatcherPriority.Loaded);
    }
    private void FocusCartEditor(CartLineViewModel line,string field)
    {
        CartTable.SelectedItem=line;
        CartTable.ScrollIntoView(line);
        CartTable.UpdateLayout();
        if(CartTable.ItemContainerGenerator.ContainerFromItem(line) is not DataGridRow row)return;
        var editor=Children<Control>(row).FirstOrDefault(control=>Equals(control.Tag,field)&&control.IsVisible&&control.IsEnabled);
        if(editor is null){FocusSearch();return;}
        editor.Focus();
        if(editor is TextBox text)text.SelectAll();
        if(editor is ComboBox combo)combo.IsDropDownOpen=true;
    }
    private bool ValidateQuantity(TextBox text)
    {
        if(text.DataContext is not CartLineViewModel line)return false;
        if(!decimal.TryParse(text.Text,NumberStyles.Number,CultureInfo.CurrentCulture,out var value)||value<=0)
        {
            line.EditError=LocalizationManager.Instance["Pos_QuantityError"];
            line.HasInvalidQuantity=true;
            return false;
        }
        line.Quantity=value;
        if(line.Quantity!=value)
        {
            line.EditError=LocalizationManager.Instance["Pos_QuantityError"];
            line.HasInvalidQuantity=true;
            return false;
        }
        line.EditError=null;
        line.HasInvalidQuantity=false;
        text.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        return true;
    }
    private void CommitQuantity(object sender,KeyboardFocusChangedEventArgs e)
    {
        if(sender is TextBox text)ValidateQuantity(text);
    }
    private void CartEditorKey(object sender,KeyEventArgs e)
    {
        if(sender is not Control control||control.DataContext is not CartLineViewModel line)return;
        if(e.Key==Key.Escape)
        {
            if(control is TextBox text)text.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            line.EditError=null;
            line.HasInvalidQuantity=false;
            e.Handled=true;
            FocusSearch();
            return;
        }
        if(e.Key!=Key.Enter)return;
        e.Handled=true;
        if(Equals(control.Tag,"quantity"))
        {
            if(!ValidateQuantity((TextBox)control)){control.Focus();return;}
            FocusCartEditor(line,line.HasUnitChoices?"unit":line.CanEditPrice?"price":"search");
        }
        else if(Equals(control.Tag,"unit"))
        {
            ((ComboBox)control).IsDropDownOpen=false;
            if(line.EditError is not null)return;
            FocusCartEditor(line,line.CanEditPrice?"price":"search");
        }
        else if(!line.HasInvalidPrice)FocusSearch();
    }
    private async void SearchKey(object sender,KeyEventArgs e)
    {
        if(_activeModel is not { } model||model.IsPharmacy)return;
        if(e.Key is Key.Down or Key.Up)
        {
            e.Handled=true;
            if(model.ProductTiles.Count==0)return;
            _searchIndex=Math.Clamp(_searchIndex+(e.Key==Key.Down?1:-1),0,model.ProductTiles.Count-1);
            foreach(var tile in model.ProductTiles)tile.SearchSelected=false;
            model.ProductTiles[_searchIndex].SearchSelected=true;
            model.ProductPage=_searchIndex/24;
            RegisterListTable.SelectedItem=model.ProductTiles[_searchIndex];
            if(model.RegisterList)RegisterListTable.ScrollIntoView(RegisterListTable.SelectedItem);
            return;
        }
        if(e.Key==Key.Enter&&_searchIndex>=0&&_searchIndex<model.ProductTiles.Count)
        {
            e.Handled=true;
            var product=model.ProductTiles[_searchIndex].Product;
            _searchIndex=-1;
            await model.AddFromResultsCommand.ExecuteAsync(product);
        }
    }
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
    private void ModelChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e)
    {
        if(e.PropertyName==nameof(PosViewModel.SearchText))_searchIndex=-1;
        if(e.PropertyName==nameof(PosViewModel.ShowPendingTax))PendingTaxColumn.Visibility=_activeModel?.ShowPendingTax==true?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;
        if(e.PropertyName!=nameof(PosViewModel.IsPaymentOpen))return;
        Dispatcher.InvokeAsync(() =>
        {
            if(_activeModel is null || SupermarketPos.Wpf.Common.InAppDialog.IsOpen)return;
            if(_activeModel.IsPaymentOpen)
            {
                var amount=_activeModel.IsCash?CashAmountInput:_activeModel.IsCredit?CreditAmountInput:null;
                if(amount is not null){amount.Focus();amount.SelectAll();}
                else PaymentOverlay.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First));
            }
            else
                SearchBox.Focus();
        },System.Windows.Threading.DispatcherPriority.Loaded);
    }
    public PosView()
    {
        InitializeComponent();
        PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape&&_activeModel?.ContextPanel is not null){_activeModel.CloseContextPanelCommand.Execute(null);e.Handled=true;SearchBox.Focus();}};
        Loaded += (_, _) =>
        {
            _activeModel = DataContext as PosViewModel;
            if(_activeModel is not null){_activeModel.PropertyChanged+=ModelChanged;_activeModel.ManualProductAdded+=ManualProductAdded;_activeModel.CustomerSelectionClosed+=FocusSearch;}
            ModelChanged(this,new(nameof(PosViewModel.ShowPendingTax)));
            RegisterLocationsColumn.Visibility=_activeModel?.IsPharmacy==true?Visibility.Visible:Visibility.Collapsed;RetailKindColumn.Visibility=_activeModel?.IsPharmacy==true?Visibility.Collapsed:Visibility.Visible;
            _activeModel?.SetRegisterActive(true);
            FocusSearch();
        };
        Unloaded += (_, _) =>
        {
            if(_activeModel is not null){_activeModel.PropertyChanged-=ModelChanged;_activeModel.ManualProductAdded-=ManualProductAdded;_activeModel.CustomerSelectionClosed-=FocusSearch;}
            _activeModel?.SetRegisterActive(false);
            _activeModel = null;
        };
    }
}
