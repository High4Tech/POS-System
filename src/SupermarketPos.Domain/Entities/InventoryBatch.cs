using SupermarketPos.Domain.Common;

namespace SupermarketPos.Domain.Entities;

public sealed class InventoryBatch
{
    public int Id { get; private set; }
    public int ProductId { get; private set; }
    public string Lot { get; private set; } = "";
    public DateOnly ExpiryDate { get; private set; }
    public long BaseQuantity { get; private set; }
    public Unit BaseUnit { get; private set; }
    public decimal Quantity=>Pricing.MeasuredGoods.Quantity(BaseQuantity,BaseUnit);
    public int? SupplierId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public string HoldReason{get;private set;}="";
    public void SetHold(string reason){if(reason.Length>300)throw new DomainException("Pharmacy_InvalidReference");HoldReason=reason.Trim();UpdatedAt=DateTime.Now;}

    private InventoryBatch() { }

    internal InventoryBatch(string lot, DateOnly expiry, decimal quantity, int? supplier, DateTime now,Unit unit)
    {
        Lot = lot;
        ExpiryDate = expiry;
        SupplierId = supplier;
        CreatedAt = UpdatedAt = now;
        BaseUnit=unit;BaseQuantity=Pricing.MeasuredGoods.BaseQuantity(quantity,unit);
    }

    internal void Add(decimal quantity, DateTime now)
    {
        BaseQuantity=checked(BaseQuantity+Pricing.MeasuredGoods.BaseQuantity(quantity,BaseUnit));
        UpdatedAt = now;
    }

    internal void Take(decimal quantity, DateTime now)
    {
        if(HoldReason.Length>0)throw new DomainException($"Batch {Lot} is on hold: {HoldReason}");
        if (ExpiryDate < DateOnly.FromDateTime(now))
            throw new DomainException($"Product expired on {ExpiryDate:yyyy-MM-dd} (batch {Lot}).");
        if (quantity > Quantity)
            throw new InsufficientStockException(Lot, Quantity, quantity);
        BaseQuantity-=Pricing.MeasuredGoods.BaseQuantity(quantity,BaseUnit);
        UpdatedAt = now;
    }
}
