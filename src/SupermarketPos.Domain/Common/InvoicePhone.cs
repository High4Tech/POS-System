namespace SupermarketPos.Domain.Common;
public static class InvoicePhone
{
    public static string Normalize(string? raw,string? countryCallingCode=null)
    {
        raw=(raw??"").Trim();
        if(raw.Any(c=>!char.IsAsciiDigit(c)&&c is not ('+' or ' ' or '-' or '(' or ')')))throw new DomainException("Invoice_PhoneInvalid");
        if(raw.Count(c=>c=='+')>1||raw.Contains('+')&&!raw.StartsWith('+'))throw new DomainException("Invoice_PhoneInvalid");
        var digits=new string(raw.Where(char.IsAsciiDigit).ToArray());
        if(digits.StartsWith("00"))digits=digits[2..];
        else if(!raw.StartsWith('+')&&digits.StartsWith("03")&&digits.Length==11&&countryCallingCode is null or "92")digits="92"+digits[1..];
        else if(!raw.StartsWith('+')&&digits.StartsWith('0'))
        {
            if(string.IsNullOrEmpty(countryCallingCode)||countryCallingCode.Any(c=>!char.IsAsciiDigit(c)))throw new DomainException("Invoice_PhoneInvalid");
            digits=countryCallingCode+digits[1..];
        }
        else if(!raw.StartsWith('+')&&countryCallingCode=="92"&&digits.StartsWith('3')&&digits.Length==10)digits="92"+digits;
        if(digits.Length is <8 or >15||digits.StartsWith('0'))throw new DomainException("Invoice_PhoneInvalid");
        if(digits.StartsWith("92")&&digits.Length!=12)throw new DomainException("Invoice_PhoneInvalid");
        return digits;
    }
}
