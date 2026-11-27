using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Wpf.Common;

namespace SupermarketPos.Wpf.Views;

public sealed class DigitalCartLine:ObservableObject,IDataErrorInfo
{
    public Guid ConfirmationId{get;init;}=Guid.NewGuid();
    public required ServiceProviderAccount Provider{get;init;}
    public MobileServicePackage? Package{get;set;}
    public RetailReference? ReferencePackage{get;init;}
    public string AccountType{get;init;}="Prepaid";
    public string Type{get;init;}="LOAD";
    public string Name{get;init;}="";
    public string Title=>Type=="PACKAGE"?Name:LocalizationManager.Instance["Service_"+Type];
    public System.Windows.FlowDirection TitleDirection=>Title.Any(c=>c>=0x600&&c<=0x6ff)?System.Windows.FlowDirection.RightToLeft:System.Windows.FlowDirection.LeftToRight;
    public override string ToString()=>Title;
    public string ProviderName=>Provider.Name;
    public bool HasCost=>Type is "LOAD" or "PACKAGE";
    public bool HasFee=>Type!="PACKAGE";
    private string _amount="",_fee="0",_cost="",_recipient="",_reference="";
    public string AmountText{get=>_amount;set=>SetProperty(ref _amount,value);}
    public string FeeText{get=>_fee;set=>SetProperty(ref _fee,value);}
    public string CostText{get=>_cost;set=>SetProperty(ref _cost,value);}
    public string Recipient{get=>_recipient;set=>SetProperty(ref _recipient,value);}
    public string Reference{get=>_reference;set=>SetProperty(ref _reference,value);}
    public bool ShowValidation{get;set;}
    public decimal Total{get{try{return Number(AmountText,out var amount)?(Type=="CASHOUT"?-amount:amount)+(HasFee&&Number(FeeText,out var fee)?fee:0):0;}catch(OverflowException){return 0;}}}
    public void RefreshTotal()=>OnPropertyChanged(nameof(TotalText));
    public string TotalText=>Money.Format(Total);
    public string Error=>string.Join("\n",Errors().Values.Select(k=>LocalizationManager.Instance[k]).Distinct());
    public string this[string property]=>ShowValidation&&Errors().TryGetValue(property,out var key)?LocalizationManager.Instance[key]:"";
    public static bool Number(string text,out decimal value)=>decimal.TryParse(text,NumberStyles.Number,CultureInfo.InvariantCulture,out value)&&value==Money.Round2(value);
    public Dictionary<string,string> Errors()
    {
        var errors=new Dictionary<string,string>();
        if(!Number(AmountText,out var amount)||amount<=0||Type=="PACKAGE"&&amount<Package?.CustomerFee)errors[nameof(AmountText)]="Service_InvalidAmount";
        if(HasFee&&(!Number(FeeText,out var fee)||fee<0))errors[nameof(FeeText)]="Service_InvalidAmount";
        if(HasCost&&CostText.Trim().Length>0&&(!Number(CostText,out var cost)||cost<0))errors[nameof(CostText)]="Service_InvalidAmount";
        try{if(Number(AmountText,out var a)&&HasFee&&Number(FeeText,out var f))_=a+f;}catch(OverflowException){errors[nameof(AmountText)]="Service_InvalidAmount";}
        if(Type is "LOAD" or "PACKAGE"){try{InvoicePhone.Normalize(Recipient);}catch(DomainException){errors[nameof(Recipient)]="Validation_Phone";}}
        else if(Type=="TRANSFER"&&string.IsNullOrWhiteSpace(Recipient))errors[nameof(Recipient)]="Retail_RecipientRequired";
        if(Recipient.Length>60||Recipient.Any(char.IsControl))errors[nameof(Recipient)]="Validation_Text";
        if(Reference.Length>80||Reference.Any(char.IsControl))errors[nameof(Reference)]="Validation_Text";
        return errors;
    }
    public void ValidateFields(){ShowValidation=true;foreach(var name in new[]{nameof(AmountText),nameof(FeeText),nameof(CostText),nameof(Recipient),nameof(Reference)})OnPropertyChanged(name);}
    public ServiceTransaction Quote()
    {
        if(Errors().Count>0)throw new DomainException(Errors().First().Value);
        Number(AmountText,out var amount);Number(FeeText,out var fee);
        decimal? cost=CostText.Trim().Length>0&&Number(CostText,out var parsed)?parsed:null;
        var quote=Type=="PACKAGE"?DigitalServiceLedger.PackageQuote(Package??throw new DomainException("Service_Invalid"),amount,cost,cost.HasValue):DigitalServiceLedger.ProviderQuote(Provider,Type,amount);
        if(Type!="PACKAGE"){quote.CustomerFee=fee;quote.Collected=Type=="CASHOUT"?0:amount+fee;}
        if(Type=="LOAD"){quote.ProviderCost=cost??0;quote.ProviderCostKnown=cost.HasValue;}
        quote.ConfirmationId=ConfirmationId;quote.Status="Confirmed";quote.OccurredAt=DateTime.Now;quote.Reference=Reference.Trim();quote.CustomerPhone=Type is "LOAD" or "PACKAGE"?InvoicePhone.Normalize(Recipient):Recipient.Trim();
        return quote;
    }
}
