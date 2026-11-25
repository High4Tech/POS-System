using System.Globalization;
using System.Text.RegularExpressions;

namespace SupermarketPos.Domain.Common;

public sealed record Gs1Barcode(string Gtin, string? Batch, DateOnly? ExpiryDate, string? SerialNumber)
{
    private const char Separator = '\u001d';
    private static readonly Regex PrintedAi = new(@"\((\d{2,4})\)", RegexOptions.CultureInvariant);
    private static readonly string[] Markers = ["]d2", "]Q3", "]C1"];
    public bool HasTrackingData() => Batch is not null || ExpiryDate is not null || SerialNumber is not null;

    public static bool IsMarked(string raw, string? symbology) =>
        Array.Exists(Markers, marker => marker == symbology || raw.StartsWith(marker, StringComparison.Ordinal)) ||
        raw.StartsWith(Separator) || IsDigitalLink(raw) || symbology is null && raw.StartsWith('(') && raw.Contains("(01)", StringComparison.Ordinal);

    public static Gs1Barcode? Parse(string raw, DateOnly today)
    {
        if(IsDigitalLink(raw))return ParseDigitalLink(raw,today);
        if (Array.Exists(Markers, marker => raw.StartsWith(marker, StringComparison.Ordinal))) raw = raw[3..];
        if (raw.StartsWith(Separator)) raw = raw[1..];
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (raw.StartsWith('('))
        {
            var matches = PrintedAi.Matches(raw);
            if (matches.Count == 0 || matches[0].Index != 0) return null;
            for (var i = 0; i < matches.Count; i++)
            {
                var start = matches[i].Index + matches[i].Length;
                var end = i + 1 < matches.Count ? matches[i + 1].Index : raw.Length;
                if (!fields.TryAdd(matches[i].Groups[1].Value, raw[start..end])) return null;
            }
        }
        else
        {
            var offset = 0;
            while (offset < raw.Length)
            {
                if (raw.Length - offset < 2) return null;
                var ai = raw.Substring(offset, 2);
                offset += 2;
                var length = FixedLength(ai);
                if (length == 0)
                {
                    if (ai is not ("10" or "21")) return null;
                    var end = raw.IndexOf(Separator, offset);
                    length = (end < 0 ? raw.Length : end) - offset;
                }
                if (length <= 0 || raw.Length - offset < length || !fields.TryAdd(ai, raw.Substring(offset, length)))
                    return null;
                offset += length;
                if (offset < raw.Length && raw[offset] == Separator) offset++;
            }
        }

        foreach (var (ai, value) in fields)
        {
            var length = FixedLength(ai);
            if (length != 0 ? value.Length != length || !value.All(char.IsAsciiDigit) :
                ai is not ("10" or "21") || !IsIdentifier(value)) return null;
        }
        if (!fields.TryGetValue("01", out var gtin) || !HasValidCheckDigit(gtin)) return null;
        var expiry = fields.GetValueOrDefault("17") is { } encoded ? ParseDate(encoded, today) : null;
        if (fields.ContainsKey("17") && expiry is null) return null;
        return new Gs1Barcode(gtin, fields.GetValueOrDefault("10"), expiry, fields.GetValueOrDefault("21"));
    }

    public static bool IsIdentifier(string value) => value.Length is >= 1 and <= 20 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || "!\"%&'()*+,-./:;<=>?_".Contains(c));
    public static bool IsDigitalLink(string raw)=>Uri.TryCreate(raw,UriKind.Absolute,out var uri)&&uri.Scheme is "http" or "https"&&uri.AbsolutePath.Split('/').Contains("01");
    private static Gs1Barcode? ParseDigitalLink(string raw,DateOnly today)
    {
        if(raw.Length>512||Regex.IsMatch(raw,"%(?![A-Fa-f0-9]{2})")||!Uri.TryCreate(raw,UriKind.Absolute,out var uri)||uri.UserInfo.Length>0)return null;
        var path=uri.AbsolutePath.Trim('/').Split('/');var start=Array.IndexOf(path,"01");if(start<0||(path.Length-start)%2!=0)return null;
        var fields=new Dictionary<string,string>(StringComparer.Ordinal);var last=0;
        for(var i=start;i<path.Length;i+=2)
        {
            var ai=path[i];var order=ai switch{"01"=>1,"10"=>2,"21"=>3,_=>0};
            if(order<=last||!fields.TryAdd(ai,Uri.UnescapeDataString(path[i+1])))return null;last=order;
        }
        foreach(var part in uri.Query.TrimStart('?').Split('&',';'))
        {
            if(part.Length==0)continue;var pair=part.Split('=',2);var key=Uri.UnescapeDataString(pair[0]);if(!key.All(char.IsAsciiDigit))continue;
            if(pair.Length!=2||key is not ("11" or "13" or "15" or "16" or "17")||!fields.TryAdd(key,Uri.UnescapeDataString(pair[1])))return null;
        }
        if(fields.Values.Any(v=>v.Any(char.IsControl)))return null;
        return Parse(string.Join(Separator,fields.Select(f=>f.Key+f.Value)),today);
    }

    public static bool HasValidCheckDigit(string value)
    {
        if (value.Length is not (8 or 12 or 13 or 14) || !value.All(char.IsAsciiDigit)) return false;
        var sum = 0;
        for (var i = value.Length - 2; i >= 0; i--)
            sum += (value[i] - '0') * ((value.Length - i) % 2 == 0 ? 3 : 1);
        return (10 - sum % 10) % 10 == value[^1] - '0';
    }

    private static int FixedLength(string ai) => ai switch { "01" => 14, "11" or "13" or "15" or "16" or "17" => 6, _ => 0 };

    private static DateOnly? ParseDate(string text, DateOnly today)
    {
        var year = today.Year / 100 * 100 + int.Parse(text[..2], CultureInfo.InvariantCulture);
        if (year - today.Year > 50) year -= 100;
        if (today.Year - year > 49) year += 100;
        var month = int.Parse(text.AsSpan(2, 2), CultureInfo.InvariantCulture);
        var day = int.Parse(text.AsSpan(4, 2), CultureInfo.InvariantCulture);
        if (year is < 1 or > 9999 || month is < 1 or > 12) return null;
        var last = DateTime.DaysInMonth(year, month);
        return day > last ? null : new DateOnly(year, month, day == 0 ? last : day);
    }
}
