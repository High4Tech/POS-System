using SupermarketPos.Domain.Common;
namespace SupermarketPos.Domain.Entities;

public sealed class StockReconciliation
{
    public int Id{get;set;}
    public int ProductId{get;set;}
    public int SaleLineId{get;set;}
    public DateTime CreatedAt{get;set;}
    public DateTime? ResolvedAt{get;private set;}
    public string Resolution{get;private set;}="";
    public string ResolvedBy{get;private set;}="";
    public void Resolve(string note,string user)
    {
        if(string.IsNullOrWhiteSpace(note)||note.Length>1000)throw new DomainException("Pharmacy_ResolutionRequired");
        if(ResolvedAt is not null)throw new DomainException("Pharmacy_AlreadyResolved");
        Resolution=note.Trim();ResolvedBy=user;ResolvedAt=DateTime.Now;
    }
}
