using SupermarketPos.Application.Abstractions;
using SupermarketPos.Infrastructure.Business;
namespace SupermarketPos.Wpf.Views;
public sealed partial class DigitalServicesView
{
    private readonly RetailInventory? _retailInventory;
    private readonly RetailReferenceCatalog? _references;
    private readonly IProductRepository? _products;
    private readonly bool _configuration;
    private readonly string _accountGroup="package-account-"+Guid.NewGuid().ToString("N");
    public Task OpenSalesAsync(string? type=null)=>Run(async()=>
    {
        if(_retailInventory is not null)await _retailInventory.EnsureProviderTemplateAsync();await LoadAsync();
        if(type is "LOAD" or "PACKAGE"&&!HasDraft)_entry.Type=type;
        Record();
    });
}
