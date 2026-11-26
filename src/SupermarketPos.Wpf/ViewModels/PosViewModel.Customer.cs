using CommunityToolkit.Mvvm.Input;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Views;

namespace SupermarketPos.Wpf.ViewModels;

public sealed partial class PosViewModel
{
    private readonly SaleHistory _customerHistory;
    public event Action? CustomerSelectionClosed;
    public string CartCustomerCaption => (CustomerSummary ?? Text["Customer_WalkIn"]) + " · " + Text["Customer_Change"];
    public string? CustomerSummary => Customer.IncludeDetails && !string.IsNullOrWhiteSpace(Customer.Name)
        ? Customer.Name + " · " + MoneyText.Format(Customer.Outstanding) : null;
    internal bool HasCustomerAccount => Sale.CustomerIdentity(Customer.Build()) is not null;
    public string? CreditBalanceSummary =>
        Sale.CustomerIdentity(Customer.Build()) is null || !TryParse(CreditPaidNow, out var paid) || paid < 0 || paid > CurrentGross
            ? null
            : Text.Format("Trade_CreditBalanceSummary", MoneyText.Format(Customer.Outstanding),
                MoneyText.Format(Customer.Outstanding + CurrentGross - paid));

    private void RefreshCustomerSummary()
    {
        OnPropertyChanged(nameof(CustomerSummary));
        OnPropertyChanged(nameof(CartCustomerCaption));
        OnPropertyChanged(nameof(CreditBalanceSummary));
    }

    [RelayCommand]
    private async Task SelectCustomerAsync()
    {
        if (InAppDialog.IsOpen || IsPaymentOpen || _checkoutBusy) return;
        var fields=new CustomerFieldsViewModel { ExistingCustomers=Customer.ExistingCustomers };
        fields.Load(Customer.Build());
        fields.IncludeDetails=true;
        var form=new DockPanel();
        Action close=()=>{};
        var accepted=false;
        var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right, Margin=new Thickness(0,12,0,0) };
        actions.Children.Add(BusinessForm.Button("Customer_ClearSelection",(_,_)=>{fields.Load(null);accepted=true;close();},"GhostButton"));
        actions.Children.Add(BusinessForm.Button("Customer_New",(_,_)=>{fields.Load(null);fields.IncludeDetails=true;},"GhostButton"));
        actions.Children.Add(BusinessForm.Button("Common_Cancel",(_,_)=>close(),"GhostButton"));
        actions.Children.Add(BusinessForm.Button("Common_Save",(_,_)=>{accepted=true;close();}));
        DockPanel.SetDock(actions,Dock.Bottom);
        form.Children.Add(actions);
        var view=new CustomerFieldsView { DataContext=fields, ShowOptionalToggle=false };
        actions.Children.OfType<Button>().Last().IsDefault=true;
        form.Children.Add(new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=view });
        await InAppDialog.ShowAsync(form,callback=>close=callback,contentWidth:620,sizeToContent:true,initialFocus:view.ExistingCustomerPicker);
        if(accepted)Customer.Load(fields.Build());
        CustomerSelectionClosed?.Invoke();
    }

    [RelayCommand]
    private async Task PreviousPurchasesAsync(bool? frequent)
    {
        if(InAppDialog.IsOpen || IsPaymentOpen)return;
        if(!HasCustomerAccount || Customer.Build() is not { } customer)
        {
            Status=Text["Shortcut_CustomerAccountRequired"];
            return;
        }
        try
        {
            var invoices=await _customerHistory.ForCustomerAsync(customer);
            var panel=new DockPanel();
            Action close=()=>{};
            if(frequent==true)
            {
                var title=BusinessForm.Label("Trade_Frequent");
                DockPanel.SetDock(title,Dock.Top);
                panel.Children.Add(title);
                var top=invoices.SelectMany(s=>s.Lines.Select(l=>new { Sale=s.Id, s.CreatedAt, Line=l }))
                    .GroupBy(l=>l.Line.ProductId)
                    .OrderByDescending(g=>g.Select(l=>l.Sale).Distinct().Count()).ThenByDescending(g=>g.Max(l=>l.CreatedAt))
                    .ThenByDescending(g=>g.Sum(l=>l.Line.BaseQuantity)).Take(20);
                var choices=HistoryChoices();
                var options=top.Join(_registerProducts, group=>group.Key, product=>product.Id, (group,product)=>
                {
                    var tile=new RegisterProductTile(product,0,_registerMetadata.GetValueOrDefault(product.Id),IsPharmacy);
                    return new FrequentOption(product, Text.Format("Trade_FrequentLine", product.Name, tile.Details,
                        group.Select(row=>row.Sale).Distinct().Count(), group.Max(row=>row.CreatedAt).ToString("d"),
                        tile.PriceText, tile.StockText));
                }).ToArray();
                choices.ItemsSource=options;
                if(options.Length>0)choices.SelectedIndex=0;
                Product? chosen=null;
                var add=BusinessForm.Button("Common_Add",(_,_)=>{chosen=(choices.SelectedItem as FrequentOption)?.Product;if(chosen is not null)close();});
                add.IsEnabled=options.Length>0;
                panel.Children.Add(HistoryActions(add, ()=>close()));
                panel.Children.Add(options.Length>0 ? choices : BusinessForm.Label("Trade_NoFrequentProducts"));
                choices.PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter&&add.IsEnabled){e.Handled=true;add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}};
                await InAppDialog.ShowAsync(panel,callback=>close=callback,contentWidth:620,sizeToContent:true,initialFocus:choices);
                if(chosen is not null)await AddFromResultsAsync(chosen);
                return;
            }
            var list=HistoryChoices();
            list.ItemsSource=invoices.Select(s=>new InvoiceOption(s)).ToArray();
            if(invoices.Count>0)list.SelectedIndex=0;
            var historyTitle=BusinessForm.Label("Trade_PreviousPurchases");
            DockPanel.SetDock(historyTitle,Dock.Top);
            panel.Children.Add(historyTitle);
            Sale? repeat=null;
            var repeatButton=BusinessForm.Button("Trade_RepeatSale",(_,_)=>{repeat=(list.SelectedItem as InvoiceOption)?.Sale;if(repeat is not null)close();});
            repeatButton.IsEnabled=invoices.Count>0;
            panel.Children.Add(HistoryActions(repeatButton, ()=>close()));
            panel.Children.Add(invoices.Count>0 ? list : BusinessForm.Label("Trade_NoCustomerInvoices"));
            await InAppDialog.ShowAsync(panel,callback=>close=callback,contentWidth:620,sizeToContent:true);
            if(repeat is not null)await RepeatAsync(repeat);
        }
        catch(Exception){Status=Text["Trade_RepeatError"];}
    }

    private static ListBox HistoryChoices()
    {
        var label=new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty,new Binding("Label"));
        label.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);
        var list=new ListBox { MaxHeight=300,HorizontalContentAlignment=HorizontalAlignment.Stretch,
            ItemTemplate=new DataTemplate { VisualTree=label } };
        ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
        VirtualizingPanel.SetVirtualizationMode(list,VirtualizationMode.Recycling);
        return list;
    }

    private static WrapPanel HistoryActions(Button primary, Action close)
    {
        var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right, Margin=new Thickness(0,10,0,0) };
        DockPanel.SetDock(actions,Dock.Bottom);
        actions.Children.Add(BusinessForm.Button("Common_Close",(_,_)=>close(),"GhostButton"));
        actions.Children.Add(primary);
        return actions;
    }

    [RelayCommand]
    private async Task CustomerLedgerAsync()
    {
        if (Customer.Build() is not { } customer ||
            System.Windows.Application.Current.MainWindow?.DataContext is not MainViewModel shell) return;
        await shell.ShowCustomersCommand.ExecuteAsync(null);
        if(shell.CurrentViewModel is CustomersViewModel customers)
        {
            customers.Search="";
            customers.Selected=customers.Customers.FirstOrDefault(c=>c.Key==Sale.CustomerIdentity(customer));
            await customers.ViewLedgerCommand.ExecuteAsync(null);
        }
    }

    private sealed record InvoiceOption(Sale Sale)
    {
        public string Label => $"{Sale.ReceiptNo} · {Sale.CreatedAt:g} · {MoneyText.Format(Sale.GrossTotal)}";
    }

    private sealed record FrequentOption(Product Product, string Label);
}
