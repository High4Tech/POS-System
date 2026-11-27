using System.Text.RegularExpressions;

namespace SupermarketPos.Domain.Common;

public static class PakistanPayment
{
    public static string Mobile(string raw)
    {
        var value=Compact(raw);
        if(value.StartsWith("0092"))value=value[2..];
        if(value.StartsWith('+'))value=value[1..];
        if(Regex.IsMatch(value,@"^03[0-9]{9}$"))return "92"+value[1..];
        if(Regex.IsMatch(value,@"^923[0-9]{9}$"))return value;
        throw new DomainException("Digital_InvalidMobile");
    }
    public static string Recipient(string raw,string provider,string lengths="")
    {
        var value=Compact(raw).ToUpperInvariant();
        if(value.StartsWith("PK"))
        {
            if(!Regex.IsMatch(value,@"^PK[0-9]{2}[A-Z]{4}[A-Z0-9]{16}$"))throw new DomainException("Digital_InvalidIban");
            var remainder=0;
            foreach(var c in value[4..]+value[..4])
            {
                if(char.IsAsciiDigit(c))remainder=(remainder*10+c-'0')%97;
                else remainder=(remainder*100+c-'A'+10)%97;
            }
            if(remainder!=1)throw new DomainException("Digital_InvalidIban");
            return value;
        }
        try{return Mobile(raw);}catch(DomainException){}
        if(value.All(char.IsAsciiDigit)&&value.Length>0&&AccountLengths(provider,lengths).Contains(value.Length))return value;
        throw new DomainException("Digital_InvalidRecipient");
    }
    public static int[] AccountLengths(string provider,string configured="")
    {
        if(!ValidLengths(configured))throw new DomainException("Digital_InvalidRecipient");
        if(configured.Length>0)return configured.Split(',').Select(s=>int.Parse(s.Trim())).Distinct().ToArray();
        var name=new string(provider.Where(char.IsAsciiLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        // Published IBFT formats are defaults; a bank's verified current lengths can be saved in provider settings.
        foreach(var (bank,sizes) in Defaults)if(name.Contains(bank,StringComparison.Ordinal))return sizes;
        return [];
    }
    public static bool ValidLengths(string value)=>value.Length<=80&&(value.Length==0||value.Split(',').All(s=>int.TryParse(s.Trim(),out var n)&&n is >=6 and <=24));
    private static string Compact(string value)=>new(value.Trim().Where(c=>c is not (' ' or '-' or '(' or ')')).ToArray());
    private static readonly (string,int[])[] Defaults=
    [
        ("alliedbank",[13,20]),("askaribank",[13]),("bankalfalah",[12]),("bankalhabib",[17]),("bankislami",[15]),
        ("bankofpunjab",[17]),("faysalbank",[9,14]),("habibbank",[14]),("jsbank",[6]),("standardchartered",[11,16]),
        ("silkbank",[14]),("soneribank",[15]),("summitbank",[20]),("unitedbank",[9,12]),("citibank",[10]),
        ("dubaiislamic",[10]),("meezanbank",[14]),("apnamicrofinance",[12]),("sambabank",[10]),("sindhbank",[14]),
        ("albaraka",[13]),("habibmetropolitan",[19]),("firstwomen",[20]),("industrialandcommercialbankofchina",[16,19]),
        ("mcb",[16]),("umicrofinance",[15])
    ];
}
