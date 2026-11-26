using System.Windows;
using System.Windows.Controls;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Wpf.Common;
namespace SupermarketPos.Wpf.Views;

public sealed partial class DigitalServicesView
{
    private readonly RetailInventory? _retailInventory;
    private readonly RetailReferenceCatalog? _references;
    private readonly IProductRepository? _products;
    private readonly string _accountGroup="package-account-"+Guid.NewGuid().ToString("N");
    public event Func<Product,System.Threading.Tasks.Task>? ProductChosen;
    private async Task CatalogAsync()
    {
        if(_retailInventory is null||_products is null||_references is null){Record();return;}
        await _retailInventory.EnsureProviderTemplateAsync();await _retailInventory.EnsureConfiguredPackagesAsync();await LoadAsync();
        var panel=new DockPanel{Margin=new Thickness(20)};var head=new StackPanel();DockPanel.SetDock(head,Dock.Top);panel.Children.Add(head);
        var title=BusinessForm.Label("Service_Title");title.SetResourceReference(TextBlock.StyleProperty,"H1");head.Children.Add(title);
        var actions=new WrapPanel();
        actions.Children.Add(BusinessForm.Button("Retail_NewService",async(_,_)=>await Run(()=>EditServiceAsync(null))));
        actions.Children.Add(BusinessForm.Button("Retail_Templates",async(_,_)=>await Run(async()=>
        {
            Action close=()=>{};var templates=new RetailTemplatePanel(_references,_retailInventory,async _=>{close();await CatalogAsync();},true);
            await InAppDialog.ShowAsync(templates,c=>close=c,contentWidth:1000);await CatalogAsync();
        }),"GhostButton"));
        foreach(var page in new[]{"Record","Providers","Packages","History","Reports"})actions.Children.Add(BusinessForm.Button("Service_"+page,async(_,_)=>await Run(async()=>{await LoadAsync();await ShowAsync(page);}),"GhostButton"));
        head.Children.Add(actions);var search=new TextBox{Margin=new Thickness(4,10,4,10),Name="DigitalCatalogSearch",MaxLength=150};head.Children.Add(search);
        var rows=(await _products.GetAllAsync()).Where(p=>p.Kind=="digital"&&p.IsActive).ToArray();
        var table=Table(rows,"digital-products",("Products_Name","Name"),("Service_Type","ServiceType"),("Products_Price","UnitPrice"));
        var bottom=new WrapPanel();DockPanel.SetDock(bottom,Dock.Bottom);panel.Children.Add(bottom);
        bottom.Children.Add(BusinessForm.Button("Products_EditTitle",async(_,_)=>await Run(async()=>{if(table.SelectedItem is Product selected){if(selected.ServiceType=="PACKAGE")Packages();else await EditServiceAsync(selected);}}),"GhostButton"));
        bottom.Children.Add(BusinessForm.Button("Purchase_AddItem",async(_,_)=>{if(table.SelectedItem is Product selected&&ProductChosen is{} choose)await choose(selected);}));
        search.TextChanged+=(_,_)=>table.ItemsSource=rows.Where(p=>p.Name.Contains(search.Text,StringComparison.CurrentCultureIgnoreCase)).OrderBy(p=>p.Name).ToArray();
        panel.Children.Add(table);_page.Content=panel;
    }
    private async Task EditServiceAsync(Product? selected)
    {
        if(_retailInventory is null)return;
        Action close=()=>{};var form=new BusinessForm();var name=new TextBox{Text=selected?.Name??"",MaxLength=200};var price=new TextBox{Text=selected?.UnitPrice.ToString(System.Globalization.CultureInfo.InvariantCulture)??""};
        var types=new ComboBox{ItemsSource=new[]{"SERVICE","LOAD","CASHIN","CASHOUT","TRANSFER"}.Select(c=>new ServiceChoice(c,T("Service_"+c))),DisplayMemberPath="Name",SelectedValuePath="Code",SelectedValue=selected?.ServiceType??"SERVICE"};
        var provider=new ComboBox{ItemsSource=_providers,DisplayMemberPath="Name",SelectedValuePath="Id",SelectedValue=selected?.ServiceProviderId};
        form.Add("Products_Name",name,true);form.Add("Service_Type",types);form.Add("Service_Provider",provider);form.Add("Products_Price",price);
        var error=new TextBlock{TextWrapping=TextWrapping.Wrap};form.Add("",error,true);
        form.Add("",BusinessForm.Button("Common_Cancel",(_,_)=>close(),"GhostButton"));form.Add("",BusinessForm.Button("Common_Save",async(sender,_)=>
        {
            ((Button)sender).IsEnabled=false;
            try
            {
                if(string.IsNullOrWhiteSpace(name.Text)||!decimal.TryParse(price.Text,System.Globalization.NumberStyles.AllowDecimalPoint,System.Globalization.CultureInfo.InvariantCulture,out var amount)||amount<0)throw new DomainException("Retail_EnterPrice");
                var type=(string?)types.SelectedValue??"SERVICE";
                await _retailInventory.SaveServiceAsync(selected?.Id??0,name.Text,amount,type,type=="SERVICE"?null:(int?)provider.SelectedValue);
                close();await CatalogAsync();
            }
            catch(Exception e){error.Text=T(e.Message);}
            finally{((Button)sender).IsEnabled=true;}
        }));await InAppDialog.ShowAsync(form,c=>close=c,contentWidth:780);
    }
    private sealed record ServiceChoice(string Code,string Name);
}
