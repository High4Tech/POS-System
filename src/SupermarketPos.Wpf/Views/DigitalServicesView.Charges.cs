using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Wpf.Common;
namespace SupermarketPos.Wpf.Views;
public sealed partial class DigitalServicesView
{
    private async Task ConfigureChargesAsync(ServiceProviderAccount provider)
    {
        var model=new ChargeEntry{Mode=provider.CustomerFeeMode,Value=(provider.CustomerFeeMode=="PERCENT"?provider.CustomerFeePercent:provider.DefaultCustomerFee).ToString(CultureInfo.InvariantCulture)};
        var panel=new StackPanel();panel.Children.Add(BusinessForm.Label("Digital_DefaultCharges"));var form=new BusinessForm();form.Select("Digital_ChargeMode",model,nameof(model.Mode),("FIXED","Digital_FixedCharge"),("PERCENT","Digital_PercentCharge"));var value=form.Text("Digital_ChargeValue",model,nameof(model.Value),true);panel.Children.Add(form);panel.Children.Add(BusinessForm.Label("Digital_ChargeHelp"));
        Action close=()=>{};var saved=false;var busy=false;var error=new TextBlock{TextWrapping=TextWrapping.Wrap};error.SetResourceReference(TextBlock.ForegroundProperty,"DangerBrush");panel.Children.Add(error);
        var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Left};var save=BusinessForm.Button("Profile_Save",async(sender,_)=>
        {
            if(busy)return;
            if(!decimal.TryParse(model.Value,NumberStyles.Number,CultureInfo.InvariantCulture,out var amount)||amount<0||amount!=decimal.Round(amount,model.Mode=="PERCENT"?4:2)||model.Mode=="PERCENT"&&amount>100){form.ShowErrors(new Dictionary<string,string>{{nameof(model.Value),"Service_InvalidAmount"}},true);return;}
            busy=true;((Button)sender).IsEnabled=false;
            try{var copy=JsonSerializer.Deserialize<ServiceProviderAccount>(JsonSerializer.Serialize(provider))!;copy.CustomerFeeMode=model.Mode;if(model.Mode=="PERCENT")copy.CustomerFeePercent=amount;else copy.DefaultCustomerFee=amount;await _ledger.SaveProviderAsync(copy);saved=true;busy=false;close();}
            catch(Exception e){error.Text=T(e is DomainException?e.Message:"Service_Error");}
            finally{busy=false;((Button)sender).IsEnabled=true;}
        });save.IsDefault=true;actions.Children.Add(save);actions.Children.Add(BusinessForm.Button("Common_Cancel",(_,_)=>close(),"GhostButton"));panel.Children.Add(actions);
        await InAppDialog.ShowAsync(panel,c=>close=c,initialFocus:value,contentWidth:560,canClose:()=>!busy);
        if(saved){await LoadAsync();Record();}
    }
    private sealed class ChargeEntry{public string Mode{get;set;}="FIXED";public string Value{get;set;}="0";}
}
