using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        var locations=new BusinessForm();var movement=new BusinessForm();var labels=new BusinessForm();
        var location=new ComboBox{DisplayMemberPath="Name"};var parent=new ComboBox{DisplayMemberPath="Name",SelectedValuePath="Code"};var name=new TextBox{MaxLength=100};var error=new TextBlock{TextWrapping=TextWrapping.Wrap};
        var product=new ComboBox{DisplayMemberPath="Name",IsEditable=true};var batch=new ComboBox{DisplayMemberPath="Lot"};var source=new ComboBox{DisplayMemberPath="Name",SelectedValuePath="Code"};var target=new ComboBox{DisplayMemberPath="Name",SelectedValuePath="Code"};var count=new TextBox{FlowDirection=FlowDirection.LeftToRight};var reason=new TextBox{MaxLength=1000};var confirmation=Guid.NewGuid();
        var labelSelection=new ListBox{DisplayMemberPath="Name",SelectionMode=SelectionMode.Multiple,Height=100};
        var labelScope=new ComboBox{ItemsSource=new[]{new BusinessForm.Choice("selected",T("Pharmacy_SelectedLabels")),new("rack",T("Pharmacy_RackLabels")),new("all",T("Pharmacy_AllLocations"))},DisplayMemberPath="Name",SelectedValuePath="Code",SelectedIndex=2};var labelFormat=new ComboBox{ItemsSource=new[]{"Code 128","QR"},SelectedIndex=0};
        async Task Reload()
        {
            var all=await service.LocationsAsync();location.ItemsSource=all;labelSelection.ItemsSource=all;parent.ItemsSource=new[]{new BusinessForm.Choice("0",T("Pharmacy_NewRack"))}.Concat(all.Where(l=>l.ParentId is null).Select(l=>new BusinessForm.Choice(l.Id.ToString(),l.Name))).ToArray();parent.SelectedIndex=0;product.ItemsSource=await products.GetAllAsync();
            var options=new[]{new BusinessForm.Choice("0",T("Pharmacy_Unassigned"))}.Concat(all.Select(l=>new BusinessForm.Choice(l.Id.ToString(),(l.ParentId is{} id?all.First(p=>p.Id==id).Name+" / ":"")+l.Name))).ToArray();source.ItemsSource=options;target.ItemsSource=options;source.SelectedIndex=0;target.SelectedIndex=-1;
        }
        async Task Run(Func<Task> action){try{error.Text="";IsEnabled=false;await action();}catch(Exception ex){error.Text=T(ex is DomainException?ex.Message:"Pharmacy_SaveError");}finally{IsEnabled=true;}}
        locations.Add("Pharmacy_Locations",location);locations.Add("Pharmacy_ParentRack",parent);locations.Add("Products_Name",name,true);
        locations.Add("",BusinessForm.Button("Common_Save",async(_,_)=>await Run(async()=>{var selected=location.SelectedItem as StockLocation;var saved=await service.SaveLocationAsync(name.Text,int.TryParse(parent.SelectedValue as string,out var id)&&id>0?id:null,selected?.Id);await Reload();location.SelectedItem=((StockLocation[])location.ItemsSource).First(l=>l.Id==saved.Id);})));
        locations.Add("",BusinessForm.Button("Category_New",(_,_)=>{location.SelectedIndex=-1;name.Text="";parent.SelectedIndex=0;},"GhostButton"));
        locations.Add("",BusinessForm.Button("Pharmacy_DeactivateLocation",async(_,_)=>await Run(async()=>{if(location.SelectedItem is not StockLocation l)throw new DomainException("Pharmacy_SelectStockLocation");await service.DeactivateAsync(l.Id);await Reload();name.Text="";}),"DangerButton"),true);
        location.SelectionChanged+=(_,_)=>{parent.IsEnabled=location.SelectedItem is null;if(location.SelectedItem is StockLocation l){name.Text=l.Name;parent.SelectedValue=(l.ParentId??0).ToString();}};
        movement.Add("Pos_Product",product);movement.Add("Batch_Lot",batch);movement.Add("Pharmacy_FromLocation",source);movement.Add("Pharmacy_ToLocation",target);movement.Add("Pharmacy_BaseQuantity",count);movement.Add("Pharmacy_VerificationNote",reason);
        var available=new TextBlock{TextWrapping=TextWrapping.Wrap};movement.Add("",available,true);
        async Task ShowStock(){if(product.SelectedItem is Product p){var places=await service.StockAsync(p.Id,(batch.SelectedItem as InventoryBatch)?.Id);available.Text=string.Join(" · ",places.Select(l=>l.Name+": "+l.BaseQuantity));}}
        product.SelectionChanged+=async(_,_)=>await Run(async()=>{if(product.SelectedItem is Product p){var current=await products.GetCurrentForRegisterAsync(p.Id);batch.ItemsSource=current?.Batches;batch.SelectedIndex=-1;await ShowStock();}});
        batch.SelectionChanged+=async(_,_)=>await Run(ShowStock);
        movement.Add("",BusinessForm.Button("Pharmacy_MoveStock",async(_,_)=>await Run(async()=>{if(product.SelectedItem is not Product p||!int.TryParse(source.SelectedValue as string,out var from)||!int.TryParse(target.SelectedValue as string,out var to)||!long.TryParse(count.Text,out var qty))throw new DomainException("Pharmacy_InvalidProduct");await service.TransferAsync(p.Id,(batch.SelectedItem as InventoryBatch)?.Id,from,to,qty,reason.Text,confirmation);confirmation=Guid.NewGuid();count.Text="";reason.Text="";await ShowStock();})),true);
        labels.Add("Pharmacy_LabelScope",labelScope);labels.Add("Pharmacy_LabelFormat",labelFormat);labels.Add("Pharmacy_SelectedLabels",labelSelection,true);
        labels.Add("",BusinessForm.Button("Pharmacy_PrintLocationLabels",async(_,_)=>await Run(async()=>
        {
            var all=await service.LocationsAsync();var selected=location.SelectedItem as StockLocation;var rack=selected?.ParentId??selected?.Id;
            var selectedLabels=(labelScope.SelectedValue as string) switch{"selected"=>labelSelection.SelectedItems.Cast<StockLocation>().ToArray(),"rack"=>all.Where(l=>l.Id==rack||l.ParentId==rack).ToArray(),_=>all};
            if(selectedLabels.Length==0)throw new DomainException("Pharmacy_SelectStockLocation");if(storage.ChoosePdfPath("Rack and shelf labels.pdf") is{} path)await System.IO.File.WriteAllBytesAsync(path,new LocationLabelDocument(selectedLabels,LocalizationManager.Instance.CurrentLanguage,labelFormat.SelectedIndex==1,all).GeneratePdf());
        })),true);
        var page=new ContentControl{Content=locations};var tabs=new UniformGrid{Columns=3,Margin=new Thickness(0,0,0,12)};var buttons=new List<Button>();
        foreach(var (key,form) in new[]{("Pharmacy_Locations",locations),("Pharmacy_MoveStock",movement),("Pharmacy_PrintLocationLabels",labels)})
        {var button=BusinessForm.Button(key,(_,_)=>{},"GhostButton");button.Margin=new Thickness(4);button.Click+=(_,_)=>{page.Content=form;error.Text="";foreach(var b in buttons)b.SetResourceReference(StyleProperty,b==button?"PrimaryButton":"GhostButton");};buttons.Add(button);tabs.Children.Add(button);}buttons[0].SetResourceReference(StyleProperty,"PrimaryButton");
        var root=new DockPanel();DockPanel.SetDock(tabs,Dock.Top);root.Children.Add(tabs);var footer=new StackPanel();footer.Children.Add(error);footer.Children.Add(BusinessForm.Button("Common_Close",(_,_)=>close(),"GhostButton"));DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);root.Children.Add(page);Content=root;Loaded+=async(_,_)=>await Run(Reload);
    }
    private static string T(string key)=>LocalizationManager.Instance[key];
}
