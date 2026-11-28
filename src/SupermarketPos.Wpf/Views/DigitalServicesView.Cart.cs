using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.ViewModels;

namespace SupermarketPos.Wpf.Views;
public sealed partial class DigitalServicesView
{
    // The collection also recovers saved batches from older versions; new transactions contain one entry.
    private readonly ObservableCollection<DigitalCartLine> _cart=[];
    private Guid _checkoutKey=Guid.NewGuid();
    private bool _resumed;
    private DigitalCartLine? _editingLine;
    private readonly Dictionary<string,TextBox> _transactionFields=[];
    private Func<bool,bool>? _prepareTransaction;
    private Func<decimal>? _previewTotal;
    private TextBlock? _cartTotal,_settlementLabel;
    private TextBox? _search;
    private Button? _recordButton;
    public IAsyncRelayCommand OpenCashCommand{get;private set;}=null!;
    public IAsyncRelayCommand OpenCardCommand{get;private set;}=null!;
    public IRelayCommand ClearCartCommand{get;private set;}=null!;
    public IRelayCommand RemoveSelectedCommand{get;private set;}=null!;
    public IReadOnlyList<DigitalCartLine> Cart=>_cart;
    private bool CanCheckout=>!_busy&&_prepareTransaction is not null;
    private bool CanPayCard=>CanCheckout&&_entry.Type!="CASHOUT"&&_entry.Outcome=="Confirmed"&&!_cart.Any(l=>l.Type=="CASHOUT"||l.Outcome!="Confirmed");
    private void InitializeCartCommands()
    {
        async Task Pay(string method){if(_prepareTransaction?.Invoke(true)==true)await Run(()=>_cart.Any(r=>r.Outcome!="Confirmed")?RecordUnsettledAsync():CheckoutCartAsync(method));}
        OpenCashCommand=new AsyncRelayCommand(()=>Pay("CASH"),()=>CanCheckout);
        OpenCardCommand=new AsyncRelayCommand(()=>Pay("CARD"),()=>CanPayCard);
        ClearCartCommand=new RelayCommand(ClearCart,()=>!_busy&&HasDraft);
        RemoveSelectedCommand=new RelayCommand(()=>{if(_resumed&&_cart.Count>1&&_editingLine is{} row){RemoveRow(row);Record();}else ClearCart();},()=>!_busy&&HasDraft);
        SaveDraftCommand=new RelayCommand(()=>{if(_prepareTransaction?.Invoke(false)==true)SaveDraft();},()=>!_busy&&HasDraft&&_draftStore is not null&&_auth?.CurrentUser is not null);
        ResumeDraftCommand=new AsyncRelayCommand(()=>Run(ResumeDraftAsync),()=>!_busy&&!HasDraft&&_draftStore is not null&&_auth?.CurrentUser is not null);
    }
    private void Record()
    {
        _prepareTransaction=null;_previewTotal=null;_transactionFields.Clear();
        var allowed=new List<string>();if(_business.MobileRechargeAvailable)allowed.AddRange(["LOAD","PACKAGE"]);if(_business.DigitalWalletAvailable)allowed.Add("TRANSFER");
        if(allowed.Count==0){_page.Content=BusinessForm.Label("Service_Disabled");return;}
        if(!allowed.Contains(_entry.Type)&&_entry.Type!="CASHOUT")_entry.Type=allowed[0];
        var root=new Grid{Margin=new Thickness(16)};root.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});root.ColumnDefinitions.Add(new(){Width=new GridLength(300)});
        var left=new DockPanel{Margin=new Thickness(0,0,14,0)};root.Children.Add(left);
        var heading=new StackPanel();
        var modes=new WrapPanel{Margin=new Thickness(0,4,0,10)};
        foreach(var type in allowed)
        {
            var selected=type==_entry.Type||type=="TRANSFER"&&_entry.Type=="CASHOUT";
            var button=BusinessForm.Button("Service_"+type,(_,_)=>{_resumed=false;DropRows();_entry.Type=type;_entry.ProviderId=0;Record();},selected?"PrimaryButton":"GhostButton");
            button.IsEnabled=!(_resumed&&_cart.Count>1);modes.Children.Add(button);
        }
        heading.Children.Add(modes);DockPanel.SetDock(heading,Dock.Top);left.Children.Add(heading);
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(4,10,0,0)};
        var cash=BusinessForm.Button("Pos_Cash",(_,_)=>{},"SuccessButton");cash.Command=OpenCashCommand;cash.Name="DigitalCash";cash.IsDefault=true;actions.Children.Add(cash);
        _recordButton=cash;
        var outcome=_cart.FirstOrDefault()?.Outcome??_entry.Outcome;
        if(outcome!="Confirmed")cash.Content=T(outcome=="Pending"?"Digital_RecordPending":"Digital_RecordFailed");
        var card=BusinessForm.Button("Pos_Card",(_,_)=>{});card.Command=OpenCardCommand;card.ToolTip=T("Digital_CashOutCashOnly");actions.Children.Add(card);
        var clear=BusinessForm.Button("Digital_ClearTransaction",(_,_)=>{},"DangerButton");clear.Command=ClearCartCommand;actions.Children.Add(clear);
        DockPanel.SetDock(actions,Dock.Bottom);left.Children.Add(actions);
        FrameworkElement content;
        if(_resumed&&_cart.Count>0)
        {
            var recovery=new StackPanel();
            if(_cart.Count>1)
            {
                recovery.Children.Add(BusinessForm.Label("Digital_SavedBatchHelp"));
                var pick=new ComboBox{ItemsSource=_cart,SelectedItem=_editingLine is{} current&&_cart.Contains(current)?current:_cart[0],Margin=new Thickness(4,4,4,12)};
                var detail=new ContentControl();
                void Select(){if(pick.SelectedItem is DigitalCartLine row){_editingLine=row;detail.Content=TransactionForm(row);}}
                pick.SelectionChanged+=(_,_)=>Select();recovery.Children.Add(pick);recovery.Children.Add(detail);Select();
            }
            else recovery.Children.Add(TransactionForm(_cart[0]));
            content=new ScrollViewer{Content=recovery,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        }
        else if(_entry.Type=="PACKAGE")
        {
            var panels=new Grid();var catalog=new RowDefinition{Height=new GridLength(2,GridUnitType.Star),MinHeight=200};panels.RowDefinitions.Add(catalog);panels.RowDefinitions.Add(new(){Height=GridLength.Auto});panels.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star),MinHeight=200});PanelHeights.SetKey(panels,"digital-package-form");
            panels.Children.Add(PackageCatalog(minimum=>{catalog.MinHeight=Math.Min(440,minimum);panels.MinHeight=catalog.MinHeight+208;}));
            var divider=new GridSplitter{Height=8,HorizontalAlignment=HorizontalAlignment.Stretch,ResizeDirection=GridResizeDirection.Rows,Background=Brushes.Transparent};Grid.SetRow(divider,1);panels.Children.Add(divider);
            var detail=new Border{Margin=new Thickness(0,8,0,0),Child=_cart.FirstOrDefault() is{} package?TransactionForm(package):BusinessForm.Label("Digital_SelectPackage")};Grid.SetRow(detail,2);panels.Children.Add(detail);var viewport=new ScrollViewer{Content=panels,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};panels.SetBinding(HeightProperty,new Binding("ViewportHeight"){Source=viewport});content=viewport;
        }
        else content=EntryForm();
        var box=new Border{Child=content};box.SetResourceReference(StyleProperty,"Card");left.Children.Add(box);
        var summary=new StackPanel();_settlementLabel=BusinessForm.Label("Digital_ToCollect");summary.Children.Add(_settlementLabel);
        _cartTotal=new TextBlock();_cartTotal.SetResourceReference(StyleProperty,"BigMoney");summary.Children.Add(new Viewbox{Child=_cartTotal,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,HorizontalAlignment=HorizontalAlignment.Left});
        if(_resumed&&_cart.Count>1)summary.Children.Add(new TextBlock{Text=T("Digital_SavedBatchHelp")+" "+_cart.Count,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)});
        var side=new Border{Child=summary};side.SetResourceReference(StyleProperty,"Card");Grid.SetColumn(side,1);root.Children.Add(side);
        _page.Content=root;RefreshCartSummary();
    }
    private FrameworkElement EntryForm()
    {
        var panel=new StackPanel();var choices=new WrapPanel{Margin=new Thickness(4,0,4,10)};
        foreach(var choice in _entry.Type=="LOAD"?new[]{"Prepaid","Postpaid"}:new[]{"TRANSFER","CASHOUT"})
        {
            var radio=new RadioButton{Content=T(choice=="TRANSFER"?"Digital_OnlineTransfer":"Service_"+choice),GroupName="digital-entry-"+_accountGroup,IsChecked=_entry.Type=="LOAD"?_entry.AccountType==choice:_entry.Type==choice,Margin=new Thickness(0,0,18,0)};
            radio.Checked+=(_,_)=>{if(_entry.Type=="LOAD")_entry.AccountType=choice;else{_entry.Type=choice;DropRows();Record();}};choices.Children.Add(radio);
        }
        panel.Children.Add(choices);var form=new BusinessForm(2);var providers=_providers.Where(p=>p.IsActive&&p.AllowedTypes.Split(',').Contains(_entry.Type)).ToArray();
        if(!providers.Any(p=>p.Id==_entry.ProviderId))_entry.ProviderId=providers.FirstOrDefault()?.Id??0;
        var provider=new ComboBox{ItemsSource=providers,DisplayMemberPath="Name",SelectedValuePath="Id",SelectedValue=_entry.ProviderId};form.Field(_entry.Type=="LOAD"?"Retail_SelectNetwork":"Service_Provider","ProviderId",provider);
        TextBox Field(string label,string field,string property,int limit=40){var input=new TextBox{MaxLength=limit,FlowDirection=FlowDirection.LeftToRight};input.SetBinding(TextBox.TextProperty,new Binding(property){Source=_entry,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});form.Field(label,field,input);_transactionFields[field]=input;return input;}
        var amount=Field("Measure_Amount",nameof(DigitalCartLine.AmountText),nameof(ServiceEntry.Principal));var recipient=Field(_entry.Type=="LOAD"?"Customer_Phone":"Digital_Recipient",nameof(DigitalCartLine.Recipient),nameof(ServiceEntry.Phone));
        recipient.ToolTip=T(_entry.Type=="LOAD"?"Digital_PhoneHint":"Digital_RecipientHint");
        var fee=Field("Digital_Fee",nameof(DigitalCartLine.FeeText),nameof(ServiceEntry.CustomerFee));Field("Digital_OptionalCost",nameof(DigitalCartLine.CostText),nameof(ServiceEntry.ProviderCost));
        Field("Digital_CustomerNameOptional",nameof(DigitalCartLine.CustomerName),nameof(ServiceEntry.CustomerName),150);
        if(_entry.Type!="LOAD")form.Fields[nameof(DigitalCartLine.CostText)].Parent.SetValue(VisibilityProperty,Visibility.Collapsed);
        _search=amount;amount.Name="DigitalAmount";
        var overridden=false;var applying=false;
        ServiceProviderAccount? Selected()=>providers.FirstOrDefault(p=>p.Id==_entry.ProviderId);
        void DefaultFee(){if(overridden||Selected() is not{} account)return;applying=true;try{_entry.CustomerFee=account.CustomerCharge(DigitalCartLine.Number(_entry.Principal,out var value)?value:0).ToString("0.00",CultureInfo.InvariantCulture);fee.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();}finally{applying=false;}}
        fee.TextChanged+=(_,_)=>{if(!applying)overridden=true;};amount.TextChanged+=(_,_)=>{DefaultFee();RefreshCartSummary();};provider.SelectionChanged+=(_,_)=>{_entry.ProviderId=(int?)provider.SelectedValue??0;overridden=false;DefaultFee();};DefaultFee();
        if(_entry.Type is "TRANSFER" or "CASHOUT")form.Add("",BusinessForm.Button("Digital_DefaultCharges",async(_,_)=>{if(Selected() is{} account)await ConfigureChargesAsync(account);},"GhostButton"));
        AddOutcome(form,_entry.Outcome,value=>{_entry.Outcome=value;if(_cart.Count==1)_cart[0].Outcome=value;_entryDirty=true;RefreshCartSummary();RefreshOutcomeButton();});
        DigitalCartLine Row(ServiceProviderAccount account)=>new(){ConfirmationId=_cart.FirstOrDefault()?.ConfirmationId??Guid.NewGuid(),Type=_entry.Type,Provider=account,AccountType=_entry.Type=="LOAD"?_entry.AccountType:"",Name=T("Service_"+_entry.Type),AmountText=_entry.Principal,Recipient=_entry.Phone,FeeText=_entry.CustomerFee,FeeOverridden=overridden,CostText=_entry.ProviderCost,CustomerName=_entry.CustomerName,Outcome=_entry.Outcome};
        _prepareTransaction=validate=>
        {
            if(Selected() is not{} account){form.ShowErrors(new Dictionary<string,string>{{"ProviderId","Service_SelectProvider"}},validate);return false;}
            var row=Row(account);var errors=validate?row.Errors():new Dictionary<string,string>();form.ShowErrors(errors,validate);if(errors.Count>0)return false;
            DropRows();row.PropertyChanged+=RowChanged;_cart.Add(row);_editingLine=row;return true;
        };
        _previewTotal=()=>{var row=Selected() is{} account?Row(account):null;return row?.Total??0;};
        recipient.LostKeyboardFocus+=(_,_)=>{if(recipient.Text.Length>0&&Selected() is{} account)form.ShowErrors(Row(account).Errors().Where(e=>e.Key==nameof(DigitalCartLine.Recipient)).ToDictionary(e=>e.Key,e=>e.Value));};
        amount.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;recipient.Focus();recipient.SelectAll();}};
        foreach(var input in _transactionFields.Values.Where(t=>!ReferenceEquals(t,amount)))input.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;OpenCashCommand.Execute(null);}};
        form.FieldEdited+=_=>
        {
            // Initial dropdown/binding events do not make an empty form a pending transaction.
            _entryDirty=_entry.Principal.Length>0||_entry.Phone.Length>0||_entry.ProviderCost.Length>0||_entry.CustomerName.Length>0||
                overridden&&(!DigitalCartLine.Number(_entry.CustomerFee,out var enteredFee)||enteredFee!=Selected()?.CustomerCharge(0));
            RefreshCartSummary();
        };panel.Children.Add(form);return new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }
    private FrameworkElement TransactionForm(DigitalCartLine row)
    {
        _editingLine=row;var panel=new StackPanel();panel.Children.Add(new TextBlock{Text=row.ProviderName+" · "+row.Title,TextWrapping=TextWrapping.Wrap,FontWeight=FontWeights.Bold,Margin=new Thickness(4,0,4,12)});
        var form=new BusinessForm(2);
        TextBox Field(string label,string property,int max=40){var input=form.Text(label,row,property,true,max);_transactionFields[property]=input;input.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;OpenCashCommand.Execute(null);}};return input;}
        var recipient=Field(row.Type is "LOAD" or "PACKAGE"?"Customer_Phone":"Digital_Recipient",nameof(row.Recipient),60);recipient.Name="DigitalRecipient";
        Field("Digital_Price",nameof(row.AmountText));if(row.HasFee)Field("Digital_Fee",nameof(row.FeeText));if(row.HasCost)Field("Digital_OptionalCost",nameof(row.CostText));
        Field("Digital_CustomerNameOptional",nameof(row.CustomerName),150);panel.Children.Add(form);_search=recipient;
        if(_cart.Count==1)AddOutcome(form,row.Outcome,value=>{row.Outcome=value;_entry.Outcome=value;RefreshCartSummary();RefreshOutcomeButton();});
        _prepareTransaction=validate=>{var errors=validate?row.Errors():new Dictionary<string,string>();form.ShowErrors(errors,validate);return errors.Count==0;};
        if(row.ShowValidation)form.ShowErrors(row.Errors());form.FieldEdited+=_=>{_entryDirty=true;RefreshCartSummary();};return new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }
    private void AddOutcome(BusinessForm form,string selected,Action<string> changed)
    {
        var choices=new[]{"Confirmed","Pending","Failed"}.Select(s=>new BusinessFormChoice(s,T("Service_"+s))).ToArray();
        var pick=new ComboBox{ItemsSource=choices,DisplayMemberPath="Name",SelectedValuePath="Code",SelectedValue=selected,Name="DigitalProviderOutcome"};
        pick.SelectionChanged+=(_,_)=>{if(pick.SelectedValue is string status)changed(status);};
        form.Field("Digital_Result",nameof(DigitalCartLine.Outcome),pick,true);form.Add("",BusinessForm.Label("Digital_ResultHelp"),true);
    }
    private void RefreshOutcomeButton()
    {
        if(_recordButton is null)return;
        var status=_cart.FirstOrDefault()?.Outcome??_entry.Outcome;
        _recordButton.Content=T(status=="Pending"?"Digital_RecordPending":status=="Failed"?"Digital_RecordFailed":"Pos_Cash");
    }
    private void DropRows(){foreach(var row in _cart)row.PropertyChanged-=RowChanged;_cart.Clear();_editingLine=null;}
    private void AddRow(DigitalCartLine row){if(_cart.Count>0){row.Recipient=_cart[0].Recipient;row.CustomerName=_cart[0].CustomerName;}DropRows();row.PropertyChanged+=RowChanged;_cart.Add(row);_resumed=false;_entryDirty=true;Record();FocusRow(row,nameof(row.Recipient));}
    private void RemoveRow(DigitalCartLine row){row.PropertyChanged-=RowChanged;_cart.Remove(row);if(_cart.Count==0)_checkoutKey=Guid.NewGuid();RefreshCartSummary();}
    private void RowChanged(object? sender,PropertyChangedEventArgs e){if(e.PropertyName is nameof(DigitalCartLine.AmountText) or nameof(DigitalCartLine.FeeText) or nameof(DigitalCartLine.CostText) or nameof(DigitalCartLine.Recipient)){((DigitalCartLine)sender!).RefreshTotal();RefreshCartSummary();}}
    private void ClearCart(){DropRows();_checkoutKey=Guid.NewGuid();_resumed=false;_entry.Reset();_entryDirty=false;Record();FocusSearch();}
    public void FocusSearch()=>Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>
    {
        if(!InAppDialog.IsOpen&&_search is{IsVisible:true,IsEnabled:true} input)FocusInput(input);
    }));
    private void RefreshCartSummary()
    {
        decimal total;try{total=_previewTotal?.Invoke()??_cart.Sum(l=>l.Total);}catch(OverflowException){total=0;_status.Text=T("Service_InvalidAmount");}
        if(_cartTotal is not null)_cartTotal.Text=Money.Format(Math.Abs(total));if(_settlementLabel is not null)_settlementLabel.Text=T(total<0?"Digital_ToPay":"Digital_ToCollect");
        OpenCashCommand?.NotifyCanExecuteChanged();OpenCardCommand?.NotifyCanExecuteChanged();ClearCartCommand?.NotifyCanExecuteChanged();RemoveSelectedCommand?.NotifyCanExecuteChanged();SaveDraftCommand?.NotifyCanExecuteChanged();ResumeDraftCommand?.NotifyCanExecuteChanged();
    }
    private void FocusRow(DigitalCartLine row,string property)=>Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>
    {
        if(InAppDialog.IsOpen||!_cart.Contains(row))return;
        if(_resumed&&_cart.Count>1&&!ReferenceEquals(_editingLine,row)){_editingLine=row;Record();}
        if(_transactionFields.TryGetValue(property,out var input))FocusInput(input);
    }));
    private static void FocusInput(TextBox input)
    {
        input.Focus();input.SelectAll();input.BringIntoView();
        for(DependencyObject? parent=VisualTreeHelper.GetParent(input);parent is not null;parent=VisualTreeHelper.GetParent(parent))
            if(parent is ScrollViewer scroll)scroll.BringIntoView();
    }
}
