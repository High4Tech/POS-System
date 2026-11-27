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
    public RetailTemplatePanel(RetailReferenceCatalog catalog,RetailInventory inventory,Func<Product,Task> selected)
    {
        LastChildFill=true;var top=new StackPanel();SetDock(top,Dock.Top);Children.Add(top);
        top.Children.Add(BusinessForm.Label("Retail_Templates"));
        var kind=new ComboBox{ItemsSource=new[]{new Choice("model",T("Retail_Models"))},DisplayMemberPath="Name",SelectedValuePath="Code",SelectedIndex=0,Margin=new Thickness(4)};
        top.Children.Add(kind);var search=new TextBox{Margin=new Thickness(4),MaxLength=150};search.ToolTip=T("Retail_SearchTemplate");top.Children.Add(search);
        var details=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(6)};SetDock(details,Dock.Bottom);Children.Add(details);
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(6)};SetDock(status,Dock.Bottom);Children.Add(status);
        Button use=null!;
        use=BusinessForm.Button("Retail_UseTemplate",async(_,_)=>
        {
            if(List?.SelectedItem is not RetailReference reference)return;useGuard(true);
            try
            {
                var product=await inventory.QuickAddAsync(reference.Name,"labeled",reference);
                if(product is not null)await selected(product);
            }
            catch(Exception e){status.Text=T(e.Message);}
            finally{useGuard(false);}
        });SetDock(use,Dock.Bottom);Children.Add(use);
        List=new ListBox{DisplayMemberPath="Caption",MinHeight=180,MaxHeight=360,Margin=new Thickness(4)};Children.Add(List);
        List.MouseDoubleClick+=(_,_)=>use.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        List.PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){e.Handled=true;use.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}};
        List.SelectionChanged+=(_,_)=>{if(List.SelectedItem is RetailReference r)details.Text=ReferenceText.Format(r.Details);};
        var version=0;async Task Load(){var current=++version;try{var rows=await catalog.SearchAsync(search.Text,(string?)kind.SelectedValue??"model");if(current==version)List.ItemsSource=rows;}catch(Exception e){status.Text=T(e.Message);}}
        search.TextChanged+=async(_,_)=>await Load();kind.SelectionChanged+=async(_,_)=>await Load();Loaded+=async(_,_)=>{await Load();search.Focus();};
        void useGuard(bool busy){use.IsEnabled=!busy;search.IsEnabled=!busy;kind.IsEnabled=!busy;}
    }
    public ListBox List{get;}
    private sealed record Choice(string Code,string Name);
}
