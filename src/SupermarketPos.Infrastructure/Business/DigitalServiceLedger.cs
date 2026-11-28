using Microsoft.EntityFrameworkCore;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Business;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Persistence;

namespace SupermarketPos.Infrastructure.Business;
public sealed partial class DigitalServiceLedger(PosDbContext db,IUnitOfWork work,IAuthService auth,IBusinessSetup setup)
{
    public async Task<IReadOnlyList<ServiceProviderAccount>> ProvidersAsync()=>await db.ServiceProviders.AsNoTracking().OrderBy(p=>p.Name).ToListAsync();
    public async Task<IReadOnlyList<MobileServicePackage>> PackagesAsync()=>await db.MobileServicePackages.AsNoTracking().OrderBy(p=>p.Name).ToListAsync();
    public async Task<IReadOnlyList<ServiceTransaction>> HistoryAsync()=>await db.ServiceTransactions.AsNoTracking().OrderByDescending(t=>t.OccurredAt).ThenByDescending(t=>t.Id).ToListAsync();
    public async Task<decimal> FloatAsync(int provider)
    {
        var account=await db.ServiceProviders.AsNoTracking().SingleAsync(p=>p.Id==provider);
        var movements=await db.ServicePostings.Where(p=>db.ServiceTransactions.Any(t=>t.Id==p.TransactionId&&t.ProviderId==provider)).Select(p=>p.FloatChange).ToListAsync();
        return account.OpeningFloat+movements.Sum();
    }
    public async Task SaveProviderAsync(ServiceProviderAccount entry)
    {
        auth.RequireAdmin();
        if((await setup.LoadAsync()).Profile.DigitalServicesAvailable==false)throw new DomainException("Service_Disabled");
        if(new[]{entry.DefaultCustomerFee,entry.DefaultProviderFee,entry.DefaultCommission}.Any(v=>v<0||v!=Money.Round2(v))||entry.LoadCostRate is <0 or >10)throw new DomainException("Service_InvalidAmount");
        if(string.IsNullOrWhiteSpace(entry.Name)||entry.Name.Length>100||entry.OpeningFloat<0||entry.OpeningFloat!=Money.Round2(entry.OpeningFloat)||entry.FeePolicy.Length>300||entry.CommissionPolicy.Length>300||entry.AllowedTypes.Split(',').Any(c=>c is not ("CASHIN" or "CASHOUT" or "TRANSFER" or "LOAD" or "PACKAGE")))throw new DomainException("Service_Invalid");
        if(entry.CustomerFeeMode is not ("FIXED" or "PERCENT")||entry.CustomerFeePercent is <0 or >100||entry.CustomerFeePercent!=decimal.Round(entry.CustomerFeePercent,4)||!PakistanPayment.ValidLengths(entry.AccountNumberLengths))throw new DomainException("Service_InvalidAmount");
        var saved=entry.Id==0?null:await db.ServiceProviders.SingleAsync(p=>p.Id==entry.Id);
        if(saved is null)db.ServiceProviders.Add(entry);
        else {if(saved.OpeningFloat!=entry.OpeningFloat&&await db.ServiceTransactions.AnyAsync(t=>t.ProviderId==saved.Id))throw new DomainException("Service_OpeningLocked");var revision=saved.Revision;db.Entry(saved).CurrentValues.SetValues(entry);saved.Revision=revision+1;}
        await db.SaveChangesAsync();
    }
    public async Task SavePackageAsync(MobileServicePackage entry)
    {
        auth.RequireAdmin();
        if((await setup.LoadAsync()).Profile.DigitalServicesAvailable==false)throw new DomainException("Service_Disabled");
        if(!await db.ServiceProviders.AnyAsync(p=>p.Id==entry.ProviderId&&p.IsActive&&p.AllowedTypes.Contains("PACKAGE"))||string.IsNullOrWhiteSpace(entry.Name)||entry.Name.Length>150||entry.Kind.Length>80||entry.Validity.Length>100||entry.AccountType is not ("Prepaid" or "Postpaid")||entry.PackageIdentifier.Length>100||entry.Benefits.Length>1000||entry.Price<=0||new[]{entry.Price,entry.ProviderCost,entry.Commission,entry.CustomerFee,entry.ProviderFee}.Any(v=>v<0||v!=Money.Round2(v)))throw new DomainException("Service_Invalid");
        if(entry.Id==0)db.MobileServicePackages.Add(entry);else{var saved=await db.MobileServicePackages.SingleAsync(p=>p.Id==entry.Id);db.Entry(saved).CurrentValues.SetValues(entry);}await db.SaveChangesAsync();
    }
    public Task<ServiceTransaction> SaveAsync(ServiceTransaction entry,bool confirmedExternally)=>SaveCoreAsync(entry,confirmedExternally,false);
    public async Task<ServiceTransaction> SellPackageAsync(int packageId,decimal price,decimal? cost,string phone,string reference,string status,bool confirmedExternally,Guid confirmationId)
    {
        var package=await db.MobileServicePackages.AsNoTracking().SingleAsync(p=>p.Id==packageId);
        var tx=PackageQuote(package,price,cost,cost.HasValue);tx.ConfirmationId=confirmationId;tx.CustomerPhone=phone;tx.Reference=reference;tx.Status=status;tx.OccurredAt=DateTime.Now;
        return await SaveCoreAsync(tx,confirmedExternally,true);
    }
    private Task<ServiceTransaction> SaveCoreAsync(ServiceTransaction entry,bool confirmedExternally,bool packageOverrides)=>work.ExecuteInTransactionAsync(async ct=>
    {
        var user=auth.CurrentUser??throw new DomainException("Service_SignIn");entry.Validate();
        var business=(await setup.LoadAsync(ct)).Profile;
        if(entry.Type is "CASHIN" or "CASHOUT" or "TRANSFER"?!business.DigitalWalletAvailable:!business.MobileRechargeAvailable)throw new DomainException("Service_Disabled");
        entry.RecipientKey=DigitalRecipient.Key(entry.Type,entry.CustomerPhone,entry.ProviderId);
        entry.CustomerName=DigitalRecipient.Name(entry.CustomerName);
        if(await db.ServiceTransactions.AsNoTracking().SingleOrDefaultAsync(t=>t.ConfirmationId==entry.ConfirmationId,ct) is{} prior)
        {
            if(entry.CustomerName.Length==0)entry.CustomerName=prior.CustomerName;
            if(prior.RecipientKey!=entry.RecipientKey||prior.CustomerName!=entry.CustomerName)throw new DomainException("Service_ConfirmationConflict");
            if(prior.ProviderId!=entry.ProviderId||prior.PackageId!=entry.PackageId||prior.Principal!=entry.Principal||prior.Type!=entry.Type||prior.CustomerFee!=entry.CustomerFee||prior.ProviderFee!=entry.ProviderFee||prior.Commission!=entry.Commission||prior.ProviderCost!=entry.ProviderCost||prior.ProviderCostKnown!=entry.ProviderCostKnown||prior.Collected!=entry.Collected||prior.FloatChange!=entry.FloatChange||prior.CustomerPhone!=entry.CustomerPhone||prior.Reference!=entry.Reference||prior.PaymentMethod!=entry.PaymentMethod||prior.CheckoutId!=entry.CheckoutId||prior.AccountType!=entry.AccountType)throw new DomainException("Service_ConfirmationConflict");
            return prior;
        }
        var provider=await db.ServiceProviders.SingleOrDefaultAsync(p=>p.Id==entry.ProviderId,ct)??throw new DomainException("Service_SelectProvider");
        if(!provider.IsActive||!provider.AllowedTypes.Split(',').Contains(entry.Type))throw new DomainException("Service_Invalid");
        var customer=await db.DigitalCustomers.SingleOrDefaultAsync(c=>c.RecipientKey==entry.RecipientKey,ct);
        if(entry.CustomerName.Length>0){if(customer is null)db.DigitalCustomers.Add(new(){RecipientKey=entry.RecipientKey,Name=entry.CustomerName});else customer.Name=entry.CustomerName;}
        else entry.CustomerName=customer?.Name??"";
        if(entry.Type=="PACKAGE")
        {
            var pack=await db.MobileServicePackages.SingleOrDefaultAsync(p=>p.Id==entry.PackageId&&p.ProviderId==entry.ProviderId&&p.IsActive,ct)??throw new DomainException("Service_Invalid");
            var expected=packageOverrides?PackageQuote(pack,entry.Collected,entry.ProviderCost,entry.ProviderCostKnown):PackageQuote(pack);
            if(entry.Principal!=expected.Principal||entry.Collected!=expected.Collected||entry.ProviderCost!=expected.ProviderCost||entry.ProviderCostKnown!=expected.ProviderCostKnown||entry.Commission!=expected.Commission||entry.CustomerFee!=expected.CustomerFee||entry.ProviderFee!=expected.ProviderFee||entry.FloatChange!=expected.FloatChange)throw new DomainException("Service_CatalogChanged");
            entry.PackageName=pack.Name;entry.PackageIdentifier=pack.PackageIdentifier;entry.AccountType=pack.AccountType;entry.PackageBenefits=pack.Benefits;entry.PackageValidity=pack.Validity;
        }
        else if(entry.PackageId.HasValue)throw new DomainException("Service_Invalid");
        if(entry.Status=="Confirmed"&&entry.Reference.Length>0&&await db.ServiceTransactions.AnyAsync(t=>t.ProviderId==entry.ProviderId&&t.Reference==entry.Reference&&t.ReversesId==null&&(t.Status=="Confirmed"||t.Status=="Reversed"),ct))throw new DomainException("Service_DuplicateReference");
        entry.InvoiceProfileJson=System.Text.Json.JsonSerializer.Serialize(business);
        entry.Operator=user.DisplayName;entry.StoreName=business.StoreName;entry.ProviderName=provider.Name;db.ServiceTransactions.Add(entry);await db.SaveChangesAsync(ct);
        if(entry.Status=="Confirmed")await PostAsync(entry,provider,ct);await db.SaveChangesAsync(ct);return entry;
    });
    public Task<ServiceTransaction> SetStatusAsync(int id,string status,bool confirmedExternally,string? reference=null)=>work.ExecuteInTransactionAsync(async ct=>
    {
        if((await setup.LoadAsync(ct)).Profile.DigitalServicesAvailable==false)throw new DomainException("Service_Disabled");
        _=auth.CurrentUser??throw new DomainException("Service_SignIn");var entry=await db.ServiceTransactions.SingleAsync(t=>t.Id==id,ct);
        if(entry.Status==status)return entry;
        if(entry.Status is not ("Draft" or "Pending")||status is not ("Pending" or "Confirmed" or "Failed"))throw new DomainException("Service_Immutable");
        if(reference is not null)entry.Reference=reference.Trim();entry.Status=status;entry.Validate();entry.Revision++;
        if(status=="Confirmed"&&entry.Reference.Length>0&&await db.ServiceTransactions.AnyAsync(t=>t.Id!=id&&t.ProviderId==entry.ProviderId&&t.Reference==entry.Reference&&t.ReversesId==null&&(t.Status=="Confirmed"||t.Status=="Reversed"),ct))throw new DomainException("Service_DuplicateReference");
        if(status=="Confirmed")await PostAsync(entry,await db.ServiceProviders.SingleAsync(p=>p.Id==entry.ProviderId,ct),ct);
        await db.SaveChangesAsync(ct);return entry;
    });
    private async Task PostAsync(ServiceTransaction entry,ServiceProviderAccount provider,CancellationToken ct)
    {
        if(entry.Type is "LOAD" or "PACKAGE")PakistanPayment.Mobile(entry.CustomerPhone);else PakistanPayment.Recipient(entry.CustomerPhone,provider.Name,provider.AccountNumberLengths);
        if(await db.ServicePostings.AnyAsync(p=>p.TransactionId==entry.Id,ct))return;
        provider.Revision++;db.ServicePostings.Add(new(){TransactionId=entry.Id,CashChange=entry.CashChange,FloatChange=entry.FloatChange,Income=entry.Income});
    }
    public Task<ServiceTransaction> ReverseAsync(int id,string reference,bool confirmedExternally)=>work.ExecuteInTransactionAsync(async ct=>
    {
        if((await setup.LoadAsync()).Profile.DigitalServicesAvailable==false)throw new DomainException("Service_Disabled");
        auth.RequireAdmin();if(!confirmedExternally||reference.Length>80)throw new DomainException("Service_ExternalRequired");
        if(await db.ServiceTransactions.SingleOrDefaultAsync(t=>t.ReversesId==id,ct) is{} repeated)return repeated;
        var original=await db.ServiceTransactions.SingleAsync(t=>t.Id==id,ct);if(original.Status!="Confirmed"||original.ReversesId.HasValue)throw new DomainException("Service_Immutable");
        var posting=await db.ServicePostings.SingleAsync(p=>p.TransactionId==id,ct);
        var reversal=new ServiceTransaction{ConfirmationId=Guid.NewGuid(),ProviderId=original.ProviderId,Type="REVERSAL",Status="Confirmed",ReversesId=id,Reference=reference,Operator=auth.CurrentUser!.DisplayName,OccurredAt=DateTime.Now,StoreName=original.StoreName,ProviderName=original.ProviderName,Principal=original.Principal};
        reversal.InvoiceProfileJson=original.InvoiceProfileJson;reversal.RecipientKey=original.RecipientKey;reversal.CustomerName=original.CustomerName;reversal.CustomerPhone=original.CustomerPhone;
        db.ServiceTransactions.Add(reversal);original.Status="Reversed";original.Revision++;await db.SaveChangesAsync(ct);
        db.ServicePostings.Add(new(){TransactionId=reversal.Id,CashChange=-posting.CashChange,FloatChange=-posting.FloatChange,Income=-posting.Income});
        (await db.ServiceProviders.SingleAsync(p=>p.Id==original.ProviderId,ct)).Revision++;await db.SaveChangesAsync(ct);return reversal;
    });
    public async Task<ServicePeriodReport> ReportAsync(DateTime from,DateTime to)
    {
        var rows=await db.ServiceTransactions.AsNoTracking().Where(t=>t.OccurredAt>=from&&t.OccurredAt<to).ToListAsync();var ids=rows.Select(t=>t.Id).ToArray();
        var posts=await db.ServicePostings.AsNoTracking().Where(p=>ids.Contains(p.TransactionId)).ToListAsync();
        var reversedIds=rows.Where(t=>t.ReversesId.HasValue).Select(t=>t.ReversesId!.Value).ToArray();
        var originals=await db.ServiceTransactions.AsNoTracking().Where(t=>reversedIds.Contains(t.Id)).ToDictionaryAsync(t=>t.Id);
        var posted=posts.Select(p=>p.TransactionId).ToHashSet();
        decimal Net(Func<ServiceTransaction,decimal> amount,params string[] types)=>rows.Where(t=>posted.Contains(t.Id)).Sum(t=>t.ReversesId is{} id?(types.Length==0||types.Contains(originals[id].Type)?-amount(originals[id]):0):(types.Length==0||types.Contains(t.Type)?amount(t):0));
        var retail=await db.Sales.AsNoTracking().Include(s=>s.Lines).Where(s=>s.CreatedAt>=from&&s.CreatedAt<to).ToListAsync();
        var loose=retail.SelectMany(s=>s.Lines).Where(l=>l.UnitCode is "kg" or "litre").Sum(l=>l.LineGross);
        return new(posts.Sum(p=>p.Income),posts.Sum(p=>p.CashChange),posts.Sum(p=>p.FloatChange),Net(t=>t.Principal,"CASHIN"),Net(t=>t.Principal,"CASHOUT"),Net(t=>t.Collected,"LOAD","PACKAGE"),Net(t=>t.Commission),rows.Count(t=>t.Status is "Draft" or "Pending"),rows.Count(t=>t.Status is "Failed" or "Reversed"),retail.Sum(s=>s.GrossTotal)-loose,loose,Net(t=>t.Principal,"TRANSFER"),rows.Count(t=>posted.Contains(t.Id)&&(t.Type=="PACKAGE"||t.Type=="LOAD")&&!t.ProviderCostKnown));
    }
    public Task<List<ServicePosting>> PostingsAsync(string? recipientKey=null)=>db.ServicePostings.AsNoTracking()
        .Where(p=>recipientKey==null||db.ServiceTransactions.Any(t=>t.Id==p.TransactionId&&t.RecipientKey==recipientKey)).ToListAsync();
    public static ServiceTransaction PackageQuote(MobileServicePackage p,decimal? collected=null,decimal? cost=null,bool? costKnown=null)=>new(){Type="PACKAGE",ProviderId=p.ProviderId,PackageId=p.Id,PackageName=p.Name,PackageIdentifier=p.PackageIdentifier,AccountType=p.AccountType,PackageBenefits=p.Benefits,PackageValidity=p.Validity,Principal=(collected??(p.Price+p.CustomerFee))-p.CustomerFee,Collected=collected??(p.Price+p.CustomerFee),ProviderCost=(costKnown??p.ProviderCostKnown)?cost??p.ProviderCost:0,ProviderCostKnown=costKnown??p.ProviderCostKnown,CustomerFee=p.CustomerFee,ProviderFee=p.ProviderFee,Commission=p.Commission,FloatChange=0};
    public static ServiceTransaction ProviderQuote(ServiceProviderAccount p,string type,decimal amount)
    {
        if(type is not ("CASHIN" or "CASHOUT" or "TRANSFER" or "LOAD"))throw new DomainException("Service_Invalid");
        var cost=type=="LOAD"?Money.Round2(amount*p.LoadCostRate):0;
        var fee=p.CustomerCharge(amount);
        return new(){Type=type,ProviderId=p.Id,Principal=amount,CustomerFee=fee,ProviderFee=p.DefaultProviderFee,Commission=p.DefaultCommission,ProviderCost=cost,Collected=amount+fee,FloatChange=0};
    }
}
public sealed record ServicePeriodReport(decimal Income,decimal CashChange,decimal FloatChange,decimal CashIn,decimal CashOut,decimal RechargeCollected,decimal Commissions,int Pending,int FailedReversed,decimal PhysicalRevenue,decimal LooseRevenue,decimal Transfers,int UnknownCosts=0);
