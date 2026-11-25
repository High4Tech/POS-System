using CommunityToolkit.Mvvm.Input;
using System.Windows;
using System.Windows.Controls;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Views;

namespace SupermarketPos.Wpf.ViewModels;

public sealed partial class PosViewModel
{
    private readonly SaleHistory _customerHistory;
    public string? CustomerSummary => Customer.IncludeDetails && !string.IsNullOrWhiteSpace(Customer.Name)
        ? Customer.Name + " · " + MoneyText.Format(Customer.Outstanding) : null;
    public string? CreditBalanceSummary =>
        Sale.CustomerIdentity(Customer.Build()) is null || !TryParse(CreditPaidNow, out var paid) || paid < 0 || paid > CurrentGross
            ? null
            : Text.Format("Trade_CreditBalanceSummary", MoneyText.Format(Customer.Outstanding),
                MoneyText.Format(Customer.Outstanding + CurrentGross - paid));

    private void RefreshCustomerSummary()
    {
        OnPropertyChanged(nameof(CustomerSummary));
        OnPropertyChanged(nameof(CreditBalanceSummary));
    }

    [RelayCommand]
    private async Task SelectCustomerAsync()
    {
        if (InAppDialog.IsOpen || IsPaymentOpen || _checkoutBusy) return;
        var fields=new CustomerFieldsViewModel { ExistingCustomers=Customer.ExistingCustomers };
        fields.Load(Customer.Build());
        fields.IncludeDetails=true;
        var form=new StackPanel();
        form.Children.Add(new CustomerFieldsView { DataContext=fields });
        Action close=()=>{};
        var accepted=false;
        form.Children.Add(BusinessForm.Button("Common_Save",(_,_)=>{accepted=true;close();}));
        form.Children.Add(BusinessForm.Button("Common_Cancel",(_,_)=>close(),"GhostButton"));
        await InAppDialog.ShowAsync(form,callback=>close=callback,contentWidth:620);
        if(!accepted)return;
        Customer.Load(fields.Build());
    }

    [RelayCommand]
    private async Task PreviousPurchasesAsync(bool? frequent)
    {
        if(InAppDialog.IsOpen || IsPaymentOpen || Customer.Build() is not { } customer)return;
        try
        {
            var invoices=await _customerHistory.ForCustomerAsync(customer);
            var panel=new StackPanel();
            Action close=()=>{};
            if(frequent==true)
            {
                var top=invoices.SelectMany(s=>s.Lines.Select(l=>new { Sale=s.Id, s.CreatedAt, Line=l }))
                    .GroupBy(l=>l.Line.ProductId)
                    .OrderByDescending(g=>g.Select(l=>l.Sale).Distinct().Count()).ThenByDescending(g=>g.Max(l=>l.CreatedAt))
                    .ThenByDescending(g=>g.Sum(l=>l.Line.BaseQuantity)).Take(20);
                var choices=new ListBox{MaxHeight=350,DisplayMemberPath="Label"};
                choices.ItemsSource=top.Join(_registerProducts, group=>group.Key, product=>product.Id, (group,product)=>
                {
                    var tile=new RegisterProductTile(product,0,_registerMetadata.GetValueOrDefault(product.Id),IsPharmacy);
                    return new FrequentOption(product, Text.Format("Trade_FrequentLine", product.Name, tile.Details,
                        group.Select(row=>row.Sale).Distinct().Count(), group.Max(row=>row.CreatedAt).ToString("d"),
                        tile.PriceText, tile.StockText));
                }).ToArray();
                panel.Children.Add(BusinessForm.Label("Trade_Frequent"));
                panel.Children.Add(choices);
                Product? chosen=null;
                panel.Children.Add(BusinessForm.Button("Common_Add",(_,_)=>{chosen=(choices.SelectedItem as FrequentOption)?.Product;if(chosen is not null)close();}));
                panel.Children.Add(BusinessForm.Button("Common_Close",(_,_)=>close(),"GhostButton"));
                await InAppDialog.ShowAsync(panel,callback=>close=callback,contentWidth:620);
                if(chosen is not null)await AddFromResultsAsync(chosen);
                return;
            }
            var list=new ListBox{MaxHeight=350,DisplayMemberPath="Label",ItemsSource=invoices.Select(s=>new InvoiceOption(s)).ToArray()};
            panel.Children.Add(BusinessForm.Label("Trade_PreviousPurchases"));
            panel.Children.Add(list);
            Sale? repeat=null;
            panel.Children.Add(BusinessForm.Button("Trade_RepeatSale",(_,_)=>{repeat=(list.SelectedItem as InvoiceOption)?.Sale;if(repeat is not null)close();}));
            panel.Children.Add(BusinessForm.Button("Common_Close",(_,_)=>close(),"GhostButton"));
            await InAppDialog.ShowAsync(panel,callback=>close=callback,contentWidth:620);
            if(repeat is not null)await RepeatAsync(repeat);
        }
        catch(Exception){Status=Text["Trade_RepeatError"];}
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
