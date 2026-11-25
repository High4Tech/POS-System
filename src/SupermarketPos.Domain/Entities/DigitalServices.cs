using SupermarketPos.Domain.Common;

namespace SupermarketPos.Domain.Entities;
public sealed class ServiceProviderAccount
{
    public int Id{get;set;} public string Name{get;set;}="";public string AllowedTypes{get;set;}="CASHIN,CASHOUT,LOAD,PACKAGE";
    public decimal OpeningFloat{get;set;} public string FeePolicy{get;set;}="";public string CommissionPolicy{get;set;}="";public bool IsActive{get;set;}=true;public int Revision{get;set;}
    public decimal DefaultCustomerFee{get;set;} public decimal DefaultProviderFee{get;set;} public decimal DefaultCommission{get;set;}
    public decimal LoadCostRate{get;set;}=1m;
}
public sealed class MobileServicePackage
{
    public int Id{get;set;}public int ProviderId{get;set;}public string Name{get;set;}="";public string Kind{get;set;}="";
    public decimal Price{get;set;}public decimal ProviderCost{get;set;}public decimal Commission{get;set;}public string Validity{get;set;}="";public bool IsActive{get;set;}=true;
    public string AccountType{get;set;}="Prepaid";public string PackageIdentifier{get;set;}="";public string Benefits{get;set;}="";
    public decimal CustomerFee{get;set;}public decimal ProviderFee{get;set;}
}
public sealed class ServiceTransaction
{
    public string? InvoiceProfileJson{get;set;}
    public int Id{get;set;}public Guid ConfirmationId{get;set;}public int ProviderId{get;set;}public int? PackageId{get;set;}public int? ReversesId{get;set;}
    public string Type{get;set;}="CASHIN";public string Status{get;set;}="Draft";public string Reference{get;set;}="";
    public string CustomerPhone{get;set;}="";public string Operator{get;set;}="";public DateTime OccurredAt{get;set;}
    public string StoreName{get;set;}="";public string ProviderName{get;set;}="";public string PackageName{get;set;}="";
    public string PackageIdentifier{get;set;}="";public string AccountType{get;set;}="";public string PackageBenefits{get;set;}="";public string PackageValidity{get;set;}="";
    public decimal Principal{get;set;}public decimal CustomerFee{get;set;}public decimal ProviderFee{get;set;}public decimal Commission{get;set;}
    public decimal ProviderCost{get;set;}public decimal Collected{get;set;}public decimal FloatChange{get;set;}public int Revision{get;set;}
    public string ReceiptNumber=>$"SVC-{Id:000000}";
    public decimal CashChange=>Type=="CASHOUT"?-Principal+CustomerFee:Type=="CASHIN"?Principal+CustomerFee:Collected;
    public decimal Income=>(Type is "CASHIN" or "CASHOUT"?CustomerFee:Collected-ProviderCost)+Commission-ProviderFee;
    public void Validate()
    {
        if(ConfirmationId==Guid.Empty||Type is not ("CASHIN" or "CASHOUT" or "LOAD" or "PACKAGE")||Status is not ("Draft" or "Pending" or "Confirmed" or "Failed"))throw new DomainException("Service_Invalid");
        if(Principal<=0||new[]{Principal,CustomerFee,ProviderFee,Commission,ProviderCost,Collected,FloatChange}.Any(v=>v!=Money.Round2(v))||new[]{CustomerFee,ProviderFee,Commission,ProviderCost,Collected}.Any(v=>v<0))throw new DomainException("Service_InvalidAmount");
        if(Reference.Length>80||Reference.Any(char.IsControl)||CustomerPhone.Length>60||OccurredAt.Year<2000||OccurredAt>DateTime.Now.AddDays(1))throw new DomainException("Service_Invalid");
        if(Type is "LOAD" or "PACKAGE"&&(CustomerPhone.Count(char.IsAsciiDigit) is <7 or >15))throw new DomainException("Validation_Phone");
        if(Status=="Confirmed"&&string.IsNullOrWhiteSpace(Reference))throw new DomainException("Service_ReferenceRequired");
    }
}
public sealed class ServicePosting
{
    public int Id{get;set;}public int TransactionId{get;set;}public decimal CashChange{get;set;}public decimal FloatChange{get;set;}public decimal Income{get;set;}
}
