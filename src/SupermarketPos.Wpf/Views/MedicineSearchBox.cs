using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Wpf.Localization;

namespace SupermarketPos.Wpf.Views;

// One bounded local search for Register, inventory, quick add and receiving.
public sealed class MedicineSearchBox:UserControl
{
    public TextBox Input{get;}=new();
    private readonly ListBox _matches=new(){MaxHeight=230,Visibility=Visibility.Collapsed};
    private readonly TextBlock _error=new(){TextWrapping=TextWrapping.Wrap,Visibility=Visibility.Collapsed};
    private readonly Popup _popup=new(){Placement=PlacementMode.Bottom,StaysOpen=true,AllowsTransparency=true};
    private CancellationTokenSource? _pending;
    private string? _chosenText;
    private string? _resultTerm;
    private readonly MedicineCatalogService _catalog;
    public event Action<MedicineMatch>? Selected;
    public event Action<string>? BarcodeSubmitted;
    public MedicineSearchBox(MedicineCatalogService catalog)
    {
        _catalog=catalog;var panel=new StackPanel();var entry=new Grid();entry.Children.Add(Input);var hint=new TextBlock{IsHitTestVisible=false,Margin=new Thickness(12,0,12,0),VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};hint.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("[Pharmacy_Search]"){Source=LocalizationManager.Instance});hint.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");entry.Children.Add(hint);panel.Children.Add(entry);panel.Children.Add(_error);Content=panel;
        Input.TextChanged+=(_,_)=>hint.Visibility=Input.Text.Length==0?Visibility.Visible:Visibility.Collapsed;
        _matches.SetResourceReference(FontSizeProperty,"TextSize14");_matches.SetResourceReference(FontFamilyProperty,"UiFont");_popup.PlacementTarget=Input;_popup.Child=_matches;
        void Show(bool visible){_matches.FlowDirection=Input.FlowDirection;_matches.Visibility=visible?Visibility.Visible:Visibility.Collapsed;_popup.IsOpen=visible&&IsLoaded;}
        _show=Show;Input.SizeChanged+=(_,_)=>_matches.Width=Input.ActualWidth;Input.PreviewMouseLeftButtonDown+=(_,_)=>{if(_matches.Items.Count>0&&_resultTerm==Input.Text.Trim()&&Input.Text.Trim()!=_chosenText)Show(true);};
        Input.TextChanged+=async(_,_)=>await SearchAsync();
        void Choose(){if(_resultTerm==Input.Text.Trim()&&_matches.SelectedItem is ListBoxItem{Tag:MedicineMatch m}){_chosenText=m.Name;_pending?.Cancel();Show(false);Input.Focus();Selected?.Invoke(m);}}
        _matches.MouseLeftButtonUp+=(_,e)=>{Choose();e.Handled=true;};
        _matches.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter){Choose();e.Handled=true;}};
        Input.PreviewKeyDown+=async(_,e)=>
        {
            if(e.Key==Key.Enter)
            {
                e.Handled=true;var value=Input.Text.Trim();
                if(BarcodeSubmitted is not null&&(SupermarketPos.Domain.Common.Gs1Barcode.IsMarked(value,null)||value.StartsWith("POSLOC:",StringComparison.OrdinalIgnoreCase)||value.Length>=4&&value.All(char.IsAsciiDigit)))
                {_pending?.Cancel();Show(false);BarcodeSubmitted(value);return;}
                if(_resultTerm!=value)await SearchAsync(immediately:true);
                if(Input.Text.Trim()!=value||_resultTerm!=value)return;
                if(_matches.Items.Count>0){_matches.SelectedIndex=0;Choose();}
                else if(BarcodeSubmitted is not null&&value.Length>=4&&!value.Any(char.IsWhiteSpace)&&value.Any(char.IsAsciiDigit)){Show(false);BarcodeSubmitted(value);}
            }
            else if(e.Key==Key.Down&&_matches.Visibility==Visibility.Visible&&_matches.Items.Count>0){e.Handled=true;_matches.Focus();_matches.SelectedIndex=0;}
            else if(e.Key==Key.Escape){Dismiss();e.Handled=true;}
        };
        // A non-modal popup leaves the input editable; explicitly dismiss outside its two focus scopes.
        Window? owner=null;
        void Dismiss(){_pending?.Cancel();Show(false);}
        void CheckFocus(object sender,KeyboardFocusChangedEventArgs args)=>Dispatcher.BeginInvoke(new Action(()=>{if(!Input.IsKeyboardFocusWithin&&!_matches.IsKeyboardFocusWithin)Dismiss();}));
        void OutsideClick(object sender,MouseButtonEventArgs args){if(!Input.IsMouseOver&&!_matches.IsMouseOver)Dismiss();}
        void Deactivated(object? sender,EventArgs args)=>Dismiss();
        Input.LostKeyboardFocus+=CheckFocus;_matches.LostKeyboardFocus+=CheckFocus;
        _matches.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){Dismiss();Input.Focus();e.Handled=true;}};
        Loaded+=(_,_)=>{owner=Window.GetWindow(this);if(owner is not null){owner.PreviewMouseDown+=OutsideClick;owner.Deactivated+=Deactivated;}};
        Unloaded+=(_,_)=>{Dismiss();if(owner is not null){owner.PreviewMouseDown-=OutsideClick;owner.Deactivated-=Deactivated;owner=null;}};
    }
    private readonly Action<bool> _show;
    private async Task SearchAsync(bool immediately=false)
    {
        _pending?.Cancel();var pending=_pending=new CancellationTokenSource();var term=Input.Text.Trim();_error.Text="";_error.Visibility=Visibility.Collapsed;_resultTerm=null;_matches.Items.Clear();_show(false);
        if(term.Length<1||term==_chosenText){_show(false);return;}
        _chosenText=null;
        try
        {
            if(!immediately)await Task.Delay(100,pending.Token);var matches=await _catalog.SearchAsync(term,20,pending.Token);if(pending.IsCancellationRequested)return;
            _matches.Items.Clear();
            foreach(var match in matches)
            {
                var row=new StackPanel{Margin=new Thickness(6)};
                row.Children.Add(new TextBlock{Text=match.Name,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap});
                if(match.Details.Length>0)row.Children.Add(new TextBlock{Text=match.Details,TextWrapping=TextWrapping.Wrap});
                if(match.Locations.Length>0)row.Children.Add(new TextBlock{Text=match.Locations,TextWrapping=TextWrapping.Wrap});
                row.Children.Add(new TextBlock{Text=LocalizationManager.Instance["Pharmacy_"+match.Status]+(match.Stock is{} stock?$" · {stock:0.###}":"")+(match.Price is{} price?$" · PKR {price:0.00}":""),TextWrapping=TextWrapping.Wrap,Foreground=Brushes.SlateGray});
                _matches.Items.Add(new ListBoxItem{Content=row,Tag=match,HorizontalContentAlignment=HorizontalAlignment.Stretch});
            }
            _resultTerm=term;_show(matches.Count>0&&Input.IsKeyboardFocusWithin);
        }
        catch(OperationCanceledException){}
        catch(Exception){if(!pending.IsCancellationRequested){_error.Text=LocalizationManager.Instance["Pharmacy_SearchError"];_error.Visibility=Visibility.Visible;}}
    }
}
