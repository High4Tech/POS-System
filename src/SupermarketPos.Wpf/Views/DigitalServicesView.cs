using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Business;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Infrastructure.Commercial;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;

namespace SupermarketPos.Wpf.Views;
public sealed partial class DigitalServicesView : UserControl
{
    private readonly DigitalServiceLedger _ledger;private readonly IBusinessSetup _setup;private readonly ReceiptPreviewDialog _receipts;
    private readonly ContentControl _page=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};private readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly ServiceEntry _entry=new();private BusinessProfile _business=new();private bool _busy;
    private IReadOnlyList<ServiceProviderAccount> _providers=[];private IReadOnlyList<MobileServicePackage> _packages=[];private IReadOnlyList<ServiceTransaction> _history=[];
    private bool _entryDirty;
    public bool HasDraft=>_cart.Count>0||_entryDirty;
    private static string T(string key)=>LocalizationManager.Instance[key];
    public DigitalServicesView(DigitalServiceLedger ledger,IBusinessSetup setup,ReceiptPreviewDialog receipts,bool configuration=false,RetailInventory? inventory=null,RetailReferenceCatalog? references=null,IProductRepository? products=null,string? section=null,ProtectedLocalStore? draftStore=null,IAuthService? auth=null)
    {
        _ledger=ledger;_setup=setup;_receipts=receipts;_retailInventory=inventory;_references=references;_products=products;_configuration=configuration;_draftStore=draftStore;_auth=auth;InitializeCartCommands();var root=new DockPanel{Margin=new Thickness(4)};
        if(configuration)
        {
            var nav=new WrapPanel();foreach(var key in new[]{"Providers","Packages"})nav.Children.Add(BusinessForm.Button("Service_"+key,async(_,_)=>await Run(async()=>{await LoadAsync();await ShowAsync(key);}),"GhostButton"));DockPanel.SetDock(nav,Dock.Top);root.Children.Add(nav);
        }
        DockPanel.SetDock(_status,Dock.Bottom);root.Children.Add(_status);root.Children.Add(configuration||section is not null?new ScrollViewer{Content=_page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}:_page);Content=root;
        Loaded+=async(_,_)=>await Run(async()=>{if(_retailInventory is not null)await _retailInventory.EnsureProviderTemplateAsync();await LoadAsync();await ShowAsync(section??(configuration?"Providers":"Record"));});
    }
    private async Task LoadAsync(){_business=(await _setup.LoadAsync()).Profile;_providers=await _ledger.ProvidersAsync();_packages=await _ledger.PackagesAsync();}
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
    private async Task HistoryAsync()
    {
        var panel=new StackPanel();_history=await _ledger.HistoryAsync();var posts=(await _ledger.PostingsAsync()).ToDictionary(p=>p.TransactionId);
        var rows=_history.Select(t=>new ServiceHistoryRow(t,posts.GetValueOrDefault(t.Id))).ToArray();
        var filters=new BusinessForm(3);var search=new TextBox{Name="DigitalHistorySearch"};var from=new DatePicker();var to=new DatePicker();filters.Field("Digital_CustomerSearch","Search",search);filters.Add("Reports_From",from);filters.Add("Reports_To",to);panel.Children.Add(filters);
        var table=Table(rows,"digital-service-history",("Service_Receipt","Number"),("Digital_AttemptDate","Date"),("Digital_PostedDate","PostedDate"),("Service_Provider","Provider"),("Service_Type","Type"),("Service_Status","Status"),("Digital_Recipient","Recipient"),("Customer_Name","CustomerName"),("Service_Principal","Principal"),("Digital_Fee","Fee"),("Service_Income","Income"));foreach(var column in table.Columns)column.Width=DataGridLength.Auto;panel.Children.Add(table);
        var customers=await _ledger.CustomersAsync();
        void Filter(){var term=search.Text.Trim();var matched=customers.Where(c=>c.Matches(term)).Select(c=>c.Key).ToHashSet();table.ItemsSource=rows.Where(r=>(from.SelectedDate is not{} start||r.Transaction.OccurredAt.Date>=start.Date)&&(to.SelectedDate is not{} end||r.Transaction.OccurredAt.Date<=end.Date)&&(r.Matches(term)||matched.Contains(r.Transaction.RecipientKey))).ToArray();}
        search.TextChanged+=(_,_)=>Filter();from.SelectedDateChanged+=(_,_)=>Filter();to.SelectedDateChanged+=(_,_)=>Filter();
        var actions=new WrapPanel();
        foreach(var state in new[]{"Confirmed","Failed","Reversed"})actions.Children.Add(BusinessForm.Button("Service_Action"+state,async(_,_)=>await Run(async()=>
        {
            if(table.SelectedItem is not ServiceHistoryRow row)throw new DomainException("Service_SelectTransaction");
            if(state=="Failed")await _ledger.SetStatusAsync(row.Transaction.Id,state,false);
            else
            {
                var reference=await ServiceConfirmPanel.ShowAsync(row.Transaction.Reference);if(reference is null)return;
                if(state=="Reversed")await _ledger.ReverseAsync(row.Transaction.Id,reference,true);else await _ledger.SetStatusAsync(row.Transaction.Id,state,true,reference);
            }
            await LoadAsync();await HistoryAsync();
        }),"GhostButton"));
        actions.Children.Add(BusinessForm.Button("Receipt_Preview",async(_,_)=>await Run(async()=>{if(table.SelectedItem is not ServiceHistoryRow row||row.Posting is null)throw new DomainException("Service_SelectCompleted");await _receipts.ShowServiceAsync(row.Transaction,row.Posting,_business);})));panel.Children.Add(actions);_page.Content=panel;
    }
    private void Providers()
    {
        var panel=new StackPanel();var f=new BusinessForm();var state=new ProviderEntry();
        var pick=new ComboBox{ItemsSource=_providers,DisplayMemberPath="Name"};f.Add("Service_EditProvider",pick,true);
        pick.SelectionChanged+=(_,_)=>{if(pick.SelectedItem is ServiceProviderAccount p){state.Id=p.Id;state.CatalogKey=p.CatalogKey;state.Name=p.Name;state.Opening=p.OpeningFloat.ToString(CultureInfo.InvariantCulture);state.FeePolicy=p.FeePolicy;state.CommissionPolicy=p.CommissionPolicy;state.CustomerFeeMode=p.CustomerFeeMode;state.CustomerFeePercent=p.CustomerFeePercent.ToString(CultureInfo.InvariantCulture);state.AccountNumberLengths=p.AccountNumberLengths;state.CustomerFee=p.DefaultCustomerFee.ToString(CultureInfo.InvariantCulture);state.ProviderFee=p.DefaultProviderFee.ToString(CultureInfo.InvariantCulture);state.Commission=p.DefaultCommission.ToString(CultureInfo.InvariantCulture);state.LoadCostRate=p.LoadCostRate.ToString(CultureInfo.InvariantCulture);state.Active=p.IsActive;state.CashIn=p.AllowedTypes.Contains("CASHIN");state.CashOut=p.AllowedTypes.Contains("CASHOUT");state.Transfer=p.AllowedTypes.Contains("TRANSFER")||p.AllowedTypes.Contains("CASHOUT");state.Load=p.AllowedTypes.Contains("LOAD");state.Package=p.AllowedTypes.Contains("PACKAGE");foreach(var input in f.Fields.Values){if(input is TextBox t)t.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();if(input is CheckBox c)c.GetBindingExpression(CheckBox.IsCheckedProperty)?.UpdateTarget();}}};
        f.Add("",BusinessForm.Button("Category_New",(_,_)=>Providers(),"GhostButton"));f.Text("Service_Provider",state,nameof(state.Name),limit:100);
        f.Check("Service_TRANSFER",state,nameof(state.Transfer));f.Check("Service_LOAD",state,nameof(state.Load));f.Check("Service_PACKAGE",state,nameof(state.Package));
        f.Select("Digital_ChargeMode",state,nameof(state.CustomerFeeMode),("FIXED","Digital_FixedCharge"),("PERCENT","Digital_PercentCharge"));f.Text("Service_DefaultCustomerFee",state,nameof(state.CustomerFee),true);f.Text("Digital_PercentCharge",state,nameof(state.CustomerFeePercent),true);f.Text("Digital_AccountLengths",state,nameof(state.AccountNumberLengths),true,80);f.Add("",BusinessForm.Label("Digital_AccountLengthsHelp"),true);f.Text("Service_DefaultProviderFee",state,nameof(state.ProviderFee),true);f.Text("Service_DefaultCommission",state,nameof(state.Commission),true);f.Text("Service_LoadCostRate",state,nameof(state.LoadCostRate),true);
        f.Add("",BusinessForm.Label("Service_SettlementDefaults"),true);
        f.Text("Service_FeePolicy",state,nameof(state.FeePolicy),limit:300);f.Text("Service_CommissionPolicy",state,nameof(state.CommissionPolicy),limit:300);f.Check("Service_Active",state,nameof(state.Active));
        f.Add("",BusinessForm.Button("Service_SaveConfig",async(_,_)=>await Run(async()=>{var types=new List<string>();if(state.Transfer)types.AddRange(["CASHOUT","TRANSFER"]);if(state.Load)types.Add("LOAD");if(state.Package)types.Add("PACKAGE");await _ledger.SaveProviderAsync(new(){Id=state.Id,CatalogKey=state.CatalogKey,Name=state.Name.Trim(),OpeningFloat=N(state.Opening),DefaultCustomerFee=N(state.CustomerFee),CustomerFeeMode=state.CustomerFeeMode,CustomerFeePercent=N(state.CustomerFeePercent),AccountNumberLengths=state.AccountNumberLengths.Trim(),DefaultProviderFee=N(state.ProviderFee),DefaultCommission=N(state.Commission),LoadCostRate=N(state.LoadCostRate),AllowedTypes=string.Join(',',types),FeePolicy=state.FeePolicy,CommissionPolicy=state.CommissionPolicy,IsActive=state.Active});await LoadAsync();Providers();_status.Text=T("Settings_Saved");})),true);
        f.Add("",BusinessForm.Label("Service_ProviderSuggestions"),true);panel.Children.Add(f);_page.Content=panel;
    }
    private void Packages()
    {
        var f=new BusinessForm(3);var state=new PackageEntry();
        var provider=new ComboBox{ItemsSource=_providers.Where(p=>p.IsActive&&p.AllowedTypes.Split(',').Contains("PACKAGE")),DisplayMemberPath="Name",SelectedValuePath="Id"};
        provider.SetBinding(ComboBox.SelectedValueProperty,new System.Windows.Data.Binding(nameof(state.ProviderId)){Source=state,Mode=System.Windows.Data.BindingMode.TwoWay});
        f.Field("Service_Provider",nameof(state.ProviderId),provider);
        var pick=new ComboBox{ItemsSource=_packages,DisplayMemberPath="Name",IsEditable=true,IsTextSearchEnabled=false};f.Add("Service_EditPackage",pick,true);
        if(_references is not null)
        {
            var sources=new ComboBox{IsEditable=true,IsTextSearchEnabled=false,DisplayMemberPath="Name",MaxDropDownHeight=360};f.Add("Digital_PackageSettings",sources,true);
            sources.Loaded+=async(_,_)=>{sources.ItemsSource=await _references.SearchAsync("","package",1000);};
            sources.SelectionChanged+=async(_,_)=>
            {
                if(sources.SelectedItem is not RetailReference reference)return;
                if(reference.Category is "directory" or "directory_only"||reference.State=="directory_not_sellable"){_status.Text=T("Service_NoPackages");return;}
                state.Id=_packages.FirstOrDefault(p=>p.CatalogKey==reference.Key)?.Id??0;state.CatalogKey=reference.Key;state.Name=reference.Name;state.Identifier=reference.Key;state.Benefits=ReferenceText.Format(reference.Details);if(state.Benefits.Length>1000)state.Benefits=state.Benefits[..1000];state.Validity=await _references.PackageValidityAsync(reference.Key);state.Price=decimal.TryParse(reference.SourcePrice,NumberStyles.Number,CultureInfo.InvariantCulture,out var amount)?amount.ToString(CultureInfo.InvariantCulture):"";state.Cost="";state.ProviderId=_providers.FirstOrDefault(p=>p.CatalogKey=="network:"+reference.Model||p.Name==reference.Brand)?.Id??0;
                foreach(var control in f.Fields.Values){if(control is TextBox t)t.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();if(control is ComboBox c)c.GetBindingExpression(ComboBox.SelectedValueProperty)?.UpdateTarget();}
            };
        }
        var accountTypes=new StackPanel{Orientation=Orientation.Horizontal};var accountGroup="edit-package-"+Guid.NewGuid().ToString("N");
        foreach(var code in new[]{"Prepaid","Postpaid"}){var radio=new RadioButton{Tag=code,Content=T("Service_"+code),GroupName=accountGroup,IsChecked=state.AccountType==code,Margin=new Thickness(4,0,16,0)};radio.Checked+=(_,_)=>state.AccountType=code;accountTypes.Children.Add(radio);}
        f.Field("Service_AccountType",nameof(state.AccountType),accountTypes);f.Add("",BusinessForm.Button("Category_New",(_,_)=>Packages(),"GhostButton"));
        f.Text("Service_PackageId",state,nameof(state.Identifier),limit:100);f.Text("Service_PackageName",state,nameof(state.Name),limit:150);
        f.Text("Service_Benefits",state,nameof(state.Benefits),limit:1000);f.Text("Service_Validity",state,nameof(state.Validity),limit:100);
        f.Text("Products_Price",state,nameof(state.Price),true);f.Text("Digital_OptionalCost",state,nameof(state.Cost),true);f.Text("Service_Commission",state,nameof(state.Commission),true);
        f.Text("Service_CustomerFee",state,nameof(state.CustomerFee),true);f.Text("Service_ProviderFee",state,nameof(state.ProviderFee),true);f.Check("Service_Active",state,nameof(state.Active));
        pick.SelectionChanged+=(_,_)=>{if(pick.SelectedItem is MobileServicePackage p)
        {
            state.Id=p.Id;state.CatalogKey=p.CatalogKey;state.ProviderId=p.ProviderId;state.Name=p.Name;state.Kind=p.Kind;state.Price=p.Price.ToString(CultureInfo.InvariantCulture);state.Cost=p.ProviderCostKnown?p.ProviderCost.ToString(CultureInfo.InvariantCulture):"";state.Commission=p.Commission.ToString(CultureInfo.InvariantCulture);state.Validity=p.Validity;state.Active=p.IsActive;
            state.AccountType=p.AccountType;foreach(var radio in accountTypes.Children.OfType<RadioButton>())radio.IsChecked=Equals(radio.Tag,state.AccountType);state.Identifier=p.PackageIdentifier;state.Benefits=p.Benefits;state.CustomerFee=p.CustomerFee.ToString(CultureInfo.InvariantCulture);state.ProviderFee=p.ProviderFee.ToString(CultureInfo.InvariantCulture);
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
            errors.Remove(nameof(state.Cost));if(state.Cost.Length>0&&(!decimal.TryParse(state.Cost,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var cost)||cost<0||cost!=Money.Round2(cost)))errors[nameof(state.Cost)]="Service_InvalidAmount";
            f.ShowErrors(errors,true);if(errors.Count>0)return;
            await _ledger.SavePackageAsync(new(){Id=state.Id,CatalogKey=state.CatalogKey,ProviderId=state.ProviderId,Name=state.Name.Trim(),Kind=state.Kind,AccountType=state.AccountType,PackageIdentifier=state.Identifier.Trim(),Benefits=state.Benefits.Trim(),CustomerFee=N(state.CustomerFee),ProviderFee=N(state.ProviderFee),Price=N(state.Price),ProviderCost=state.Cost.Length==0?0:N(state.Cost),ProviderCostKnown=state.Cost.Length>0,Commission=N(state.Commission),Validity=state.Validity,IsActive=state.Active});
            await LoadAsync();Packages();_status.Text=T("Settings_Saved");
        })),true);_page.Content=f;
    }
    private async Task ReportsAsync(DateTime? from=null,DateTime? to=null)
    {
        var start=from??DateTime.Today;var end=to??DateTime.Today;var f=new BusinessForm();var first=new DatePicker{SelectedDate=start};var last=new DatePicker{SelectedDate=end};f.Add("Reports_From",first);f.Add("Reports_To",last);
        f.Add("",BusinessForm.Button("Reports_Generate",async(_,_)=>await Run(()=>ReportsAsync(first.SelectedDate,last.SelectedDate))),true);
        if(end<start)throw new DomainException("Service_Invalid");var report=await _ledger.ReportAsync(start,end.AddDays(1));
        foreach(var pair in new[]{("Service_PhysicalRevenue",report.PhysicalRevenue),("Service_LooseRevenue",report.LooseRevenue),("Service_Income",report.Income),("Service_CashChange",report.CashChange),("Service_CASHIN",report.CashIn),("Service_CASHOUT",report.CashOut),("Service_TRANSFER",report.Transfers),("Service_RechargeCollected",report.RechargeCollected),("Service_Commission",report.Commissions)})f.Add(pair.Item1,new TextBlock{Text=Money.Format(pair.Item2)});
        if(report.UnknownCosts>0)f.Add("Digital_UnknownCosts",new TextBlock{Text=report.UnknownCosts.ToString()},true);
        f.Add("Service_Pending",new TextBlock{Text=report.Pending.ToString()});f.Add("Service_FailedReversed",new TextBlock{Text=report.FailedReversed.ToString()});
        _page.Content=f;
    }
    private static DataGrid Table<TItem>(IEnumerable<TItem> rows,string key,params(string Key,string Property)[] columns)
    {
        var grid=new DataGrid{ItemsSource=rows,AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,SelectionMode=DataGridSelectionMode.Single,MinHeight=240,MaxHeight=520};TableLayout.SetKey(grid,key);
        foreach(var c in columns)grid.Columns.Add(new DataGridTextColumn{Header=T(c.Key),Binding=new System.Windows.Data.Binding(c.Property),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});return grid;
    }
    private static decimal N(string value)=>decimal.TryParse(value,NumberStyles.AllowDecimalPoint|NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var n)?n:throw new DomainException("Common_InvalidNumber");
    private async Task Run(Func<Task> action){if(_busy)return;_busy=true;IsEnabled=false;try{await action();}catch(Exception e){_status.Text=T(e is DomainException?e.Message:"Service_Error");_status.SetResourceReference(TextBlock.ForegroundProperty,"DangerBrush");}finally{IsEnabled=true;_busy=false;RefreshCartSummary();}}
    private sealed class ServiceEntry
    {
        public string AccountType{get;set;}="Prepaid";public int ProviderId{get;set;}public string Type{get;set;}="LOAD";
        public string Principal{get;set;}="";public string CustomerFee{get;set;}="0";public string Phone{get;set;}="";public string Reference{get;set;}="";public string ProviderCost{get;set;}="";public string CustomerName{get;set;}="";
        public string Outcome{get;set;}="Confirmed";
        public void Reset(){Principal=Phone=Reference=ProviderCost=CustomerName="";Outcome="Confirmed";}
    }
    private sealed class ProviderEntry{public string CustomerFeeMode{get;set;}="FIXED";public string CustomerFeePercent{get;set;}="0";public string AccountNumberLengths{get;set;}="";public string CatalogKey="";public string CustomerFee{get;set;}="0";public string ProviderFee{get;set;}="0";public string Commission{get;set;}="0";public string LoadCostRate{get;set;}="1";public int Id;public string Name{get;set;}="";public string Opening{get;set;}="0";public string FeePolicy{get;set;}="";public string CommissionPolicy{get;set;}="";public bool Active{get;set;}=true;public bool Transfer{get;set;}=true;public bool CashIn{get;set;}=true;public bool CashOut{get;set;}=true;public bool Load{get;set;}=true;public bool Package{get;set;}=true;}
    private sealed class PackageEntry{public string CatalogKey="";public string AccountType{get;set;}="Prepaid";public string Identifier{get;set;}="";public string Benefits{get;set;}="";public string CustomerFee{get;set;}="0";public string ProviderFee{get;set;}="0";public int Id;public int ProviderId{get;set;}public string Name{get;set;}="";public string Kind{get;set;}="";public string Price{get;set;}="0";public string Cost{get;set;}="";public string Commission{get;set;}="0";public string Validity{get;set;}="";public bool Active{get;set;}=true;}
}
public sealed record ServiceHistoryRow(ServiceTransaction Transaction,ServicePosting? Posting)
{
    public string PostedDate=>Posting is null?"":(Posting.PostedAt==default?Transaction.OccurredAt:Posting.PostedAt).ToString("yyyy-MM-dd HH:mm");
    public string Recipient=>Transaction.CustomerPhone;public string CustomerName=>Transaction.CustomerName;public string Fee=>Money.Format(Transaction.CustomerFee);
    public bool Matches(string term)=>new[]{Number,Recipient,CustomerName,Provider,Type,Transaction.PackageName}.Any(v=>v.Contains(term,StringComparison.CurrentCultureIgnoreCase));
    public string Number=>Transaction.ReceiptNumber;public string Date=>Transaction.OccurredAt.ToString("yyyy-MM-dd HH:mm");public string Provider=>Transaction.ProviderName;public string Type=>LocalizationManager.Instance["Service_"+Transaction.Type];public string Status=>LocalizationManager.Instance["Service_"+Transaction.Status];public string Principal=>Money.Format(Transaction.Principal);public string Income=>Transaction.Type is "LOAD" or "PACKAGE"&&!Transaction.ProviderCostKnown?LocalizationManager.Instance["Digital_NotRecorded"]:Money.Format(Posting?.Income??0);public string Reference=>Transaction.Reference;
}
internal static class ServiceConfirmPanel
{
    public static async Task<string?> ShowAsync(string reference)
    {
        var f=new BusinessForm();var input=new TextBox{Text=reference,MaxLength=80};f.Add("Digital_ReferenceOptional",input,true);
        f.Add("",BusinessForm.Label("Digital_ConfirmResultHelp"),true);Action close=()=>{};string? result=null;
        f.Add("",BusinessForm.Button("Common_Cancel",(_,_)=>close(),"GhostButton"));var confirm=BusinessForm.Button("Common_Confirm",(_,_)=>{result=input.Text.Trim();close();});confirm.IsDefault=true;f.Add("",confirm);
        await InAppDialog.ShowAsync(f,c=>close=c,contentWidth:650,sizeToContent:true);return result;
    }
}
