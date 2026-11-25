using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Wpf.Localization;
namespace SupermarketPos.Wpf.Views;
public sealed class PharmacyQuickAddPanel:UserControl
{
    public Gs1Barcode? ScannedTracking {get;private set;}
    public PharmacyQuickAddPanel(MedicineCatalogService catalog,PharmacyInventory inventory,string barcode,Action<Product> saved,Action<string,string,int?> full,Action close,string initialName="",int? initialReference=null,bool purchasing=false,ViewModels.ScannerController? scanner=null)
    {
        var search=new MedicineSearchBox(catalog);MedicineMatch? match=initialReference is null?null:new(initialReference,null,initialName,"","","","","","",null,null,"CatalogOnly");search.Input.Text=initialName;var code=new TextBox{Text=barcode,MaxLength=64,FlowDirection=FlowDirection.LeftToRight};var price=new TextBox{FlowDirection=FlowDirection.LeftToRight};var vat=new TextBox{FlowDirection=FlowDirection.LeftToRight};
        var unit=new ComboBox{ItemsSource=new[]{"piece","tablet","capsule","bottle","tube","sachet","vial","other"}.Select(u=>new BusinessForm.Choice(u,LocalizationManager.Instance["Measure_"+u])).ToArray(),DisplayMemberPath="Name",SelectedValuePath="Code",SelectedIndex=-1};
        var error=new TextBlock{TextWrapping=TextWrapping.Wrap};error.SetResourceReference(TextBlock.ForegroundProperty,"DangerBrush");var form=new BusinessForm();form.Add("Pharmacy_QuickAdd",search,true);form.Add("Products_Barcode",code);form.Add("Pharmacy_SellingUnit",unit);form.Add("Products_Price",price);form.Add("Products_Vat",vat);form.Add("",error,true);
        var openingVerified=new CheckBox{Content=BusinessForm.Label("Pharmacy_ZeroOpeningVerified"),Visibility=purchasing?Visibility.Visible:Visibility.Collapsed};form.Add("",openingVerified,true);
        search.Selected+=m=>{match=m;search.Input.Text=m.Name;};
        search.Input.TextChanged+=(_,_)=>{if(search.Input.Text!=match?.Name)match=null;};
        scanner?.AttachBarcodeField(code,(request,current)=>
        {
            if(!current())return Task.FromResult(new SupermarketPos.Infrastructure.Scanning.ScanOutcome("paused"));
            if(request.Barcode?.StartsWith("POSLOC:",StringComparison.OrdinalIgnoreCase)==true)return Task.FromResult(new SupermarketPos.Infrastructure.Scanning.ScanOutcome("invalid"));
            code.Text=request.Gs1?.Gtin??request.Barcode??"";
            if(request.Gs1?.HasTrackingData()==true){ScannedTracking=request.Gs1;full(search.Input.Text,code.Text,match?.ReferenceId);}
            return Task.FromResult(new SupermarketPos.Infrastructure.Scanning.ScanOutcome("captured"));
        });
        form.Add("",BusinessForm.Button("Pharmacy_FullDetails",(_,_)=>full(search.Input.Text,code.Text,match?.ReferenceId),"GhostButton"));
        var save=BusinessForm.Button("Common_Save",async(sender,_)=>
        {
            ((Button)sender).IsEnabled=false;
            try
            {
                decimal Parse(string s)=>decimal.TryParse(s,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var n)?n:throw new DomainException("Common_InvalidNumber");
                var product=await inventory.QuickAddAsync(new(match?.ReferenceId,search.Input.Text,code.Text,unit.SelectedValue as string??throw new DomainException("Pharmacy_InvalidUnits"),price.Text.Trim().Length==0?0:Parse(price.Text),Parse(vat.Text)/100,false));
                if(purchasing&&openingVerified.IsChecked==true&&!product.StockTracked)await inventory.VerifyStockAsync(product.Id,0,LocalizationManager.Instance["Pharmacy_ZeroOpeningVerified"]);
                saved(product);
            }
            catch(Exception ex){error.Text=LocalizationManager.Instance[ex is DomainException?ex.Message:"Pharmacy_SaveError"];}
            finally{((Button)sender).IsEnabled=true;}
        });form.Add("",save);form.Add("",BusinessForm.Button("Common_Close",(_,_)=>close(),"GhostButton"),true);Content=form;Loaded+=(_,_)=>search.Input.Focus();
    }
}
