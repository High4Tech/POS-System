using System.Globalization;
using System.Resources;

namespace SupermarketPos.Domain.Localization;

public static class PosText
{
    private static readonly ResourceManager Resources =
        new("SupermarketPos.Domain.Localization.Strings", typeof(PosText).Assembly);

    public static string NormalizeLanguage(string? code) =>
        string.Equals(code?.Trim(), "ur", StringComparison.OrdinalIgnoreCase) ? "ur" : "en";

    public static CultureInfo Culture(string? code) =>
        CultureInfo.GetCultureInfo(NormalizeLanguage(code) == "ur" ? "ur-PK" : "en-PK");

    public static string Get(string key, CultureInfo culture) => Resources.GetString(key, culture) ?? key;
}
