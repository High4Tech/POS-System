using SupermarketPos.Domain.Common;

namespace SupermarketPos.Domain.Entities;
public sealed class ServiceProviderAccount
{
    public string CatalogKey{get;set;}="";
    public int Id{get;set;} public string Name{get;set;}="";public string AllowedTypes{get;set;}="CASHIN,CASHOUT,LOAD,PACKAGE";
    public decimal OpeningFloat{get;set;} public string FeePolicy{get;set;}="";public string CommissionPolicy{get;set;}="";public bool IsActive{get;set;}=true;public int Revision{get;set;}
    public decimal DefaultCustomerFee{get;set;} public decimal DefaultProviderFee{get;set;} public decimal DefaultCommission{get;set;}
    public decimal LoadCostRate{get;set;}=1m;
    public string CustomerFeeMode{get;set;}="FIXED";
    public decimal CustomerFeePercent{get;set;}
    public string AccountNumberLengths{get;set;}="";
    public decimal CustomerCharge(decimal amount)=>CustomerFeeMode=="PERCENT"?Money.Round2(amount*(CustomerFeePercent/100m)):DefaultCustomerFee;
}
public sealed class MobileServicePackage
{
    public bool ProviderCostKnown{get;set;}=true;
    public string CatalogKey{get;set;}="";
    public int Id{get;set;}public int ProviderId{get;set;}public string Name{get;set;}="";public string Kind{get;set;}="";
    public decimal Price{get;set;}public decimal ProviderCost{get;set;}public decimal Commission{get;set;}public string Validity{get;set;}="";public bool IsActive{get;set;}=true;
    public string AccountType{get;set;}="Prepaid";public string PackageIdentifier{get;set;}="";public string Benefits{get;set;}="";
    public decimal CustomerFee{get;set;}public decimal ProviderFee{get;set;}
}
public sealed class ServiceTransaction
{
    public string RecipientKey{get;set;}="";
    public string CustomerName{get;set;}="";
    public int? CheckoutId{get;set;}
    public string PaymentMethod{get;set;}="CASH";
    public bool ProviderCostKnown{get;set;}=true;
    public string? InvoiceProfileJson{get;set;}
    public int Id{get;set;}public Guid ConfirmationId{get;set;}public int ProviderId{get;set;}public int? PackageId{get;set;}public int? ReversesId{get;set;}
    public string Type{get;set;}="CASHIN";public string Status{get;set;}="Draft";public string Reference{get;set;}="";
    public string CustomerPhone{get;set;}="";public string Operator{get;set;}="";public DateTime OccurredAt{get;set;}
    public string StoreName{get;set;}="";public string ProviderName{get;set;}="";public string PackageName{get;set;}="";
    public string PackageIdentifier{get;set;}="";public string AccountType{get;set;}="";public string PackageBenefits{get;set;}="";public string PackageValidity{get;set;}="";
    public decimal Principal{get;set;}public decimal CustomerFee{get;set;}public decimal ProviderFee{get;set;}public decimal Commission{get;set;}
    public decimal ProviderCost{get;set;}public decimal Collected{get;set;}public decimal FloatChange{get;set;}public int Revision{get;set;}
    public string ReceiptNumber=>CheckoutId is{} checkout?$"DS-{checkout:000000}":$"SVC-{Id:000000}";
    public decimal SettlementAmount=>Type=="CASHOUT"?-Principal+CustomerFee:Type is "CASHIN" or "TRANSFER"?Principal+CustomerFee:Collected;
    public decimal CashChange=>PaymentMethod=="CARD"?0:SettlementAmount;
    public decimal Income=>Type is "LOAD" or "PACKAGE"&&!ProviderCostKnown?0:(Type is "CASHIN" or "CASHOUT" or "TRANSFER"?CustomerFee:Collected-ProviderCost)+Commission-ProviderFee;
    public void Validate()
    {
        DigitalRecipient.Name(CustomerName);
        if(ConfirmationId==Guid.Empty||Type is not ("CASHIN" or "CASHOUT" or "TRANSFER" or "LOAD" or "PACKAGE")||Status is not ("Draft" or "Pending" or "Confirmed" or "Failed"))throw new DomainException("Service_Invalid");
        if(Principal<=0||new[]{Principal,CustomerFee,ProviderFee,Commission,ProviderCost,Collected,FloatChange}.Any(v=>v!=Money.Round2(v))||new[]{CustomerFee,ProviderFee,Commission,ProviderCost,Collected}.Any(v=>v<0))throw new DomainException("Service_InvalidAmount");
        if(Reference.Length>80||Reference.Any(char.IsControl)||CustomerPhone.Length>60||OccurredAt.Year<2000||OccurredAt>DateTime.Now.AddDays(1))throw new DomainException("Service_Invalid");
        if(Type is "LOAD" or "PACKAGE"&&(CustomerPhone.Count(char.IsAsciiDigit) is <7 or >15))throw new DomainException("Validation_Phone");
        if(Type=="TRANSFER"&&string.IsNullOrWhiteSpace(CustomerPhone))throw new DomainException("Retail_RecipientRequired");
        if(PaymentMethod is not ("CASH" or "CARD")||PaymentMethod=="CARD"&&Type=="CASHOUT")throw new DomainException("Digital_InvalidPayment");
    }
}
public sealed class DigitalCustomer
{
    public string RecipientKey{get;set;}="";
    public string Name{get;set;}="";
}
public sealed class ServicePosting
{
    public int Id{get;set;}public int TransactionId{get;set;}public decimal CashChange{get;set;}public decimal FloatChange{get;set;}public decimal Income{get;set;}
}

public sealed class ServiceCheckout
{
    public int Id{get;set;}public Guid ConfirmationId{get;set;}public DateTime CreatedAt{get;set;}
    public string PaymentMethod{get;set;}="CASH";public decimal Total{get;set;}public decimal Tendered{get;set;}
    public string InvoiceProfileJson{get;set;}="";public string StoreName{get;set;}="";public string Operator{get;set;}="";
    public List<ServiceTransaction> Lines{get;set;}=[];
    public string ReceiptNumber=>$"DS-{Id:000000}";
    public decimal Change=>PaymentMethod=="CASH"&&Total>0?Tendered-Total:0;
}
