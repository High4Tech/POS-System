using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Wpf.Common;
namespace SupermarketPos.Wpf.Views;
public sealed partial class DigitalServicesView
{
    private sealed record PackageChoice(MobileServicePackage? Package,RetailReference? Reference,string Caption,string Details,string Price);
    private void PackageCatalog(StackPanel panel)
    {
        var providers=_providers.Where(p=>p.IsActive&&p.AllowedTypes.Split(',').Contains("PACKAGE")).ToArray();
        if(!providers.Any(p=>p.Name==_network))_network=providers.FirstOrDefault()?.Name??"";
        var network=new ComboBox{ItemsSource=providers,DisplayMemberPath="Name",SelectedValuePath="Name",SelectedValue=_network,MinWidth=220,Margin=new Thickness(4)};
        panel.Children.Add(BusinessForm.Label("Retail_SelectNetwork"));panel.Children.Add(network);
        network.SelectionChanged+=(_,_)=>{_network=(string?)network.SelectedValue??"";Record();};
        var accounts=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(4,8,4,8)};
        foreach(var type in new[]{"Prepaid","Postpaid"}){var radio=new RadioButton{Content=T("Service_"+type),GroupName=_accountGroup,IsChecked=_accountType==type,Margin=new Thickness(4,0,18,0)};radio.Checked+=(_,_)=>{_accountType=type;Record();};accounts.Children.Add(radio);}panel.Children.Add(accounts);
        var search=new TextBox{Name="PackageSearch",MaxLength=150,Margin=new Thickness(4,8,4,8),ToolTip=T("Service_SearchPackages")};System.Windows.Automation.AutomationProperties.SetName(search,T("Service_SearchPackages"));panel.Children.Add(search);
        var list=new ListBox{Name="PackageChoices",MinHeight=180,MaxHeight=500,Margin=new Thickness(4),HorizontalContentAlignment=HorizontalAlignment.Stretch};panel.Children.Add(list);
        var factory=new FrameworkElementFactory(typeof(StackPanel));
        var name=new FrameworkElementFactory(typeof(TextBlock));name.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Caption"));name.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);name.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);factory.AppendChild(name);
        var details=new FrameworkElementFactory(typeof(TextBlock));details.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Details"));details.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);details.SetValue(FrameworkElement.MarginProperty,new Thickness(0,4,0,10));factory.AppendChild(details);var priceLabel=new FrameworkElementFactory(typeof(TextBlock));priceLabel.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Price"));priceLabel.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);priceLabel.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,0,10));factory.AppendChild(priceLabel);list.ItemTemplate=new DataTemplate{VisualTree=factory};
        async Task Choose(){if(list.SelectedItem is PackageChoice choice&&!InAppDialog.IsOpen)await SellPackagePopupAsync(choice);}
        list.MouseDoubleClick+=async(_,_)=>await Choose();list.PreviewKeyDown+=async(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;await Choose();}};
        search.PreviewKeyDown+=async(_,e)=>{if(e.Key==Key.Down){list.SelectedIndex=0;list.Focus();e.Handled=true;}else if(e.Key==Key.Enter){list.SelectedIndex=Math.Max(0,list.SelectedIndex);e.Handled=true;await Choose();}};
        panel.Children.Add(BusinessForm.Button("Digital_Sell",async(_,_)=>await Choose()));
        var empty=BusinessForm.Label("Service_NoPackages");panel.Children.Add(empty);
        var version=0;async Task Filter()
        {
            var current=++version;var provider=providers.FirstOrDefault(p=>p.Name==_network);
            var local=_packages.Where(p=>p.IsActive&&p.ProviderId==provider?.Id&&p.AccountType==_accountType).ToArray();
            var rows=local.Where(p=>(p.Name+" "+p.Benefits).Contains(search.Text.Trim(),StringComparison.CurrentCultureIgnoreCase)).Select(p=>new PackageChoice(p,null,p.Name,ReferenceText.Format(p.Benefits)+"\n"+p.Validity,Money.Format(p.Price+p.CustomerFee))).ToList();
            if(_references is not null&&_accountType=="Prepaid")
            {
                var references=await _references.SearchAsync(search.Text,"package",1000);
                rows.AddRange(references.Where(r=>r.Brand.Equals(_network,StringComparison.OrdinalIgnoreCase)&&r.Category is not ("directory" or "directory_only")&&r.State!="directory_not_sellable"&&!_packages.Any(p=>p.CatalogKey==r.Key)).Select(r=>new PackageChoice(null,r,r.Name,ReferenceText.Format(r.Details),decimal.TryParse(r.SourcePrice,NumberStyles.Number,CultureInfo.InvariantCulture,out var amount)?Money.Format(amount):T("Digital_PriceOnSale"))));
            }
            if(current!=version)return;list.ItemsSource=rows.OrderBy(r=>r.Caption).ToArray();empty.Visibility=rows.Count==0?Visibility.Visible:Visibility.Collapsed;
        }
        search.TextChanged+=async(_,_)=>{try{await Filter();}catch(Exception){_status.Text=T("Service_Error");}};
        _=InitialFilter();
        async Task InitialFilter(){try{await Filter();}catch(Exception){_status.Text=T("Service_Error");}}
    }
    private async Task SellPackagePopupAsync(PackageChoice choice)
    {
        Action close=()=>{};ServiceTransaction? completed=null;var panel=new StackPanel();var form=new BusinessForm();
        var title=new TextBlock{Text=choice.Caption,TextWrapping=TextWrapping.Wrap};title.SetResourceReference(TextBlock.StyleProperty,"H1");panel.Children.Add(title);
        panel.Children.Add(new TextBlock{Text=choice.Details,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(6,4,6,12)});
        var package=choice.Package;var price=new TextBox{FlowDirection=FlowDirection.LeftToRight,Text=package is not null?(package.Price+package.CustomerFee).ToString("0.00",CultureInfo.InvariantCulture):decimal.TryParse(choice.Reference?.SourcePrice,NumberStyles.Number,CultureInfo.InvariantCulture,out var sourcePrice)?sourcePrice.ToString("0.00",CultureInfo.InvariantCulture):""};
        var cost=new TextBox{FlowDirection=FlowDirection.LeftToRight,Text=package?.ProviderCostKnown==true?package.ProviderCost.ToString("0.00",CultureInfo.InvariantCulture):""};
        form.Field("Digital_Price","price",price);form.Field("Digital_OptionalCost","cost",cost);panel.Children.Add(form);
        _entry.Type="PACKAGE";_entry.ClearConfirmation();
        ServiceTransaction Quote()
        {
            var errors=new Dictionary<string,string>();
            if(!decimal.TryParse(price.Text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var sellingPrice)||sellingPrice<=0||sellingPrice!=Money.Round2(sellingPrice))errors["price"]="Service_InvalidAmount";
            decimal? providerCost=null;if(cost.Text.Length>0){if(!decimal.TryParse(cost.Text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var c)||c<0||c!=Money.Round2(c))errors["cost"]="Service_InvalidAmount";else providerCost=c;}
            form.ShowErrors(errors,true);if(errors.Count>0)throw new DomainException("Service_InvalidAmount");
            return DigitalServiceLedger.PackageQuote(package??new MobileServicePackage{ProviderCostKnown=false},sellingPrice,providerCost,providerCost.HasValue);
        }
        form.FieldEdited+=_=>{_entry.External=false;HasDraft=true;};
        AddConfirmation(panel,Quote,()=>close(),async tx=>
        {
            if(package is null)package=await (_retailInventory??throw new DomainException("Service_Invalid")).PrepareReferencePackageAsync(choice.Reference!,tx.Collected,_accountType);
            return completed=await _ledger.SellPackageAsync(package.Id,tx.Collected,tx.ProviderCostKnown?tx.ProviderCost:null,tx.CustomerPhone,tx.Reference,tx.Status,_entry.External,tx.ConfirmationId);
        },showReceipt:false);
        panel.Children.Add(BusinessForm.Button("Digital_Back",(_,_)=>close(),"GhostButton"));
        try{await InAppDialog.ShowAsync(panel,c=>close=c,contentWidth:820);}finally{_entry.Reset();HasDraft=false;}
        if(completed?.Status=="Confirmed")await _receipts.ShowServiceAsync(completed,(await _ledger.PostingsAsync()).Single(p=>p.TransactionId==completed.Id),_business);
    }
}
