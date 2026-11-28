using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Wpf.Views;
using System.Windows;
using System.Windows.Controls;

namespace SupermarketPos.Wpf.ViewModels;

public sealed partial class CustomersViewModel
{
    private IReadOnlyList<DigitalCustomerSummary> _digitalAll=[];
    [ObservableProperty] private int _customerTab;
    [ObservableProperty] private bool _digitalAvailable;
    [ObservableProperty] private string _digitalSearch="";
    [ObservableProperty] private IReadOnlyList<DigitalCustomerSummary> _digitalCustomers=[];
    [ObservableProperty] private DigitalCustomerSummary? _selectedDigitalCustomer;
    [ObservableProperty] private IReadOnlyList<ServiceHistoryRow> _digitalHistory=[];
    [ObservableProperty] private ServiceHistoryRow? _selectedDigitalTransaction;
    private Task _digitalLoad=Task.CompletedTask;

    private async Task RefreshDigitalAsync()
    {
        _digitalAll=await digital.CustomersAsync();
        DigitalAvailable=(await setup.LoadAsync()).Profile.DigitalServicesAvailable||_digitalAll.Count>0;
        var previous=SelectedDigitalCustomer;
        FilterDigital();
        if(Equals(previous,SelectedDigitalCustomer))_digitalLoad=LoadDigitalHistoryAsync();
        await _digitalLoad;
    }
    partial void OnDigitalSearchChanged(string value)=>FilterDigital();
    private void FilterDigital()
    {
        var key=SelectedDigitalCustomer?.Key;
        DigitalCustomers=_digitalAll.Where(c=>c.Matches(DigitalSearch.Trim())).ToArray();
        SelectedDigitalCustomer=DigitalCustomers.FirstOrDefault(c=>c.Key==key)??DigitalCustomers.FirstOrDefault();
    }
    partial void OnSelectedDigitalCustomerChanged(DigitalCustomerSummary? value)
    {
        DigitalHistory=[];SelectedDigitalTransaction=null;
        _digitalLoad=LoadDigitalHistoryAsync();
        EditDigitalNameCommand.NotifyCanExecuteChanged();
    }
    internal async Task LoadDigitalHistoryAsync()
    {
        var selected=SelectedDigitalCustomer;
        if(selected is null)return;
        try
        {
            var transactions=await digital.CustomerHistoryAsync(selected.Key);
            var posts=(await digital.PostingsAsync(selected.Key)).ToDictionary(p=>p.TransactionId);
            if(SelectedDigitalCustomer?.Key!=selected.Key)return;
            DigitalHistory=transactions.Select(t=>new ServiceHistoryRow(t,posts.GetValueOrDefault(t.Id))).ToArray();
            SelectedDigitalTransaction=DigitalHistory.FirstOrDefault();
        }
        catch(Exception){Error=LocalizationManager.Instance["Customers_LoadError"];}
    }
    partial void OnSelectedDigitalTransactionChanged(ServiceHistoryRow? value)=>OpenDigitalReceiptCommand.NotifyCanExecuteChanged();
    private bool HasDigitalCustomer()=>SelectedDigitalCustomer is not null;
    private bool HasDigitalReceipt()=>SelectedDigitalTransaction?.Posting is not null;

    [RelayCommand(CanExecute=nameof(HasDigitalReceipt))]
    private async Task OpenDigitalReceiptAsync()
    {
        if(SelectedDigitalTransaction is not {Posting:not null} row||InAppDialog.IsOpen)return;
        try{await receipts.ShowServiceAsync(row.Transaction,row.Posting,(await setup.LoadAsync()).Profile);}
        catch(Exception){Error=LocalizationManager.Instance["Customers_LoadError"];}
    }

    [RelayCommand(CanExecute=nameof(HasDigitalCustomer))]
    private async Task EditDigitalNameAsync()
    {
        if(SelectedDigitalCustomer is not{} customer||InAppDialog.IsOpen)return;
        var form=new BusinessForm(1);
        var name=new TextBox{Text=customer.Name,MaxLength=150};
        form.Field("Digital_CustomerNameOptional","Name",name);
        var error=new TextBlock{TextWrapping=TextWrapping.Wrap};error.SetResourceReference(TextBlock.ForegroundProperty,"DangerBrush");form.Add("",error,true);
        Action close=()=>{};var saved=false;var busy=false;
        var actions=new WrapPanel();
        var save=BusinessForm.Button("Profile_Save",async(sender,_)=>{
            if(busy)return;busy=true;((Button)sender).IsEnabled=false;
            try{await digital.SaveCustomerNameAsync(customer.Key,name.Text);saved=true;busy=false;close();}
            catch(SupermarketPos.Domain.Common.DomainException e){form.ShowErrors(new Dictionary<string,string>{{"Name",e.Message}},true);}
            catch(Exception){error.Text=LocalizationManager.Instance["Customers_LoadError"];}
            finally{busy=false;((Button)sender).IsEnabled=true;}
        });save.IsDefault=true;actions.Children.Add(save);actions.Children.Add(BusinessForm.Button("Common_Cancel",(_,_)=>close(),"GhostButton"));form.Add("",actions,true);
        await InAppDialog.ShowAsync(form,c=>close=c,contentWidth:540,initialFocus:name,canClose:()=>!busy);
        if(saved)await RefreshDigitalAsync();
    }
}
