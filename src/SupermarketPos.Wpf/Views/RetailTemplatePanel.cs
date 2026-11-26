using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;
namespace SupermarketPos.Wpf.Views;

public sealed class RetailTemplatePanel:DockPanel
{
    private static string T(string key)=>LocalizationManager.Instance[key];
    public RetailTemplatePanel(RetailReferenceCatalog catalog,RetailInventory inventory,Func<Product,Task> selected,bool digital=false)
    {
        LastChildFill=true;var top=new StackPanel();SetDock(top,Dock.Top);Children.Add(top);
        top.Children.Add(BusinessForm.Label("Retail_Templates"));
        var kind=new ComboBox{ItemsSource=digital?new[]{new Choice("package",T("Service_Packages")),new Choice("provider",T("Service_Providers"))}:new[]{new Choice("model",T("Retail_Models"))},DisplayMemberPath="Name",SelectedValuePath="Code",SelectedIndex=0,Margin=new Thickness(4)};
        top.Children.Add(kind);var search=new TextBox{Margin=new Thickness(4),MaxLength=150};search.ToolTip=T("Retail_SearchTemplate");top.Children.Add(search);
        var details=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(6)};SetDock(details,Dock.Bottom);Children.Add(details);
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(6)};SetDock(status,Dock.Bottom);Children.Add(status);
        Button use=null!;
        use=BusinessForm.Button("Retail_UseTemplate",async(_,_)=>
        {
            if(List?.SelectedItem is not RetailReference reference)return;useGuard(true);
            try
            {
                if(reference.Kind is "provider" or "network"){await inventory.UseProviderAsync(reference);status.Text=T("Settings_Saved");return;}
                var product=reference.Kind=="package"?await ConfigurePackageAsync(inventory,reference):await inventory.QuickAddAsync(reference.Name,"labeled",reference);
                if(product is not null)await selected(product);
            }
            catch(Exception e){status.Text=T(e.Message);}
            finally{useGuard(false);}
        });SetDock(use,Dock.Bottom);Children.Add(use);
        List=new ListBox{DisplayMemberPath="Caption",MinHeight=180,MaxHeight=360,Margin=new Thickness(4)};Children.Add(List);
        List.SelectionChanged+=(_,_)=>{if(List.SelectedItem is RetailReference r)details.Text=r.Details+"\n"+T("Retail_ReferencePrice")+": "+r.SourcePrice+"\n"+r.State+" · "+r.SourceUrl;};
        var version=0;async Task Load(){var current=++version;try{var rows=await catalog.SearchAsync(search.Text,(string?)kind.SelectedValue??"model");if(current==version)List.ItemsSource=rows;}catch(Exception e){status.Text=T(e.Message);}}
        search.TextChanged+=async(_,_)=>await Load();kind.SelectionChanged+=async(_,_)=>await Load();Loaded+=async(_,_)=>{await Load();search.Focus();};
        void useGuard(bool busy){use.IsEnabled=!busy;search.IsEnabled=!busy;kind.IsEnabled=!busy;}
    }
    public ListBox List{get;}
    private sealed record Choice(string Code,string Name);
    public static async Task<Product?> ConfigurePackageAsync(RetailInventory inventory,RetailReference reference)
    {
        if(reference.Category is "directory" or "directory_only"||reference.State=="directory_not_sellable")throw new SupermarketPos.Domain.Common.DomainException("Service_NoPackages");
        Product? result=null;Action close=()=>{};var form=new BusinessForm();var price=new TextBox();var cost=new TextBox();var error=new TextBlock{TextWrapping=TextWrapping.Wrap};
        form.Add("Service_PackageName",new TextBlock{Text=reference.Name,TextWrapping=TextWrapping.Wrap},true);
        form.Add("Retail_ReferencePrice",new TextBlock{Text=reference.SourcePrice+" · "+reference.State,TextWrapping=TextWrapping.Wrap},true);
        form.Field("Products_Price","price",price);form.Field("Service_ProviderCost","cost",cost);
        var verified=new CheckBox{Content=BusinessForm.Label("Retail_VerifyPackage")};form.Add("",verified,true);form.Add("",error,true);
        form.Add("",BusinessForm.Button("Common_Cancel",(_,_)=>close(),"GhostButton"));
        form.Add("",BusinessForm.Button("Common_Save",async(sender,_)=>
        {
            ((Button)sender).IsEnabled=false;
            try
            {
                var errors=new Dictionary<string,string>();
                if(!decimal.TryParse(price.Text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var p)||p<=0)errors["price"]="Service_InvalidAmount";
                if(!decimal.TryParse(cost.Text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var c)||c<0)errors["cost"]="Service_InvalidAmount";
                form.ShowErrors(errors,true);
                if(verified.IsChecked!=true){error.Text=T("Retail_VerifyPackage");return;}
                if(errors.Count>0){error.Text=T("Service_InvalidAmount");return;}
                result=await inventory.QuickAddAsync(reference.Name,"digital",reference,p,c);close();
            }
            catch(Exception e){error.Text=T(e.Message);}
            finally{((Button)sender).IsEnabled=true;}
        }));
        await InAppDialog.ShowAsync(form,c=>close=c,contentWidth:720);return result;
    }
}
