using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Common;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Wpf.ViewModels;

namespace SupermarketPos.Wpf.Views;

internal static class CustomerCreditPanel
{
    public static async Task ShowAsync(ReturnsService service,IAuthService auth,CustomerRow customer)
    {
        var ids=customer.Sales.Select(s=>s.Sale.Id).ToArray();var credits=await service.AvailableCreditsAsync(ids);
        var history=await service.CreditMovementsAsync(ids);var panel=new StackPanel();var form=new BusinessForm();
        panel.Children.Add(BusinessForm.Label("Credit_Manage"));
        var pick=new ComboBox{ItemsSource=credits.Select(c=>new CreditChoice(c,$"{c.Receipt} · {MoneyText.Format(c.Available)}")).ToArray(),DisplayMemberPath="Label",SelectedIndex=0};
        var targets=customer.Sales.Where(r=>r.Sale.PaymentMethod=="CREDIT"&&r.Sale.Outstanding>0).ToArray();
        var target=new ComboBox{ItemsSource=targets,DisplayMemberPath="ReceiptNo",SelectedIndex=0};
        var choices=new[]{new BusinessFormChoice("APPLY",LocalizationManager.Instance["Credit_Apply"]),new("CASH",LocalizationManager.Instance["Credit_RefundCash"]),new("CARD",LocalizationManager.Instance["Credit_RefundCard"])};
        var mode=new ComboBox{ItemsSource=choices,DisplayMemberPath="Name",SelectedValuePath="Code",SelectedValue=targets.Length>0?"APPLY":"CASH"};
        var amount=new TextBox{FlowDirection=FlowDirection.LeftToRight,MaxLength=40};var reference=new TextBox{MaxLength=150};
        form.Field("Credit_Available","credit",pick,true);form.Field("Credit_Action","action",mode,true);form.Field("Reports_ReceiptNo","target",target,true);form.Field("Measure_Amount","amount",amount);form.Field("Trade_Reference","reference",reference);
        form.Add("",BusinessForm.Label("Credit_RefundHelp"),true);panel.Children.Add(form);
        if(credits.Count==0){form.Visibility=Visibility.Collapsed;panel.Children.Add(BusinessForm.Label("Credit_None"));}
        var error=new TextBlock{TextWrapping=TextWrapping.Wrap};error.SetResourceReference(TextBlock.ForegroundProperty,"DangerBrush");
        Action close=()=>{};var saving=false;var confirmation=Guid.NewGuid();var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Left};
        var submit=BusinessForm.Button("Credit_Apply",async(_,_)=>
        {
            if(saving)return;
            if(pick.SelectedItem is not CreditChoice source){error.Text=LocalizationManager.Instance["Credit_Insufficient"];return;}
            if(!DigitalCartLine.Number(amount.Text,out var value)||value<=0){form.ShowErrors(new Dictionary<string,string>{{"amount","Service_InvalidAmount"}},true);return;}
            var action=(string?)mode.SelectedValue??"";
            if(action=="APPLY"&&target.SelectedItem is not SaleRowViewModel){form.ShowErrors(new Dictionary<string,string>{{"target","Validation_Required"}},true);return;}
            saving=true;buttons.IsEnabled=false;
            try{await service.UseCreditAsync(source.Credit.Return.Id,action=="APPLY"?(target.SelectedItem as SaleRowViewModel)?.Sale.Id:null,value,action,reference.Text,confirmation,refundConfirmed:action!="APPLY");saving=false;close();}
            catch(DomainException e){error.Text=LocalizationManager.Instance[e.Message];}
            catch(Exception){error.Text=LocalizationManager.Instance["Credit_Invalid"];}
            finally{saving=false;buttons.IsEnabled=true;}
        });
        submit.IsDefault=true;submit.Visibility=credits.Count==0?Visibility.Collapsed:Visibility.Visible;buttons.Children.Add(submit);buttons.Children.Add(BusinessForm.Button("Common_Close",(_,_)=>close(),"GhostButton"));
        void Update(){target.IsEnabled=(string?)mode.SelectedValue=="APPLY";submit.Content=LocalizationManager.Instance[(string?)mode.SelectedValue=="APPLY"?"Credit_Apply":"Credit_RecordRefund"];submit.IsEnabled=credits.Count>0&&auth.CurrentUser?.IsAdmin==true;if(pick.SelectedItem is CreditChoice selected)amount.Text=selected.Credit.Available.ToString("0.00",CultureInfo.InvariantCulture);}
        pick.SelectionChanged+=(_,_)=>Update();mode.SelectionChanged+=(_,_)=>Update();Update();
        if(history.Count>0)
        {
            var table=new DataGrid{ItemsSource=history.Select(h=>new{Date=h.RecordedAt.ToString("g"),Action=LocalizationManager.Instance[h.Method=="APPLY"?"Credit_Apply":h.Method=="CARD"?"Credit_RefundCard":"Credit_RefundCash"],Amount=MoneyText.Format(h.Amount),Invoice=h.TargetSaleId is{} id?customer.Sales.FirstOrDefault(s=>s.Sale.Id==id)?.ReceiptNo:"",h.Operator,h.Reference}).ToArray(),AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,MaxHeight=230,Margin=new Thickness(0,12,0,0)};
            foreach(var column in new[]{("Reports_Date","Date"),("Credit_Action","Action"),("Measure_Amount","Amount"),("Reports_ReceiptNo","Invoice"),("Reports_Cashier","Operator"),("Trade_Reference","Reference")})table.Columns.Add(new DataGridTextColumn{Header=LocalizationManager.Instance[column.Item1],Binding=new System.Windows.Data.Binding(column.Item2),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
            panel.Children.Add(table);
        }
        var scroll=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        var root=new DockPanel();var footer=new StackPanel();footer.Children.Add(error);footer.Children.Add(buttons);DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);root.Children.Add(scroll);
        await InAppDialog.ShowAsync(root,c=>close=c,contentWidth:760,sizeToContent:true,canClose:()=>!saving);
    }
    private sealed record CreditChoice(AvailableCustomerCredit Credit,string Label);
}
