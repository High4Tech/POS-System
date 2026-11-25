using System.Windows;
using System.Windows.Controls;
using QuestPDF.Fluent;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Infrastructure.Documents;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;
namespace SupermarketPos.Wpf.Views;
public sealed class StockLocationsPanel:UserControl
{
    public StockLocationsPanel(StockLocationService service,IProductRepository products,PosStorage storage,Action close)
    {
        var form=new BusinessForm();var location=new ComboBox{DisplayMemberPath="Name"};var parent=new ComboBox{DisplayMemberPath="Name",SelectedValuePath="Code"};var name=new TextBox{MaxLength=100};var error=new TextBlock{TextWrapping=TextWrapping.Wrap};
        var product=new ComboBox{DisplayMemberPath="Name",IsEditable=true};var batch=new ComboBox{DisplayMemberPath="Lot"};var count=new TextBox{FlowDirection=FlowDirection.LeftToRight};var reason=new TextBox{MaxLength=1000};
        var labelSelection=new ListBox{DisplayMemberPath="Name",SelectionMode=SelectionMode.Multiple,Height=100};
        var labelScope=new ComboBox{ItemsSource=new[]{new BusinessForm.Choice("selected",T("Pharmacy_SelectedLabels")),new("rack",T("Pharmacy_RackLabels")),new("all",T("Pharmacy_AllLocations"))},DisplayMemberPath="Name",SelectedValuePath="Code",SelectedIndex=2};
        var labelFormat=new ComboBox{ItemsSource=new[]{"Code 128","QR"},SelectedIndex=0};
        async Task Reload(){var all=await service.LocationsAsync();location.ItemsSource=all;labelSelection.ItemsSource=all;parent.ItemsSource=new[]{new BusinessForm.Choice("0",T("Pharmacy_NewRack"))}.Concat(all.Where(l=>l.ParentId is null).Select(l=>new BusinessForm.Choice(l.Id.ToString(),l.Name))).ToArray();parent.SelectedIndex=0;product.ItemsSource=await products.GetAllAsync();}
        async Task Run(Func<Task> action){try{error.Text="";IsEnabled=false;await action();}catch(Exception ex){error.Text=T(ex is DomainException?ex.Message:"Pharmacy_SaveError");}finally{IsEnabled=true;}}
        form.Add("Pharmacy_Locations",location);form.Add("Pharmacy_ParentRack",parent);form.Add("Products_Name",name,true);form.Add("",BusinessForm.Button("Common_Save",async(_,_)=>await Run(async()=>{var selected=location.SelectedItem as StockLocation;var saved=await service.SaveLocationAsync(name.Text,int.TryParse(parent.SelectedValue as string,out var id)&&id>0?id:null,selected?.Id);await Reload();location.SelectedItem=((StockLocation[])location.ItemsSource).First(l=>l.Id==saved.Id);})));form.Add("",BusinessForm.Button("Category_New",(_,_)=>{location.SelectedIndex=-1;name.Text="";parent.SelectedIndex=0;},"GhostButton"));
        location.SelectionChanged+=(_,_)=>{parent.IsEnabled=location.SelectedItem is null;if(location.SelectedItem is StockLocation l){name.Text=l.Name;parent.SelectedValue=(l.ParentId??0).ToString();}};
        form.Add("Pharmacy_LabelScope",labelScope);form.Add("Pharmacy_LabelFormat",labelFormat);form.Add("Pharmacy_SelectedLabels",labelSelection,true);
        form.Add("",BusinessForm.Button("Pharmacy_PrintLocationLabels",async(_,_)=>await Run(async()=>
        {
            var all=await service.LocationsAsync();var selected=location.SelectedItem as StockLocation;var rack=selected?.ParentId??selected?.Id;
            var labels=(labelScope.SelectedValue as string) switch{"selected"=>labelSelection.SelectedItems.Cast<StockLocation>().ToArray(),"rack"=>all.Where(l=>l.Id==rack||l.ParentId==rack).ToArray(),_=>all};
            if(labels.Length==0)throw new DomainException("Pharmacy_SelectStockLocation");if(storage.ChoosePdfPath("Rack and shelf labels.pdf") is{} path)await System.IO.File.WriteAllBytesAsync(path,new LocationLabelDocument(labels,LocalizationManager.Instance.CurrentLanguage,labelFormat.SelectedIndex==1,all).GeneratePdf());
        })),true);
        form.Add("Pos_Product",product);form.Add("Batch_Lot",batch);form.Add("Pharmacy_BaseQuantity",count);form.Add("Pharmacy_VerificationNote",reason);
        product.SelectionChanged+=async(_,_)=>await Run(async()=>{if(product.SelectedItem is Product p){var current=await products.GetCurrentForRegisterAsync(p.Id);batch.ItemsSource=current?.Batches;batch.SelectedIndex=-1;}});
        form.Add("",BusinessForm.Button("Pharmacy_AssignLocation",async(_,_)=>await Run(async()=>{if(product.SelectedItem is not Product p||location.SelectedItem is not StockLocation l||!long.TryParse(count.Text,out var qty))throw new DomainException("Pharmacy_InvalidProduct");await service.AssignOpeningAsync(p.Id,(batch.SelectedItem as InventoryBatch)?.Id,l.Id,qty,reason.Text);count.Text="";reason.Text="";})),true);
        form.Add("",error,true);form.Add("",BusinessForm.Button("Common_Close",(_,_)=>close(),"GhostButton"),true);Content=form;Loaded+=async(_,_)=>await Run(Reload);
    }
    private static string T(string key)=>LocalizationManager.Instance[key];
}
