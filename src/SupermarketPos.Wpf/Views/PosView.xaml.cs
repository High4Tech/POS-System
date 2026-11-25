using System.Windows.Controls;
using SupermarketPos.Wpf.ViewModels;

namespace SupermarketPos.Wpf.Views;

public partial class PosView : UserControl
{
    private PosViewModel? _activeModel;
    public PosView()
    {
        InitializeComponent();
        PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape&&_activeModel?.ContextPanel is not null){_activeModel.CloseContextPanelCommand.Execute(null);e.Handled=true;SearchBox.Focus();}};
        Loaded += (_, _) =>
        {
            _activeModel = DataContext as PosViewModel;
            _activeModel?.SetRegisterActive(true);
            SearchBox.Focus();
        };
        Unloaded += (_, _) =>
        {
            _activeModel?.SetRegisterActive(false);
            _activeModel = null;
        };
    }
}
