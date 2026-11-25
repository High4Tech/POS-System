using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Business;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;

namespace SupermarketPos.Wpf.Views;
public sealed class DigitalServicesView : UserControl
{
    private readonly DigitalServiceLedger _ledger;private readonly IBusinessSetup _setup;private readonly ReceiptPreviewDialog _receipts;
    private readonly ContentControl _page=new();private readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly ServiceEntry _entry=new();private BusinessProfile _business=new();private bool _busy;
    private IReadOnlyList<ServiceProviderAccount> _providers=[];private IReadOnlyList<MobileServicePackage> _packages=[];private IReadOnlyList<ServiceTransaction> _history=[];
    public bool HasDraft{get;private set;}
    public void ChooseType(string? type){if(type is not ("LOAD" or "PACKAGE"))return;if(HasDraft&&_entry.Type!=type){_status.Text=T("Service_FinishDraft");return;}_entry.Type=type;if(IsLoaded)Record();}
    private static string T(string key)=>LocalizationManager.Instance[key];
    public DigitalServicesView(DigitalServiceLedger ledger,IBusinessSetup setup,ReceiptPreviewDialog receipts,bool configuration=false)
    {
        _ledger=ledger;_setup=setup;_receipts=receipts;var root=new DockPanel{Margin=new Thickness(4)};
        if(configuration)
        {
            var nav=new WrapPanel();foreach(var key in new[]{"Providers","Packages"})nav.Children.Add(BusinessForm.Button("Service_"+key,async(_,_)=>await Run(async()=>{await LoadAsync();await ShowAsync(key);}),"GhostButton"));DockPanel.SetDock(nav,Dock.Top);root.Children.Add(nav);
        }
        else
        {
            var more=new Button{Content="⋯",ToolTip=T("Service_History")+" / "+T("Service_Reports"),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Padding=new Thickness(6,0,6,0)};
            more.SetResourceReference(StyleProperty,"GhostButton");var menu=new ContextMenu();foreach(var key in new[]{"Record","History","Reports"})
            {var item=new MenuItem{Header=T("Service_"+key)};item.Click+=async(_,_)=>await Run(async()=>{await LoadAsync();await ShowAsync(key);});menu.Items.Add(item);}
            more.ContextMenu=menu;more.Click+=(_,_)=>{menu.PlacementTarget=more;menu.IsOpen=true;};DockPanel.SetDock(more,Dock.Right);root.Children.Add(more);
        }
        DockPanel.SetDock(_status,Dock.Bottom);root.Children.Add(_status);root.Children.Add(new ScrollViewer{Content=_page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Content=root;
        Loaded+=async(_,_)=>await Run(async()=>{await LoadAsync();await ShowAsync(configuration?"Providers":"Record");});
    }
    private async Task LoadAsync(){_business=(await _setup.LoadAsync()).Profile;_providers=await _ledger.ProvidersAsync();_packages=await _ledger.PackagesAsync();_history=await _ledger.HistoryAsync();}
    private async Task ShowAsync(string page)
    {
        if(_business.IsPharmacy){_page.Content=BusinessForm.Label("Service_Disabled");return;}
        switch(page){case "Record":Record();break;case "Providers":Providers();break;case "Packages":Packages();break;case "History":await HistoryAsync();break;case "Reports":await ReportsAsync();break;}
    }
    private ComboBox ProviderSelect(BusinessForm f,int selected,Action<int> changed)
    {
        var combo=new ComboBox{ItemsSource=_providers,DisplayMemberPath="Name",SelectedValuePath="Id",SelectedValue=selected};combo.SelectionChanged+=(_,_)=>{if(combo.SelectedItem is ServiceProviderAccount p)changed(p.Id);};f.Field("Service_Provider","ProviderId",combo);return combo;
    }
    private string _network="";
    private string _accountType="Prepaid";
    private void Record()
    {
        var panel=new StackPanel();var types=new WrapPanel();
        var allowed=new List<string>();if(_business.DigitalWalletAvailable)allowed.AddRange(["CASHIN","CASHOUT"]);if(_business.MobileRechargeAvailable)allowed.AddRange(["LOAD","PACKAGE"]);
        if(allowed.Count==0){_page.Content=BusinessForm.Label("Service_Disabled");return;}
        if(!allowed.Contains(_entry.Type))_entry.Type=allowed[0];
        foreach(var code in allowed)types.Children.Add(BusinessForm.Button("Service_"+code,(_,_)=>{_entry.ClearConfirmation();_entry.Type=code;_entry.PackageId=null;Record();},code==_entry.Type?"PrimaryButton":"GhostButton"));
        panel.Children.Add(types);
        if(_entry.Type=="PACKAGE")PackageCatalog(panel);
        else
        {
            var f=new BusinessForm(_entry.Type=="LOAD"?3:2);var providers=_providers.Where(p=>p.IsActive&&p.AllowedTypes.Split(',').Contains(_entry.Type)).ToArray();
            if(!providers.Any(p=>p.Id==_entry.ProviderId))_entry.ProviderId=providers.FirstOrDefault()?.Id??0;
            var provider=new ComboBox{ItemsSource=providers,DisplayMemberPath="Name",SelectedValuePath="Id",SelectedValue=_entry.ProviderId};
            provider.SelectionChanged+=(_,_)=>{if(provider.SelectedItem is ServiceProviderAccount p){_entry.ClearConfirmation();_entry.ProviderId=p.Id;Record();}};
            f.Add("Service_Provider",provider);f.Text("Measure_Amount",_entry,nameof(_entry.Principal),true);
            if(_entry.Type=="LOAD")f.Text("Customer_Phone",_entry,nameof(_entry.Phone),true,60);
            var summary=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(6)};
            void Quote(){try{var p=providers.SingleOrDefault(p=>p.Id==_entry.ProviderId);summary.Text=p is null?T("Service_SelectProvider"):Summary(DigitalServiceLedger.ProviderQuote(p,_entry.Type,N(_entry.Principal)));}catch{summary.Text=T("Service_InvalidAmount");}}
            f.FieldEdited+=_=>{_entry.ClearConfirmation();HasDraft=true;Quote();};Quote();panel.Children.Add(f);panel.Children.Add(summary);
            AddConfirmation(panel,()=>DigitalServiceLedger.ProviderQuote(providers.SingleOrDefault(p=>p.Id==_entry.ProviderId)??throw new DomainException("Service_SelectProvider"),_entry.Type,N(_entry.Principal)));
        }
        _page.Content=panel;
    }
    private void PackageCatalog(StackPanel panel)
    {
        var providers=_providers.Where(p=>p.IsActive&&p.AllowedTypes.Split(',').Contains("PACKAGE")).ToArray();
        var names=new[]{"Jazz","Zong","Ufone","Telenor"}.Concat(providers.Select(p=>p.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(_network.Length==0)_network=providers.FirstOrDefault()?.Name??names[0];
        var networks=new WrapPanel();foreach(var name in names)
        {
            var button=new Button{Content=name,Margin=new Thickness(4)};button.SetResourceReference(StyleProperty,name.Equals(_network,StringComparison.OrdinalIgnoreCase)?"PrimaryButton":"GhostButton");
            button.Click+=(_,_)=>{_entry.ClearConfirmation();_network=name;_entry.PackageId=null;Record();};networks.Children.Add(button);
        }
        panel.Children.Add(networks);var accounts=new WrapPanel();
        foreach(var account in new[]{"Prepaid","Postpaid"})accounts.Children.Add(BusinessForm.Button("Service_"+account,(_,_)=>{_entry.ClearConfirmation();_accountType=account;_entry.PackageId=null;Record();},_accountType==account?"PrimaryButton":"GhostButton"));
        panel.Children.Add(accounts);
        var provider=providers.SingleOrDefault(p=>p.Name.Equals(_network,StringComparison.OrdinalIgnoreCase));_entry.ProviderId=provider?.Id??0;
        var packages=_packages.Where(p=>p.ProviderId==_entry.ProviderId&&p.IsActive&&p.AccountType==_accountType).ToArray();
        if(_entry.PackageId is{} selected&&packages.SingleOrDefault(p=>p.Id==selected) is{} choice)
        {
            panel.Children.Remove(networks);panel.Children.Remove(accounts);
            var heading=new DockPanel();var change=BusinessForm.Button("Service_ChangePackage",(_,_)=>{_entry.PackageId=null;Record();},"GhostButton");DockPanel.SetDock(change,Dock.Right);heading.Children.Add(change);
            var details=new TextBlock{Text=_network+" · "+T("Service_"+_accountType)+" · "+choice.Name,TextWrapping=TextWrapping.Wrap,FontWeight=FontWeights.SemiBold,Margin=new Thickness(6,8,6,4)};
            heading.Children.Add(details);panel.Children.Add(heading);
            panel.Children.Add(new TextBlock{Text=choice.Benefits+" · "+choice.Validity,TextWrapping=TextWrapping.Wrap,MaxHeight=72,ToolTip=choice.Benefits,Margin=new Thickness(6,0,6,8)});
            panel.Children.Add(new TextBlock{Text=Summary(DigitalServiceLedger.PackageQuote(choice)),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(6)});
            AddConfirmation(panel,()=>DigitalServiceLedger.PackageQuote(choice));return;
        }
        var search=new TextBox{Tag=T("Service_SearchPackages"),Margin=new Thickness(4,8,4,8)};
        panel.Children.Add(search);var cards=new WrapPanel();panel.Children.Add(cards);
        void Filter()
        {
            cards.Children.Clear();var filtered=packages.Where(p=>(p.Name+" "+p.PackageIdentifier+" "+p.Benefits).Contains(search.Text.Trim(),StringComparison.CurrentCultureIgnoreCase)).ToArray();
            foreach(var p in filtered)
            {
                var body=new StackPanel();body.Children.Add(new TextBlock{Text=p.Name,TextWrapping=TextWrapping.Wrap,FontWeight=FontWeights.SemiBold});
                body.Children.Add(new TextBlock{Text=p.Benefits,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,6)});
                body.Children.Add(new TextBlock{Text=p.Validity,TextWrapping=TextWrapping.Wrap});
                var price=new TextBlock{Text=Money.Format(p.Price+p.CustomerFee),FontWeight=FontWeights.Bold,Margin=new Thickness(0,8,0,0)};price.SetResourceReference(TextBlock.ForegroundProperty,"AccentBrush");body.Children.Add(price);
                var button=new Button{Content=body,Width=240,Margin=new Thickness(4),HorizontalContentAlignment=HorizontalAlignment.Stretch};button.SetResourceReference(StyleProperty,"CatalogChoiceCard");
                button.Click+=(_,_)=>{_entry.ClearConfirmation();_entry.PackageId=p.Id;HasDraft=true;Record();};cards.Children.Add(button);
            }
            if(filtered.Length==0)cards.Children.Add(BusinessForm.Label("Service_NoPackages"));
        }
        search.TextChanged+=(_,_)=>Filter();Filter();
    }
    private static string Summary(ServiceTransaction quote)=>$"{T(quote.Type=="CASHOUT"?"Service_ToPay":"Service_ToCollect")}: {Money.Format(quote.Type=="CASHOUT"?-quote.CashChange:quote.CashChange)}"+(quote.CustomerFee>0?$" · {T("Service_FeeShort")}: {Money.Format(quote.CustomerFee)}":"")+(quote.Commission>0?$" · {T("Service_CommissionShort")}: {Money.Format(quote.Commission)}":"");
    private void AddConfirmation(StackPanel panel,Func<ServiceTransaction> quote)
    {
        var f=new BusinessForm(_entry.Type=="PACKAGE"?3:2);
        if(_entry.Type=="PACKAGE")
        {
            var phone=f.Text("Customer_Phone",_entry,nameof(_entry.Phone),true,60);Dispatcher.BeginInvoke(()=>{phone.Focus();phone.SelectAll();});
        }
        var result=f.Select("Service_ProviderResult",_entry,nameof(_entry.Status),("Confirmed","Service_ResultConfirmed"),("Pending","Service_ResultPending"),("Failed","Service_Failed"));
        f.Text("Service_Reference",_entry,nameof(_entry.Reference),true,80);
        var external=f.Check("Service_ExternalConfirmed",_entry,nameof(_entry.External));external.ToolTip=T("Service_ManualNotice");
        Grid.SetColumnSpan((FrameworkElement)external.Parent,_entry.Type=="PACKAGE"?3:2);
        void ResultChanged(){((FrameworkElement)external.Parent).Visibility=_entry.Status=="Confirmed"?Visibility.Visible:Visibility.Collapsed;if(_entry.Status!="Confirmed")_entry.External=false;}
        result.SelectionChanged+=(_,_)=>ResultChanged();ResultChanged();
        f.FieldEdited+=_=>HasDraft=true;
        var error=new TextBlock{TextWrapping=TextWrapping.Wrap,Visibility=Visibility.Collapsed};error.SetResourceReference(TextBlock.ForegroundProperty,"DangerBrush");
        var actions=new WrapPanel();actions.Children.Add(BusinessForm.Button("Service_Save",async(_,_)=>await Run(async()=>
        {
            try
            {
                var tx=quote();tx.ConfirmationId=_entry.Key;tx.Status=_entry.Status;tx.Reference=_entry.Reference.Trim();tx.CustomerPhone=_entry.Phone.Trim();tx.OccurredAt=DateTime.Now;
                var errors=new Dictionary<string,string>();
                if(tx.Principal<=0)errors[nameof(_entry.Principal)]="Service_InvalidAmount";
                if(tx.Type is "LOAD" or "PACKAGE"){try{tx.CustomerPhone=InvoicePhone.Normalize(tx.CustomerPhone);}catch(DomainException){errors[nameof(_entry.Phone)]="Validation_Phone";}}
                if(tx.Status=="Confirmed"){if(!tx.Reference.Any())errors[nameof(_entry.Reference)]="Validation_Required";if(!_entry.External)errors[nameof(_entry.External)]="Service_ExternalRequired";}
                // Required fields live in this panel and the compact transaction panel.
                foreach(var form in panel.Children.OfType<BusinessForm>())form.ShowErrors(errors,true);f.ShowErrors(errors,true);
                if(errors.Count>0)return;
                var saved=await _ledger.SaveAsync(tx,_entry.External);HasDraft=false;_entry.Reset();await LoadAsync();Record();_status.Text=T("Service_Saved")+" · "+saved.ReceiptNumber;
                if(saved.Status=="Confirmed")await _receipts.ShowServiceAsync(saved,(await _ledger.PostingsAsync()).Single(p=>p.TransactionId==saved.Id),_business);
            }
            catch(DomainException ex){error.Text=T(ex.Message);error.Visibility=Visibility.Visible;((FrameworkElement)error.Parent).Visibility=Visibility.Visible;}
        })));
        actions.Children.Add(BusinessForm.Button("Common_Cancel",(_,_)=>{_entry.Reset();HasDraft=false;Record();},"GhostButton"));f.Add("",error,true);((FrameworkElement)error.Parent).Visibility=Visibility.Collapsed;f.Add("",actions,true);panel.Children.Add(f);
    }
    private async Task HistoryAsync()
    {
        var panel=new StackPanel();var posts=(await _ledger.PostingsAsync()).ToDictionary(p=>p.TransactionId);
        var rows=_history.Select(t=>new ServiceHistoryRow(t,posts.GetValueOrDefault(t.Id))).ToArray();var table=Table(rows,"digital-service-history",("Service_Receipt","Number"),("Reports_Date","Date"),("Service_Provider","Provider"),("Service_Type","Type"),("Service_Status","Status"),("Service_Principal","Principal"),("Service_Income","Income"),("Service_Reference","Reference"));panel.Children.Add(table);
        var actions=new WrapPanel();
        foreach(var state in new[]{"Confirmed","Failed","Reversed"})actions.Children.Add(BusinessForm.Button("Service_Action"+state,async(_,_)=>await Run(async()=>
        {
            if(table.SelectedItem is not ServiceHistoryRow row)throw new DomainException("Service_SelectTransaction");
            if(state=="Failed")await _ledger.SetStatusAsync(row.Transaction.Id,state,false);
            else
            {
                var confirmation=new ServiceConfirmWindow(row.Transaction.Reference){Owner=Window.GetWindow(this)};if(confirmation.ShowDialog()!=true)return;
                if(state=="Reversed")await _ledger.ReverseAsync(row.Transaction.Id,confirmation.Reference,true);else await _ledger.SetStatusAsync(row.Transaction.Id,state,true,confirmation.Reference);
            }
            await LoadAsync();await HistoryAsync();
        }),"GhostButton"));
        actions.Children.Add(BusinessForm.Button("Receipt_Preview",async(_,_)=>await Run(async()=>{if(table.SelectedItem is not ServiceHistoryRow row||row.Posting is null)throw new DomainException("Service_SelectCompleted");await _receipts.ShowServiceAsync(row.Transaction,row.Posting,_business);})));panel.Children.Add(actions);_page.Content=panel;
    }
    private void Providers()
    {
        var panel=new StackPanel();var f=new BusinessForm();var state=new ProviderEntry();
        var pick=new ComboBox{ItemsSource=_providers,DisplayMemberPath="Name"};f.Add("Service_EditProvider",pick,true);
        pick.SelectionChanged+=(_,_)=>{if(pick.SelectedItem is ServiceProviderAccount p){state.Id=p.Id;state.Name=p.Name;state.Opening=p.OpeningFloat.ToString(CultureInfo.InvariantCulture);state.FeePolicy=p.FeePolicy;state.CommissionPolicy=p.CommissionPolicy;state.CustomerFee=p.DefaultCustomerFee.ToString(CultureInfo.InvariantCulture);state.ProviderFee=p.DefaultProviderFee.ToString(CultureInfo.InvariantCulture);state.Commission=p.DefaultCommission.ToString(CultureInfo.InvariantCulture);state.LoadCostRate=p.LoadCostRate.ToString(CultureInfo.InvariantCulture);state.Active=p.IsActive;state.CashIn=p.AllowedTypes.Contains("CASHIN");state.CashOut=p.AllowedTypes.Contains("CASHOUT");state.Load=p.AllowedTypes.Contains("LOAD");state.Package=p.AllowedTypes.Contains("PACKAGE");foreach(var input in f.Fields.Values){if(input is TextBox t)t.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();if(input is CheckBox c)c.GetBindingExpression(CheckBox.IsCheckedProperty)?.UpdateTarget();}}};
        f.Text("Service_Provider",state,nameof(state.Name),limit:100);f.Text("Service_OpeningFloat",state,nameof(state.Opening),true);
        f.Check("Service_CASHIN",state,nameof(state.CashIn));f.Check("Service_CASHOUT",state,nameof(state.CashOut));f.Check("Service_LOAD",state,nameof(state.Load));f.Check("Service_PACKAGE",state,nameof(state.Package));
        f.Text("Service_DefaultCustomerFee",state,nameof(state.CustomerFee),true);f.Text("Service_DefaultProviderFee",state,nameof(state.ProviderFee),true);f.Text("Service_DefaultCommission",state,nameof(state.Commission),true);f.Text("Service_LoadCostRate",state,nameof(state.LoadCostRate),true);
        f.Add("",BusinessForm.Label("Service_SettlementDefaults"),true);
        f.Text("Service_FeePolicy",state,nameof(state.FeePolicy),limit:300);f.Text("Service_CommissionPolicy",state,nameof(state.CommissionPolicy),limit:300);f.Check("Service_Active",state,nameof(state.Active));
        f.Add("",BusinessForm.Button("Service_SaveConfig",async(_,_)=>await Run(async()=>{var types=new List<string>();if(state.CashIn)types.Add("CASHIN");if(state.CashOut)types.Add("CASHOUT");if(state.Load)types.Add("LOAD");if(state.Package)types.Add("PACKAGE");await _ledger.SaveProviderAsync(new(){Id=state.Id,Name=state.Name.Trim(),OpeningFloat=N(state.Opening),DefaultCustomerFee=N(state.CustomerFee),DefaultProviderFee=N(state.ProviderFee),DefaultCommission=N(state.Commission),LoadCostRate=N(state.LoadCostRate),AllowedTypes=string.Join(',',types),FeePolicy=state.FeePolicy,CommissionPolicy=state.CommissionPolicy,IsActive=state.Active});await LoadAsync();Providers();_status.Text=T("Settings_Saved");})),true);
        f.Add("",BusinessForm.Label("Service_ProviderSuggestions"),true);panel.Children.Add(f);_page.Content=panel;
    }
    private void Packages()
    {
        var f=new BusinessForm(3);var state=new PackageEntry();
        var provider=new ComboBox{ItemsSource=_providers.Where(p=>p.IsActive&&p.AllowedTypes.Split(',').Contains("PACKAGE")),DisplayMemberPath="Name",SelectedValuePath="Id"};
        provider.SetBinding(ComboBox.SelectedValueProperty,new System.Windows.Data.Binding(nameof(state.ProviderId)){Source=state,Mode=System.Windows.Data.BindingMode.TwoWay});
        f.Field("Service_Provider",nameof(state.ProviderId),provider);
        var pick=new ComboBox{ItemsSource=_packages,DisplayMemberPath="Name",IsEditable=true,IsTextSearchEnabled=false};f.Add("Service_EditPackage",pick,true);
        f.Select("Service_AccountType",state,nameof(state.AccountType),("Prepaid","Service_Prepaid"),("Postpaid","Service_Postpaid"));
        f.Text("Service_PackageId",state,nameof(state.Identifier),limit:100);f.Text("Service_PackageName",state,nameof(state.Name),limit:150);
        f.Text("Service_Benefits",state,nameof(state.Benefits),limit:1000);f.Text("Service_Validity",state,nameof(state.Validity),limit:100);
        f.Text("Products_Price",state,nameof(state.Price),true);f.Text("Service_ProviderCost",state,nameof(state.Cost),true);f.Text("Service_Commission",state,nameof(state.Commission),true);
        f.Text("Service_CustomerFee",state,nameof(state.CustomerFee),true);f.Text("Service_ProviderFee",state,nameof(state.ProviderFee),true);f.Check("Service_Active",state,nameof(state.Active));
        pick.SelectionChanged+=(_,_)=>{if(pick.SelectedItem is MobileServicePackage p)
        {
            state.Id=p.Id;state.ProviderId=p.ProviderId;state.Name=p.Name;state.Kind=p.Kind;state.Price=p.Price.ToString(CultureInfo.InvariantCulture);state.Cost=p.ProviderCost.ToString(CultureInfo.InvariantCulture);state.Commission=p.Commission.ToString(CultureInfo.InvariantCulture);state.Validity=p.Validity;state.Active=p.IsActive;
            state.AccountType=p.AccountType;state.Identifier=p.PackageIdentifier;state.Benefits=p.Benefits;state.CustomerFee=p.CustomerFee.ToString(CultureInfo.InvariantCulture);state.ProviderFee=p.ProviderFee.ToString(CultureInfo.InvariantCulture);
            foreach(var input in f.Fields.Values){if(input is TextBox t)t.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();if(input is ComboBox c)c.GetBindingExpression(ComboBox.SelectedValueProperty)?.UpdateTarget();if(input is CheckBox b)b.GetBindingExpression(CheckBox.IsCheckedProperty)?.UpdateTarget();}
        }};
        f.Add("",BusinessForm.Button("Service_SaveConfig",async(_,_)=>await Run(async()=>
        {
            var errors=new Dictionary<string,string>();
            foreach(var field in new[]{nameof(state.Name),nameof(state.Identifier),nameof(state.Benefits),nameof(state.Validity)})if(string.IsNullOrWhiteSpace((string)typeof(PackageEntry).GetProperty(field)!.GetValue(state)!))errors[field]="Validation_Required";
            if(state.ProviderId<=0)errors[nameof(state.ProviderId)]="Validation_Required";
            if(state.AccountType is not ("Prepaid" or "Postpaid"))errors[nameof(state.AccountType)]="Validation_Choice";
            foreach(var field in new[]{nameof(state.Price),nameof(state.Cost),nameof(state.Commission),nameof(state.CustomerFee),nameof(state.ProviderFee)})
            {if(!decimal.TryParse((string)typeof(PackageEntry).GetProperty(field)!.GetValue(state)!,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var n)||n<0||n!=Money.Round2(n)||field==nameof(state.Price)&&n<=0)errors[field]="Service_InvalidAmount";}
            f.ShowErrors(errors,true);if(errors.Count>0)return;
            await _ledger.SavePackageAsync(new(){Id=state.Id,ProviderId=state.ProviderId,Name=state.Name.Trim(),Kind=state.Kind,AccountType=state.AccountType,PackageIdentifier=state.Identifier.Trim(),Benefits=state.Benefits.Trim(),CustomerFee=N(state.CustomerFee),ProviderFee=N(state.ProviderFee),Price=N(state.Price),ProviderCost=N(state.Cost),Commission=N(state.Commission),Validity=state.Validity,IsActive=state.Active});
            await LoadAsync();Packages();_status.Text=T("Settings_Saved");
        })),true);_page.Content=f;
    }
    private async Task ReportsAsync(DateTime? from=null,DateTime? to=null)
    {
        var start=from??DateTime.Today;var end=to??DateTime.Today;var f=new BusinessForm();var first=new DatePicker{SelectedDate=start};var last=new DatePicker{SelectedDate=end};f.Add("Reports_From",first);f.Add("Reports_To",last);
        f.Add("",BusinessForm.Button("Reports_Generate",async(_,_)=>await Run(()=>ReportsAsync(first.SelectedDate,last.SelectedDate))),true);
        if(end<start)throw new DomainException("Service_Invalid");var report=await _ledger.ReportAsync(start,end.AddDays(1));
        foreach(var pair in new[]{("Service_PhysicalRevenue",report.PhysicalRevenue),("Service_LooseRevenue",report.LooseRevenue),("Service_Income",report.Income),("Service_CashChange",report.CashChange),("Service_FloatChange",report.FloatChange),("Service_CASHIN",report.CashIn),("Service_CASHOUT",report.CashOut),("Service_RechargeCollected",report.RechargeCollected),("Service_Commission",report.Commissions)})f.Add(pair.Item1,new TextBlock{Text=Money.Format(pair.Item2)});
        f.Add("Service_Pending",new TextBlock{Text=report.Pending.ToString()});f.Add("Service_FailedReversed",new TextBlock{Text=report.FailedReversed.ToString()});
        foreach(var provider in _providers)f.Add("Service_RecordedFloat",new TextBlock{Text=provider.Name+" · "+Money.Format(await _ledger.FloatAsync(provider.Id))});_page.Content=f;
    }
    private static DataGrid Table<TItem>(IEnumerable<TItem> rows,string key,params(string Key,string Property)[] columns)
    {
        var grid=new DataGrid{ItemsSource=rows,AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,SelectionMode=DataGridSelectionMode.Single,MinHeight=240,MaxHeight=520};TableLayout.SetKey(grid,key);
        foreach(var c in columns)grid.Columns.Add(new DataGridTextColumn{Header=T(c.Key),Binding=new System.Windows.Data.Binding(c.Property),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});return grid;
    }
    private static decimal N(string value)=>decimal.TryParse(value,NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var n)?n:throw new DomainException("Common_InvalidNumber");
    private async Task Run(Func<Task> action){if(_busy)return;_busy=true;IsEnabled=false;try{await action();}catch(Exception e){_status.Text=T(e is DomainException?e.Message:"Service_Error");_status.SetResourceReference(TextBlock.ForegroundProperty,"DangerBrush");}finally{IsEnabled=true;_busy=false;}}
    private sealed class ServiceEntry : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private bool _external;
        public void ClearConfirmation(){External=false;Reference="";PropertyChanged?.Invoke(this,new(nameof(Reference)));}
        public Guid Key=Guid.NewGuid();public int ProviderId{get;set;}public int? PackageId{get;set;}public string Type{get;set;}="CASHIN";public string Status{get;set;}="Confirmed";public string Principal{get;set;}="0";public string CustomerFee{get;set;}="0";public string ProviderFee{get;set;}="0";public string Commission{get;set;}="0";public string Cost{get;set;}="0";public string Collected{get;set;}="0";public string FloatChange{get;set;}="0";public string Reference{get;set;}="";public string Phone{get;set;}="";public DateTime? Date{get;set;}=DateTime.Today;public string Time{get;set;}=DateTime.Now.ToString("HH:mm");public bool External{get=>_external;set{_external=value;PropertyChanged?.Invoke(this,new(nameof(External)));}}
        public void Reset(){Key=Guid.NewGuid();Reference=Phone="";Principal=CustomerFee=ProviderFee=Commission=Cost=Collected=FloatChange="0";External=false;Status="Confirmed";PackageId=null;}
    }
    private sealed class ProviderEntry{public string CustomerFee{get;set;}="0";public string ProviderFee{get;set;}="0";public string Commission{get;set;}="0";public string LoadCostRate{get;set;}="1";public int Id;public string Name{get;set;}="";public string Opening{get;set;}="0";public string FeePolicy{get;set;}="";public string CommissionPolicy{get;set;}="";public bool Active{get;set;}=true;public bool CashIn{get;set;}=true;public bool CashOut{get;set;}=true;public bool Load{get;set;}=true;public bool Package{get;set;}=true;}
    private sealed class PackageEntry{public string AccountType{get;set;}="Prepaid";public string Identifier{get;set;}="";public string Benefits{get;set;}="";public string CustomerFee{get;set;}="0";public string ProviderFee{get;set;}="0";public int Id;public int ProviderId{get;set;}public string Name{get;set;}="";public string Kind{get;set;}="";public string Price{get;set;}="0";public string Cost{get;set;}="0";public string Commission{get;set;}="0";public string Validity{get;set;}="";public bool Active{get;set;}=true;}
}
public sealed record ServiceHistoryRow(ServiceTransaction Transaction,ServicePosting? Posting)
{
    public string Number=>Transaction.ReceiptNumber;public string Date=>Transaction.OccurredAt.ToString("yyyy-MM-dd HH:mm");public string Provider=>Transaction.ProviderName;public string Type=>LocalizationManager.Instance["Service_"+Transaction.Type];public string Status=>LocalizationManager.Instance["Service_"+Transaction.Status];public string Principal=>Money.Format(Transaction.Principal);public string Income=>Money.Format(Posting?.Income??0);public string Reference=>Transaction.Reference;
}
public sealed class ServiceConfirmWindow:Window
{
    private readonly TextBox _reference;public string Reference=>_reference.Text.Trim();
    public ServiceConfirmWindow(string reference)
    {
        Title=LocalizationManager.Instance["Service_ExternalConfirmed"];SetResourceReference(StyleProperty,"FormDialogWindow");WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var f=new BusinessForm();_reference=new(){Text=reference,MaxLength=80};f.Add("Service_Reference",_reference,true);var confirm=new CheckBox{Content=BusinessForm.Label("Service_ExternalConfirmed")};f.Add("",confirm,true);var error=new TextBlock{TextWrapping=TextWrapping.Wrap};f.Add("",error,true);
        f.Add("",BusinessForm.Button("Common_Cancel",(_,_)=>DialogResult=false,"GhostButton"));f.Add("",BusinessForm.Button("Common_Confirm",(_,_)=>{if(confirm.IsChecked==true&&Reference.Length>0)DialogResult=true;else error.Text=LocalizationManager.Instance["Service_ExternalRequired"];}));var card=new Border{Width=650,Child=f};card.SetResourceReference(StyleProperty,"Card");var fit=new Viewbox{Child=card};fit.SetResourceReference(StyleProperty,"DialogFit");Content=fit;
    }
}



