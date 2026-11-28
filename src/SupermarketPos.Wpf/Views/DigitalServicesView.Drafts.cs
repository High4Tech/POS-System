using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Common;
using SupermarketPos.Infrastructure.Commercial;

namespace SupermarketPos.Wpf.Views;
public sealed partial class DigitalServicesView
{
    private readonly ProtectedLocalStore? _draftStore;
    private readonly IAuthService? _auth;
    public IRelayCommand SaveDraftCommand{get;private set;}=null!;
    public IAsyncRelayCommand ResumeDraftCommand{get;private set;}=null!;
    private sealed record DigitalDraft(int Version,Guid CheckoutId,DigitalDraftRow[] Lines);
    private sealed record DigitalDraftRow(Guid Key,string Type,int Provider,int? Package,string? CatalogKey,string AccountType,string Name,string Amount,string Fee,string Cost,string Recipient,string Reference,bool FeeOverridden=true,string CustomerName="");
    private string DraftName=>"digital-cart-"+(_auth?.CurrentUser?.Id??throw new DomainException("Service_SignIn"));
    private void SaveDraft()
    {
        try
        {
            var lines=_cart.Select(r=>new DigitalDraftRow(r.ConfirmationId,r.Type,r.Provider.Id,r.Package?.Id,r.ReferencePackage?.Key,r.AccountType,r.Name,r.AmountText,r.FeeText,r.CostText,r.Recipient,r.Reference,r.FeeOverridden,r.CustomerName)).ToArray();
            _draftStore!.Write(DraftName,JsonSerializer.Serialize(new DigitalDraft(1,_checkoutKey,lines)));_status.Text=T("Digital_DraftSaved");
        }
        catch(Exception){_status.Text=T("Digital_DraftError");}
    }
    private async Task ResumeDraftAsync()
    {
        if(HasDraft)return;
        var json=_draftStore!.Read(DraftName);if(string.IsNullOrWhiteSpace(json)){_status.Text=T("Digital_DraftError");return;}
        var draft=JsonSerializer.Deserialize<DigitalDraft>(json)??throw new DomainException("Digital_DraftError");
        if(draft.Version!=1||draft.CheckoutId==Guid.Empty||draft.Lines.Length is 0 or >500||draft.Lines.Select(r=>r.Key).Distinct().Count()!=draft.Lines.Length)throw new DomainException("Digital_DraftError");
        await LoadAsync();var staged=new List<DigitalCartLine>();
        foreach(var saved in draft.Lines)
        {
            if(saved.Key==Guid.Empty||saved.Type is not ("LOAD" or "PACKAGE" or "TRANSFER" or "CASHOUT")||((saved.Type is "LOAD" or "PACKAGE")?!_business.MobileRechargeAvailable:!_business.DigitalWalletAvailable))throw new DomainException("Service_Disabled");
            var provider=_providers.SingleOrDefault(p=>p.Id==saved.Provider&&p.IsActive&&p.AllowedTypes.Split(',').Contains(saved.Type))??throw new DomainException("Service_SelectProvider");
            var package=saved.Package is{} id?_packages.SingleOrDefault(p=>p.Id==id&&p.ProviderId==provider.Id):_packages.SingleOrDefault(p=>saved.CatalogKey is not null&&p.CatalogKey==saved.CatalogKey&&p.ProviderId==provider.Id);
            if(package?.IsActive==false||saved.Package is not null&&package is null)throw new DomainException("Service_CatalogChanged");
            var reference=package is null&&saved.CatalogKey is{} key&&_references is not null?await _references.FindAsync(key):null;
            if(saved.Type=="PACKAGE"&&package is null&&(reference is null||reference.Kind!="package"||reference.Category is "directory" or "directory_only"))throw new DomainException("Service_CatalogChanged");
            staged.Add(new(){ConfirmationId=saved.Key,Type=saved.Type,Provider=provider,Package=package,ReferencePackage=reference,AccountType=saved.AccountType,Name=package?.Name??reference?.Name??saved.Name,AmountText=saved.Amount,FeeText=saved.Fee,CostText=saved.Cost,Recipient=saved.Recipient,Reference=saved.Reference,FeeOverridden=saved.FeeOverridden,CustomerName=saved.CustomerName});
        }
        _checkoutKey=draft.CheckoutId;foreach(var row in staged){row.PropertyChanged+=RowChanged;_cart.Add(row);}_resumed=true;_entry.Type=staged[0].Type;_entryDirty=false;Record();_status.Text=T("Digital_DraftResumed");
    }
    private void ClearCompletedDraft(Guid key)
    {
        try{if(_draftStore is not null&&_auth?.CurrentUser is not null&&_draftStore.Read(DraftName) is{} text&&!string.IsNullOrWhiteSpace(text)&&JsonSerializer.Deserialize<DigitalDraft>(text)?.CheckoutId==key)_draftStore.Write(DraftName,"");}
        catch(Exception){/* The stored confirmation key still makes a retry idempotent. */}
    }
}
