using System.Globalization;

namespace SupermarketPos.Domain.Common;

/// <summary>
/// Money helpers. ALL monetary values in the system are <see cref="decimal"/> (never float/double),
/// currency is PKR app-wide, and rounding is to 2 decimals using AwayFromZero.
/// This is the single rounding primitive the whole domain depends on.
/// </summary>
public static class Money
{
    public const string CurrencyCode = "PKR";

    public static string Format(decimal value) =>
        $"{CurrencyCode} {value.ToString("N2", CultureInfo.InvariantCulture)}";

    public static decimal Round2(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
