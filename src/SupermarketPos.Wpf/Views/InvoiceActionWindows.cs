using System.Windows;
using System.Windows.Controls;
using SupermarketPos.Domain.Common;
using SupermarketPos.Wpf.Localization;
namespace SupermarketPos.Wpf.Views;
public sealed class InvoiceNumberWindow:Window
{
    public string Phone{get;private set;}="";public bool SaveNumber{get;private set;}
    public InvoiceNumberWindow(string? number,bool canSave)
    {
        Title=LocalizationManager.Instance["Invoice_AddNumber"];SetResourceReference(StyleProperty,"FormDialogWindow");WindowStartupLocation=WindowStartupLocation.CenterOwner;FlowDirection=LocalizationManager.Instance.FlowDirection;
        var f=new BusinessForm();var phone=new TextBox{Text=number??"",MaxLength=60,FlowDirection=FlowDirection.LeftToRight};var country=new TextBox{Text="92",MaxLength=4,FlowDirection=FlowDirection.LeftToRight};f.Add("Customer_Phone",phone);f.Add("Invoice_CountryCode",country);
        var save=new CheckBox{Content=BusinessForm.Label("Invoice_SaveNumber"),Visibility=canSave?Visibility.Visible:Visibility.Collapsed};f.Add("",save,true);var error=new TextBlock{TextWrapping=TextWrapping.Wrap};error.SetResourceReference(TextBlock.ForegroundProperty,"DangerBrush");f.Add("",error,true);
        f.Add("",BusinessForm.Button("Common_Cancel",(_,_)=>DialogResult=false,"GhostButton"));f.Add("",BusinessForm.Button("Common_Confirm",(_,_)=>{try{Phone=InvoicePhone.Normalize(phone.Text,country.Text.Trim());SaveNumber=save.IsChecked==true;DialogResult=true;}catch(DomainException ex){error.Text=LocalizationManager.Instance[ex.Message];}}));
        var card=new Border{Width=650,Child=f};card.SetResourceReference(StyleProperty,"Card");var fit=new Viewbox{Child=card};fit.SetResourceReference(StyleProperty,"DialogFit");Content=fit;
    }
}
public sealed class InvoicePrinterWindow:Window
{
    public string? SelectedPrinter{get;private set;}
    public InvoicePrinterWindow(string[] printers,string current)
    {
        Title=LocalizationManager.Instance["Invoice_PrinterSettings"];SetResourceReference(StyleProperty,"FormDialogWindow");WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var f=new BusinessForm();var selection=new ComboBox{ItemsSource=printers,SelectedItem=printers.FirstOrDefault(p=>p==current)};f.Add("Setup_Printer",selection,true);var error=new TextBlock{TextWrapping=TextWrapping.Wrap};f.Add("",error,true);
        f.Add("",BusinessForm.Button("Common_Cancel",(_,_)=>DialogResult=false,"GhostButton"));f.Add("",BusinessForm.Button("Profile_Save",(_,_)=>{if(selection.SelectedItem is string printer){SelectedPrinter=printer;DialogResult=true;}else error.Text=LocalizationManager.Instance["Invoice_PrinterMissing"];}));
        var card=new Border{Width=650,Child=f};card.SetResourceReference(StyleProperty,"Card");var fit=new Viewbox{Child=card};fit.SetResourceReference(StyleProperty,"DialogFit");Content=fit;
    }
}
