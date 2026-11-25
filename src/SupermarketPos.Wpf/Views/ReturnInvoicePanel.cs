using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;

namespace SupermarketPos.Wpf.Views;

internal static class ReturnInvoicePanel
{
    public static async Task ShowAsync(ReturnsService service,Sale sale)
    {
        if(InAppDialog.IsOpen) return;
        var history=await service.HistoryAsync(sale.Id);
        var prior=history.Where(r=>r.Status=="Posted").SelectMany(ReturnsService.Items).ToArray();
        var pending=history.FirstOrDefault(r=>r.Status=="Pending");
        var panel=new DockPanel();
        var header=BusinessForm.Label("Trade_ReturnItems");
        header.Text=LocalizationManager.Instance["Trade_ReturnItems"]+" · "+sale.ReceiptNo;
        DockPanel.SetDock(header,Dock.Top);panel.Children.Add(header);
        var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);panel.Children.Add(footer);
        var form=new BusinessForm();
        var body=new ScrollViewer {Content=form,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=465};
        panel.Children.Add(body);

        Action close=()=>{};
        var error=new TextBlock{TextWrapping=TextWrapping.Wrap};
        error.SetResourceReference(TextBlock.ForegroundProperty,"DangerBrush");
        var choices=new List<(SaleLine Line, TextBox Quantity, CheckBox Restock, TextBox Identity)>();
        var confirmation=Guid.NewGuid();
        var reason=new TextBox{MaxLength=300};
        var external=new TextBox{MaxLength=150};
        var cash=new CheckBox{Content=BusinessForm.Label("Trade_ConfirmCashRefund"),Visibility=sale.PaymentMethod=="CASH"?Visibility.Visible:Visibility.Collapsed};
        var summary=new TextBlock{TextWrapping=TextWrapping.Wrap};
        async Task Run(Func<Task> action)
        {
            try { panel.IsEnabled=false; error.Text="";await action();close(); }
            catch(DomainException ex){error.Text=LocalizationManager.Instance[ex.Message];}
            catch(Exception){error.Text=LocalizationManager.Instance["Trade_ReturnInvalid"];}
            finally{panel.IsEnabled=true;}
        }
        foreach(var record in history)
            form.Add("",new TextBlock{Text=$"{record.CreatedAt:g} · {LocalizationManager.Instance["Trade_Return"+record.Status]} · {MoneyText.Format(record.Gross)} · {record.Reason}",TextWrapping=TextWrapping.Wrap},true);
        if(pending is not null)
        {
            form.Add("Trade_RefundReference",external,true);
            footer.Children.Add(BusinessForm.Button("Trade_ConfirmCardRefund",async(_,_)=>await Run(()=>service.ResolveCardAsync(pending.Id,true,external.Text))));
            footer.Children.Add(BusinessForm.Button("Trade_CardRefundFailed",async(_,_)=>await Run(()=>service.ResolveCardAsync(pending.Id,false,external.Text)),"GhostButton"));
        }
        else
        {
            foreach(var line in sale.Lines)
            {
                var remaining=line.Quantity-prior.Where(r=>r.SaleLineId==line.Id).Sum(r=>r.Quantity);
                if(remaining<=0)continue;
                var quantity=new TextBox{FlowDirection=FlowDirection.LeftToRight};
                var restock=new CheckBox{Content=BusinessForm.Label("Trade_RestockVerified"),IsEnabled=line.BatchId is null&&line.SerialNumber is null&&line.StockLocation is null&&sale.PaymentMethod!="CARD"};
                form.Add("",new TextBlock{Text=$"{line.Name} · {remaining:0.###} · {MoneyText.Format(line.UnitPrice)}",TextWrapping=TextWrapping.Wrap},true);
                var identity=new TextBox{MaxLength=64};
                if(line.BatchLot is not null||line.SerialNumber is not null||line.Imei1 is not null||line.Imei2 is not null)form.Add("Trade_ReturnIdentityField",identity,true);
                form.Add("Pos_Qty",quantity);form.Add("",restock);choices.Add((line,quantity,restock,identity));
                quantity.TextChanged+=(_,_)=>UpdateTotal();
            }
            form.Add("Trade_ReturnReason",reason,true);
            form.Add("",cash,true);
            form.Add("",summary,true);
            if(choices.Count>0)
                footer.Children.Add(BusinessForm.Button(sale.PaymentMethod=="CARD"?"Trade_RequestCardRefund":"Trade_ReturnItems",async(_,_)=>await Run(async()=>
                {
                    await service.RecordAsync(sale.Id,ReadSelection(),reason.Text,cash.IsChecked==true,confirmation);
                })));
        }
        footer.Children.Add(error);
        footer.Children.Add(BusinessForm.Button("Common_Close",(_,_)=>close(),"GhostButton"));
        await InAppDialog.ShowAsync(panel,callback=>close=callback,contentWidth:900);

        ReturnSelection[] ReadSelection() => choices.Where(c=>!string.IsNullOrWhiteSpace(c.Quantity.Text)).Select(c=>
        {
            if(!decimal.TryParse(c.Quantity.Text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var value)||value<0)
                throw new DomainException("Trade_ReturnInvalid");
            return new ReturnSelection(c.Line.Id,value,c.Restock.IsChecked==true,c.Identity.Text);
        }).Where(l=>l.Quantity>0).ToArray();

        void UpdateTotal()
        {
            try
            {
                decimal total=0;
                foreach(var request in ReadSelection())
                {
                    var line=sale.Lines.Single(l=>l.Id==request.SaleLineId);
                    var already=prior.Where(r=>r.SaleLineId==line.Id).Sum(r=>r.Quantity);
                    if(request.Quantity+already>line.Quantity)throw new DomainException("Trade_ReturnInvalid");
                    total+=Money.Round2(line.LineGross*(already+request.Quantity)/line.Quantity)-Money.Round2(line.LineGross*already/line.Quantity);
                }
                summary.Text=LocalizationManager.Instance["Pos_Total"]+" · "+MoneyText.Format(total);
            }
            catch(Exception ex) when(ex is DomainException or OverflowException)
            { summary.Text=LocalizationManager.Instance["Trade_ReturnInvalid"]; }
        }
    }
}
