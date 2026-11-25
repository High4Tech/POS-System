using SupermarketPos.Domain.Common;

namespace SupermarketPos.Domain.Pricing;
public static class MeasuredGoods
{
    public static long BaseQuantity(decimal quantity,Unit unit)
    {
        if(!Enum.IsDefined(unit)||quantity<0)throw new DomainException("Measure_InvalidQuantity");
        var exact=checked(quantity*(unit==Unit.Piece?1m:1000m));
        if(exact!=decimal.Truncate(exact)||exact>long.MaxValue)throw new DomainException("Measure_InvalidQuantity");
        return (long)exact;
    }
    public static decimal Quantity(long bases,Unit unit)=>bases/(unit==Unit.Piece?1m:1000m);
    public static (long BaseQuantity,decimal Quantity,decimal Charge) ForAmount(decimal amount,decimal price,Unit unit)
    {
        if(unit==Unit.Piece||price<=0||amount<=0||amount!=Money.Round2(amount))throw new DomainException("Measure_InvalidAmount");
        var bases=checked((long)decimal.Floor(amount/price*1000m));
        if(bases<=0)throw new DomainException("Measure_TooSmall");
        var charge=Money.Round2(bases*price/1000m);
        while(charge>amount&&bases>0){bases--;charge=Money.Round2(bases*price/1000m);}
        if(bases<=0)throw new DomainException("Measure_TooSmall");
        return (bases,bases/1000m,charge);
    }
}
