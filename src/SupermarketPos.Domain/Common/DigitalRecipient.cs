namespace SupermarketPos.Domain.Common;

public static class DigitalRecipient
{
    public static string Key(string type,string value,int provider)
    {
        try{return "phone:"+PakistanPayment.Mobile(value);}catch(DomainException)when(type is not ("LOAD" or "PACKAGE")){}
        var clean=new string(value.Trim().Where(c=>c is not (' ' or '-' or '(' or ')')).ToArray()).ToUpperInvariant();
        if(clean.Length==0||clean.Length>60||clean.Any(char.IsControl))throw new DomainException("Digital_InvalidRecipient");
        return clean.StartsWith("PK",StringComparison.Ordinal)?"iban:"+clean:$"account:{provider}:{clean}";
    }
    public static string Identifier(string key)
    {
        if(key.StartsWith("phone:",StringComparison.Ordinal))return key[6..];
        if(key.StartsWith("iban:",StringComparison.Ordinal))return key[5..];
        var colon=key.IndexOf(':',key.IndexOf(':')+1);return colon>=0?key[(colon+1)..]:key;
    }
    public static string Name(string? value)
    {
        var name=value?.Trim()??"";if(name.Length>150||name.Any(char.IsControl))throw new DomainException("Validation_Text");return name;
    }
}
