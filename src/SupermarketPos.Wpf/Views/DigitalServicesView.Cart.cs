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
using SupermarketPos.Wpf.Common;

namespace SupermarketPos.Wpf.Views;

public sealed partial class DigitalServicesView
{
    private readonly ObservableCollection<DigitalCartLine> _cart=[];
    private Guid _checkoutKey=Guid.NewGuid();
    private DataGrid? _cartTable;
    private TextBlock? _cartTotal,_cartCount,_settlementLabel;
    private TextBox? _search;
    private Button? _cashButton,_cardButton,_clearButton;
    public IAsyncRelayCommand OpenCashCommand{get;private set;}=null!;
    public IAsyncRelayCommand OpenCardCommand{get;private set;}=null!;
    public IRelayCommand ClearCartCommand{get;private set;}=null!;
    public IRelayCommand RemoveSelectedCommand{get;private set;}=null!;
    public IReadOnlyList<DigitalCartLine> Cart=>_cart;
    private bool CanCheckout=>!_busy&&_cart.Count>0;
    private bool CanPayCard=>CanCheckout&&!_cart.Any(l=>l.Type=="CASHOUT");
    private void InitializeCartCommands()
    {
        OpenCashCommand=new AsyncRelayCommand(()=>Run(()=>CheckoutCartAsync("CASH")),()=>CanCheckout);
        OpenCardCommand=new AsyncRelayCommand(()=>Run(()=>CheckoutCartAsync("CARD")),()=>CanPayCard);
        ClearCartCommand=new RelayCommand(ClearCart,()=>!_busy&&HasDraft);
        RemoveSelectedCommand=new RelayCommand(()=>{if(_cartTable?.SelectedItem is DigitalCartLine row)RemoveRow(row);},()=>!_busy&&_cartTable?.SelectedItem is DigitalCartLine);
        SaveDraftCommand=new RelayCommand(SaveDraft,()=>CanCheckout&&_draftStore is not null&&_auth?.CurrentUser is not null);
        ResumeDraftCommand=new AsyncRelayCommand(()=>Run(ResumeDraftAsync),()=>!_busy&&!HasDraft&&_draftStore is not null&&_auth?.CurrentUser is not null);
    }
    private void Record()
    {
        var allowed=new List<string>();if(_business.MobileRechargeAvailable)allowed.AddRange(["LOAD","PACKAGE"]);if(_business.DigitalWalletAvailable)allowed.Add("TRANSFER");
        if(allowed.Count==0){_page.Content=BusinessForm.Label("Service_Disabled");return;}
        if(!allowed.Contains(_entry.Type)&&_entry.Type!="CASHOUT")_entry.Type=allowed[0];
        if(_entry.Type=="CASHOUT"&&!_business.DigitalWalletAvailable)_entry.Type=allowed[0];
        var root=new Grid{Margin=new Thickness(16)};root.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});root.ColumnDefinitions.Add(new(){Width=new GridLength(300)});
        var leftHost=new DockPanel{Margin=new Thickness(0,0,14,0)};root.Children.Add(leftHost);var left=new Grid();var catalogRow=new RowDefinition{Height=new GridLength(2,GridUnitType.Star)};catalogRow.SetResourceReference(RowDefinition.MinHeightProperty,"DigitalCatalogMinimumHeight");left.RowDefinitions.Add(catalogRow);left.RowDefinitions.Add(new(){Height=GridLength.Auto});var cartRow=new RowDefinition{Height=new GridLength(3,GridUnitType.Star)};cartRow.SetResourceReference(RowDefinition.MinHeightProperty,"RegisterCartMinimumHeight");left.RowDefinitions.Add(cartRow);PanelHeights.SetKey(left,"digital-service-cart");
        var header=new StackPanel();var title=BusinessForm.Label("Service_Title");title.SetResourceReference(StyleProperty,"H1");header.Children.Add(title);var modes=new WrapPanel{Margin=new Thickness(0,4,0,10)};
        foreach(var type in allowed){var selected=type==_entry.Type||type=="TRANSFER"&&_entry.Type=="CASHOUT";modes.Children.Add(BusinessForm.Button("Service_"+type,(_,_)=>{_entry.Type=type;_entry.ProviderId=0;Record();},selected?"PrimaryButton":"GhostButton"));}header.Children.Add(modes);DockPanel.SetDock(header,Dock.Top);leftHost.Children.Add(header);var workspace=new ScrollViewer{Content=left,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,CanContentScroll=false};left.SetBinding(HeightProperty,new Binding("ViewportHeight"){Source=workspace});left.SetResourceReference(MinHeightProperty,"DigitalWorkspaceMinimumHeight");leftHost.Children.Add(workspace);
        void KeepPackageResultsVisible(double minimum){catalogRow.MinHeight=minimum;left.MinHeight=minimum+cartRow.MinHeight+8;}
        var catalog=new Border{Child=_entry.Type=="PACKAGE"?PackageCatalog(KeepPackageResultsVisible):EntryForm(),Margin=new Thickness(0,0,0,4)};catalog.SetResourceReference(StyleProperty,"Card");Grid.SetRow(catalog,0);left.Children.Add(catalog);
        var split=new GridSplitter{Height=8,HorizontalAlignment=HorizontalAlignment.Stretch,ResizeDirection=GridResizeDirection.Rows,ResizeBehavior=GridResizeBehavior.PreviousAndNext,Background=Brushes.Transparent};Grid.SetRow(split,1);left.Children.Add(split);
        var cartPanel=new DockPanel();var cartHeader=new WrapPanel();cartHeader.Children.Add(BusinessForm.Label("Pos_Cart"));var more=BusinessForm.Button("Common_More",(_,_)=>{},"GhostButton");var menu=new ContextMenu();menu.Items.Add(new MenuItem{Header=T("Trade_SaveDraft")+" (Ctrl+S)",Command=SaveDraftCommand});menu.Items.Add(new MenuItem{Header=T("Trade_ResumeDraft")+" (Ctrl+Shift+O)",Command=ResumeDraftCommand});more.Click+=(_,_)=>{menu.PlacementTarget=more;menu.IsOpen=true;};cartHeader.Children.Add(more);DockPanel.SetDock(cartHeader,Dock.Top);cartPanel.Children.Add(cartHeader);
        _cartTable=new DataGrid{Name="DigitalCart",ItemsSource=_cart,AutoGenerateColumns=false,CanUserAddRows=false,IsReadOnly=true,SelectionMode=DataGridSelectionMode.Single,RowHeight=double.NaN};_cartTable.SetResourceReference(DataGrid.MinRowHeightProperty,"TableRowHeight");TableLayout.SetKey(_cartTable,"digital-cart");
        AddTextColumn("Digital_ServiceName",nameof(DigitalCartLine.Title),1.3);AddTextColumn("Service_Provider",nameof(DigitalCartLine.ProviderName),1);
        AddEditColumn("Digital_Recipient",nameof(DigitalCartLine.Recipient),1.1,60);AddEditColumn("Measure_Amount",nameof(DigitalCartLine.AmountText),.85,40);
        AddEditColumn("Digital_Fee",nameof(DigitalCartLine.FeeText),.7,40,nameof(DigitalCartLine.HasFee));AddEditColumn("Digital_OptionalCost",nameof(DigitalCartLine.CostText),.8,40,nameof(DigitalCartLine.HasCost));AddEditColumn("Digital_ReferenceOptional",nameof(DigitalCartLine.Reference),.9,80);
        AddTextColumn("Pos_Total",nameof(DigitalCartLine.TotalText),1);
        var remove=new FrameworkElementFactory(typeof(Button));remove.SetValue(Button.ContentProperty,"×");remove.SetValue(FrameworkElement.StyleProperty,FindResource("DangerButton"));remove.SetValue(FrameworkElement.WidthProperty,30d);remove.SetValue(Control.PaddingProperty,new Thickness(3));remove.AddHandler(Button.ClickEvent,new RoutedEventHandler((sender,_)=>{if(((FrameworkElement)sender).DataContext is DigitalCartLine row)RemoveRow(row);}));_cartTable.Columns.Add(new DataGridTemplateColumn{CellTemplate=new DataTemplate{VisualTree=remove},Width=44});
        _cartTable.SelectionChanged+=(_,_)=>RemoveSelectedCommand.NotifyCanExecuteChanged();_cartTable.PreviewKeyDown+=CartKeyDown;cartPanel.Children.Add(_cartTable);
        var cartBorder=new Border{Child=cartPanel};cartBorder.SetResourceReference(StyleProperty,"Card");Grid.SetRow(cartBorder,2);left.Children.Add(cartBorder);
        var sidebar=new Grid();sidebar.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});sidebar.RowDefinitions.Add(new(){Height=GridLength.Auto});var summary=new StackPanel();_settlementLabel=BusinessForm.Label("Digital_ToCollect");summary.Children.Add(_settlementLabel);
        _cartTotal=new TextBlock();_cartTotal.SetResourceReference(StyleProperty,"BigMoney");summary.Children.Add(new Viewbox{Child=_cartTotal,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,HorizontalAlignment=HorizontalAlignment.Left});_cartCount=new TextBlock{Margin=new Thickness(0,12,0,8)};summary.Children.Add(_cartCount);sidebar.Children.Add(new ScrollViewer{Content=summary,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var actions=new StackPanel();_cashButton=BusinessForm.Button("Pos_Cash",(_,_)=>{},"SuccessButton");_cashButton.Command=OpenCashCommand;_cashButton.Name="DigitalCash";actions.Children.Add(_cashButton);
        _cardButton=BusinessForm.Button("Pos_Card",(_,_)=>{},"PrimaryButton");_cardButton.Command=OpenCardCommand;_cardButton.ToolTip=T("Digital_CashOutCashOnly");actions.Children.Add(_cardButton);
        _clearButton=BusinessForm.Button("Pos_Clear",(_,_)=>{},"DangerButton");_clearButton.Command=ClearCartCommand;actions.Children.Add(_clearButton);Grid.SetRow(actions,1);sidebar.Children.Add(actions);
        var side=new Border{Child=sidebar};side.SetResourceReference(StyleProperty,"Card");Grid.SetColumn(side,1);root.Children.Add(side);_page.Content=root;RefreshCartSummary();
    }
    private FrameworkElement EntryForm()
    {
        var panel=new StackPanel();
        if(_entry.Type is "TRANSFER" or "CASHOUT")
        {
            var choices=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(4,0,4,10)};
            foreach(var type in new[]{"TRANSFER","CASHOUT"}){var radio=new RadioButton{Content=T(type=="TRANSFER"?"Digital_OnlineTransfer":"Service_CASHOUT"),GroupName="digital-transfer-"+_accountGroup,IsChecked=_entry.Type==type,Margin=new Thickness(0,0,18,0)};radio.Checked+=(_,_)=>{_entry.Type=type;Record();};choices.Children.Add(radio);}panel.Children.Add(choices);
        }
        var form=new BusinessForm(3);var providers=_providers.Where(p=>p.IsActive&&p.AllowedTypes.Split(',').Contains(_entry.Type)).ToArray();if(!providers.Any(p=>p.Id==_entry.ProviderId))_entry.ProviderId=providers.FirstOrDefault()?.Id??0;
        var provider=new ComboBox{ItemsSource=providers,DisplayMemberPath="Name",SelectedValuePath="Id",SelectedValue=_entry.ProviderId};provider.SelectionChanged+=(_,_)=>{_entry.ProviderId=(int?)provider.SelectedValue??0;};form.Field(_entry.Type=="LOAD"?"Retail_SelectNetwork":"Service_Provider","ProviderId",provider);
        TextBox Field(string label,string field,string property,int limit=40){var input=new TextBox{MaxLength=limit,FlowDirection=FlowDirection.LeftToRight};input.SetBinding(TextBox.TextProperty,new Binding(property){Source=_entry,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});form.Field(label,field,input);return input;}
        var amount=Field("Measure_Amount",nameof(DigitalCartLine.AmountText),nameof(ServiceEntry.Principal));var recipient=Field(_entry.Type=="LOAD"?"Customer_Phone":"Digital_Recipient",nameof(DigitalCartLine.Recipient),nameof(ServiceEntry.Phone),60);
        var fee=Field("Digital_Fee",nameof(DigitalCartLine.FeeText),nameof(ServiceEntry.CustomerFee));var reference=Field("Digital_ReferenceOptional",nameof(DigitalCartLine.Reference),nameof(ServiceEntry.Reference),80);_search=amount;amount.Name="DigitalAmount";
        void Add()
        {
            var selected=providers.FirstOrDefault(p=>p.Id==_entry.ProviderId);if(selected is null){form.ShowErrors(new Dictionary<string,string>{{"ProviderId","Service_SelectProvider"}},true);return;}
            var row=new DigitalCartLine{Type=_entry.Type,Provider=selected,Name=T("Service_"+_entry.Type),AmountText=_entry.Principal,Recipient=_entry.Phone,FeeText=_entry.CustomerFee,Reference=_entry.Reference};var errors=row.Errors();form.ShowErrors(errors,true);if(errors.Count>0)return;
            AddRow(row);_entry.Reset();_entryDirty=false;foreach(var box in new[]{amount,recipient,reference})box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();amount.Focus();
        }
        var add=BusinessForm.Button("Digital_AddToCart",(_,_)=>Add());add.Name="AddDigitalLine";form.Add("",add);panel.Children.Add(form);
        amount.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;recipient.Focus();recipient.SelectAll();}};
        foreach(var box in new[]{recipient,fee,reference})box.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;Add();}};
        form.FieldEdited+=_=>{_entryDirty=true;RefreshCartSummary();};return panel;
    }
    private void AddTextColumn(string label,string property,double width)
    {
        var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new Binding(property));text.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);text.SetBinding(TextBlock.FontFamilyProperty,new Binding(property){Converter=(IValueConverter)FindResource("ProductTextFont")});if(property==nameof(DigitalCartLine.Title))text.SetBinding(FlowDirectionProperty,new Binding(nameof(DigitalCartLine.TitleDirection)));else text.SetValue(FlowDirectionProperty,FlowDirection.LeftToRight);text.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);_cartTable!.Columns.Add(new DataGridTemplateColumn{Header=T(label),CellTemplate=new DataTemplate{VisualTree=text},MinWidth=Math.Max(130,width*150)*(Convert.ToDouble(TryFindResource("TableTextSize")??17d)/17),Width=new DataGridLength(width,DataGridLengthUnitType.Star)});
    }
    private void AddEditColumn(string label,string property,double width,int limit,string? visible=null)
    {
        var box=new FrameworkElementFactory(typeof(TextBox));box.SetValue(FrameworkElement.UidProperty,property);box.SetValue(System.Windows.Automation.AutomationProperties.NameProperty,T(label));box.SetValue(TextBox.MaxLengthProperty,limit);box.SetValue(FrameworkElement.FlowDirectionProperty,FlowDirection.LeftToRight);box.SetBinding(TextBox.TextProperty,new Binding(property){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged,ValidatesOnDataErrors=true});
        if(visible is not null)box.SetBinding(VisibilityProperty,new Binding(visible){Converter=new BooleanToVisibilityConverter()});_cartTable!.Columns.Add(new DataGridTemplateColumn{Header=T(label),CellTemplate=new DataTemplate{VisualTree=box},MinWidth=Math.Max(130,width*150)*(Convert.ToDouble(TryFindResource("TableTextSize")??17d)/17),Width=new DataGridLength(width,DataGridLengthUnitType.Star)});
    }
    private void AddRow(DigitalCartLine row){if(_cart.Count>=500){_status.Text=T("Service_Invalid");return;}row.PropertyChanged+=RowChanged;_cart.Add(row);RefreshCartSummary();FocusRow(row,nameof(DigitalCartLine.Recipient));}
    private void RemoveRow(DigitalCartLine row){row.PropertyChanged-=RowChanged;_cart.Remove(row);if(_cart.Count==0)_checkoutKey=Guid.NewGuid();RefreshCartSummary();}
    private void RowChanged(object? sender,PropertyChangedEventArgs e){if(e.PropertyName is nameof(DigitalCartLine.AmountText) or nameof(DigitalCartLine.FeeText) or nameof(DigitalCartLine.CostText) or nameof(DigitalCartLine.Recipient) or nameof(DigitalCartLine.Reference)){((DigitalCartLine)sender!).RefreshTotal();RefreshCartSummary();}}
    private void ClearCart(){foreach(var row in _cart)row.PropertyChanged-=RowChanged;_cart.Clear();_checkoutKey=Guid.NewGuid();_entry.Reset();_entryDirty=false;Record();FocusSearch();}
    public void FocusSearch(){if(_search?.IsVisible==true){_search.Focus();_search.SelectAll();}}
    private void RefreshCartSummary()
    {
        decimal total;try{total=_cart.Sum(l=>l.Total);}catch(OverflowException){total=0;_status.Text=T("Service_InvalidAmount");}
        if(_cartTotal is not null)_cartTotal.Text=Money.Format(Math.Abs(total));if(_cartCount is not null)_cartCount.Text=T("Pos_ItemCount")+": "+_cart.Count;if(_settlementLabel is not null)_settlementLabel.Text=T(total<0?"Digital_ToPay":"Digital_ToCollect");
        OpenCashCommand?.NotifyCanExecuteChanged();OpenCardCommand?.NotifyCanExecuteChanged();ClearCartCommand?.NotifyCanExecuteChanged();RemoveSelectedCommand?.NotifyCanExecuteChanged();
        SaveDraftCommand?.NotifyCanExecuteChanged();ResumeDraftCommand?.NotifyCanExecuteChanged();
    }
    private void CartKeyDown(object sender,KeyEventArgs e)
    {
        if(e.Key!=Key.Enter||e.OriginalSource is not TextBox box||box.DataContext is not DigitalCartLine row)return;e.Handled=true;
        var fields=new[]{nameof(DigitalCartLine.Recipient),nameof(DigitalCartLine.AmountText),nameof(DigitalCartLine.FeeText),nameof(DigitalCartLine.CostText),nameof(DigitalCartLine.Reference)}.Where(p=>p!=nameof(DigitalCartLine.FeeText)||row.HasFee).Where(p=>p!=nameof(DigitalCartLine.CostText)||row.HasCost).ToArray();var index=Array.IndexOf(fields,box.Uid);
        if(row.Errors().ContainsKey(box.Tag?.ToString()??"")){row.ValidateFields();box.Focus();box.SelectAll();return;}
        if(index>=0&&index+1<fields.Length)FocusRow(row,fields[index+1]);else FocusSearch();
    }
    private void FocusRow(DigitalCartLine row,string property)=>Dispatcher.BeginInvoke(()=>
    {
        if(_cartTable is null||!_cart.Contains(row))return;_cartTable.SelectedItem=row;_cartTable.ScrollIntoView(row);_cartTable.UpdateLayout();var input=VisualChildren(_cartTable).OfType<TextBox>().FirstOrDefault(t=>ReferenceEquals(t.DataContext,row)&&t.Uid==property&&t.IsVisible);input?.Focus();input?.SelectAll();
    });
    private static IEnumerable<DependencyObject> VisualChildren(DependencyObject root){for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in VisualChildren(child))yield return nested;}}
}
