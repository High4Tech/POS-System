using Microsoft.EntityFrameworkCore;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
namespace SupermarketPos.Infrastructure.Business;

public sealed partial class DigitalServiceLedger
{
    public async Task<IReadOnlyList<DigitalCustomerSummary>> CustomersAsync()
    {
        _=auth.CurrentUser??throw new DomainException("Service_SignIn");
        var groups=await db.ServiceTransactions.AsNoTracking().Where(t=>t.RecipientKey!=""&&t.ReversesId==null)
            .GroupBy(t=>t.RecipientKey).Select(g=>new{Key=g.Key,Count=g.Count(),Last=g.Max(t=>t.OccurredAt)}).ToListAsync();
        var names=await db.DigitalCustomers.AsNoTracking().ToDictionaryAsync(c=>c.RecipientKey,c=>c.Name);
        var providers=await db.ServiceProviders.AsNoTracking().ToDictionaryAsync(p=>p.Id,p=>p.Name);
        string Provider(string key){var parts=key.Split(':',3);return parts[0]=="account"&&parts.Length==3&&int.TryParse(parts[1],out var id)?providers.GetValueOrDefault(id)??"":"";}
        return groups.OrderByDescending(g=>g.Last).Select(g=>new DigitalCustomerSummary(g.Key,DigitalRecipient.Identifier(g.Key),names.GetValueOrDefault(g.Key)??"",g.Count,g.Last,Provider(g.Key))).ToArray();
    }
    public async Task<IReadOnlyList<ServiceTransaction>> CustomerHistoryAsync(string key)
    {
        _=auth.CurrentUser??throw new DomainException("Service_SignIn");
        return await db.ServiceTransactions.AsNoTracking().Where(t=>t.RecipientKey==key).OrderByDescending(t=>t.OccurredAt).ThenByDescending(t=>t.Id).ToListAsync();
    }
    public async Task SaveCustomerNameAsync(string key,string name)
    {
        _=auth.CurrentUser??throw new DomainException("Service_SignIn");name=DigitalRecipient.Name(name);
        if(!await db.ServiceTransactions.AnyAsync(t=>t.RecipientKey==key))throw new DomainException("Service_SelectTransaction");
        var customer=await db.DigitalCustomers.SingleOrDefaultAsync(c=>c.RecipientKey==key);
        if(customer is null)db.DigitalCustomers.Add(new(){RecipientKey=key,Name=name});else customer.Name=name;
        await db.SaveChangesAsync();
    }
}
public sealed record DigitalCustomerSummary(string Key,string Identifier,string Name,int Transactions,DateTime LastActivity,string Provider="")
{
    public string Last=>LastActivity.ToString("g");
    public bool Matches(string text)
    {
        if(Identifier.Contains(text,StringComparison.OrdinalIgnoreCase)||Name.Contains(text,StringComparison.CurrentCultureIgnoreCase)||Provider.Contains(text,StringComparison.CurrentCultureIgnoreCase))return true;
        var compact=new string(text.Where(c=>c is not (' ' or '-' or '(' or ')')).ToArray());
        if(compact.Length>0&&Identifier.Contains(compact,StringComparison.OrdinalIgnoreCase))return true;
        if(Key.StartsWith("phone:",StringComparison.Ordinal))
        {
            var prefix=compact.TrimStart('+');
            if(prefix.StartsWith("0092",StringComparison.Ordinal))prefix=prefix[2..];
            else if(prefix.StartsWith("03",StringComparison.Ordinal))prefix="92"+prefix[1..];
            if(prefix.Length>0&&Identifier.StartsWith(prefix,StringComparison.Ordinal))return true;
        }
        return false;
    }
}
