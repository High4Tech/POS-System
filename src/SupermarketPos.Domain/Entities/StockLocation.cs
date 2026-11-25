using SupermarketPos.Domain.Common;
namespace SupermarketPos.Domain.Entities;
public sealed class StockLocation
{
    public int Id{get;private set;}
    public int? ParentId{get;private set;}
    public string Name{get;private set;}="";
    public string Barcode{get;private set;}="";
    public bool IsActive{get;private set;}=true;
    private StockLocation(){}
    public StockLocation(string name,int? parentId=null){Rename(name);ParentId=parentId;Barcode="POSLOC:"+Guid.NewGuid().ToString("N");}
    public void Rename(string name){name=name.Trim();if(name.Length is <1 or >100)throw new DomainException("Pharmacy_LocationNameRequired");Name=name;}
    public void SetActive(bool active)=>IsActive=active;
}
public sealed class StockPlacement
{
    public int Id{get;private set;}
    public int ProductId{get;private set;}
    public int? BatchId{get;private set;}
    public int LocationId{get;private set;}
    public string AllocationKey{get;private set;}="";
    public long BaseQuantity{get;private set;}
    public int Revision{get;private set;}
    private StockPlacement(){}
    public StockPlacement(int product,int? batch,int location){ProductId=product;BatchId=batch;LocationId=location;AllocationKey=$"{product}:{batch??0}:{location}";}
    public void Adjust(long delta){var next=checked(BaseQuantity+delta);if(next<0)throw new DomainException("Pharmacy_LocationStockShortage");BaseQuantity=next;Revision++;}
}
