using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Wpf.Common;

namespace SupermarketPos.Wpf.Views;

public sealed partial class DigitalServicesView
{
    private async Task CheckoutCartAsync(string paymentMethod)
    {
        if(_cart.Count==0)return;
        _cartTable?.CommitEdit(DataGridEditingUnit.Cell,true);
        foreach(var row in _cart)row.ValidateFields();
        if(_cart.FirstOrDefault(r=>r.Errors().Count>0) is{} invalid){_status.Text=invalid.Error;FocusRow(invalid,invalid.Errors().First().Key);return;}
        var total=_cart.Sum(r=>r.Total);if(paymentMethod=="CARD"&&_cart.Any(r=>r.Type=="CASHOUT"))throw new DomainException("Digital_InvalidPayment");
        var panel=new StackPanel();var heading=BusinessForm.Label("Pos_Pay");heading.SetResourceReference(StyleProperty,"H1");panel.Children.Add(heading);
        panel.Children.Add(BusinessForm.Label(total<0?"Digital_ToPay":"Digital_ToCollect"));var money=new TextBlock{Text=Money.Format(Math.Abs(total))};money.SetResourceReference(StyleProperty,"BigMoney");panel.Children.Add(new Viewbox{Child=money,Stretch=System.Windows.Media.Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,HorizontalAlignment=HorizontalAlignment.Left});
        var form=new BusinessForm();var tendered=new TextBox{Name="DigitalTendered",Text=Math.Max(0,total).ToString("0.00",CultureInfo.InvariantCulture),FlowDirection=FlowDirection.LeftToRight,MaxLength=40};
        if(paymentMethod=="CASH"&&total>0)form.Field("Pos_Tendered","tendered",tendered,true);else form.Add("Reports_Payment",BusinessForm.Label("Payment_"+(paymentMethod=="CARD"?"Card":"Cash")),true);
        var change=new TextBlock();if(paymentMethod=="CASH"&&total>0){form.Add("Pos_Change",change,true);void UpdateChange(){change.Text=DigitalCartLine.Number(tendered.Text,out var paid)&&paid>=total?Money.Format(paid-total):"—";}tendered.TextChanged+=(_,_)=>UpdateChange();UpdateChange();}
        var error=new TextBlock{TextWrapping=TextWrapping.Wrap};error.SetResourceReference(TextBlock.ForegroundProperty,"DangerBrush");form.Add("",error,true);panel.Children.Add(form);
        var posting=false;Action close=()=>{};ServiceCheckout? completed=null;
        var actions=new WrapPanel();var cancel=BusinessForm.Button("Common_Cancel",(_,_)=>close(),"GhostButton");actions.Children.Add(cancel);
        var finish=BusinessForm.Button("Pos_Pay",async(_,_)=>
        {
            if(posting)return;
            if(paymentMethod=="CASH"&&total>0&&(!DigitalCartLine.Number(tendered.Text,out var amount)||amount<total)){form.ShowErrors(new Dictionary<string,string>{{"tendered","Digital_InvalidTendered"}},true);return;}
            posting=true;cancel.IsEnabled=false;try
            {
                finishEnabled(false);
                foreach(var row in _cart.Where(r=>r.Package is null&&r.ReferencePackage is not null))row.Package=await (_retailInventory??throw new DomainException("Service_Invalid")).PrepareReferencePackageAsync(row.ReferencePackage!,decimal.Parse(row.AmountText,NumberStyles.Number,CultureInfo.InvariantCulture),row.AccountType);
                var quotes=_cart.Select(r=>r.Quote()).ToArray();var paid=paymentMethod=="CARD"?total:total>0?decimal.Parse(tendered.Text,NumberStyles.Number,CultureInfo.InvariantCulture):0;
                completed=await _ledger.CheckoutAsync(_checkoutKey,quotes,paymentMethod,paid);posting=false;close();
            }
            catch(Exception e){error.Text=T(e is DomainException?e.Message:"Service_Error");}
            finally{posting=false;cancel.IsEnabled=true;finishEnabled(true);}
        },"SuccessButton");
        void finishEnabled(bool enabled){foreach(var button in actions.Children.OfType<Button>())if(!ReferenceEquals(button,cancel))button.IsEnabled=enabled;}
        finish.Name="CompleteDigitalPayment";actions.Children.Add(finish);panel.Children.Add(actions);
        panel.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter&&!posting){e.Handled=true;finish.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}};
        await InAppDialog.ShowAsync(panel,c=>close=c,contentWidth:640,initialFocus:paymentMethod=="CASH"&&total>0?tendered:finish,canClose:()=>!posting);
        if(completed is null)return;
        ClearCompletedDraft(completed.ConfirmationId);foreach(var row in _cart)row.PropertyChanged-=RowChanged;_cart.Clear();_checkoutKey=Guid.NewGuid();_entryDirty=false;_entry.Reset();_status.Text=T("Service_Saved")+" · "+completed.ReceiptNumber;Record();
        await _receipts.ShowCheckoutAsync(completed);_ = Dispatcher.BeginInvoke(()=>FocusSearch());
    }
}
