using System.Globalization;

namespace SupermarketPos.Wpf.Common;

public static class MoneyText
{
    public static string Format(decimal value) => SupermarketPos.Domain.Common.Money.Format(value);

    public static string Qty(decimal qty, bool weighed) =>
        qty.ToString(weighed ? "0.000" : "0", CultureInfo.InvariantCulture);
}
