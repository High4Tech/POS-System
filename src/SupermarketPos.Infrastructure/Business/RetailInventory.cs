using Microsoft.EntityFrameworkCore;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Persistence;
namespace SupermarketPos.Infrastructure.Business;

public sealed class RetailInventory(PosDbContext db,IUnitOfWork work,IAuthService auth,IBusinessSetup setup,RetailReferenceCatalog catalog,DigitalServiceLedger digital)
{
    private async Task RequireAsync()
    {auth.RequireAdmin();if((await setup.LoadAsync()).Profile.IsPharmacy)throw new DomainException("Retail_Only");}
    public async Task EnsureProviderTemplateAsync()
    {
        var profile=(await setup.LoadAsync()).Profile;
        if(profile.IsPharmacy||profile.RetailTemplate!="pakistan-tech"||!profile.DigitalServicesAvailable||auth.CurrentUser?.IsAdmin!=true)return;
        var existing=(await db.ServiceProviders.Select(p=>p.Name).ToArrayAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var reference in await catalog.SearchAsync("","provider",1000))if(existing.Add(reference.Name))
            db.ServiceProviders.Add(new(){CatalogKey=reference.Key,Name=reference.Name,AllowedTypes=reference.Kind=="network"?"LOAD,PACKAGE":"CASHIN,CASHOUT,TRANSFER"});
        await db.SaveChangesAsync();
    }
    public async Task<Product> QuickAddAsync(string name,string kind="labeled",RetailReference? reference=null,decimal price=0,decimal? cost=null)
    {
        await RequireAsync();if(kind=="loose"&&!(await setup.LoadAsync()).Profile.LooseGoodsAvailable)throw new DomainException("Service_Disabled");
        name=name.Trim();if(name.Length is <1 or >200||name.Any(char.IsControl))throw new DomainException("Validation_Text");
        return await work.ExecuteInTransactionAsync(async ct=>
        {
            if(reference is not null&&await db.Products.FirstOrDefaultAsync(p=>p.CatalogKey==reference.Key,ct) is{} linked)return linked;
            if(reference is null&&await db.Products.FirstOrDefaultAsync(p=>p.Name.ToLower()==name.ToLower(),ct) is{} existing)return existing;
            var categoryName=reference?.Category is{Length:>0} category&&reference.Kind=="model"?category:kind=="digital"?"Digital Services":kind=="loose"?"Loose Goods":"Other Products";
            var savedCategory=await db.Categories.FirstOrDefaultAsync(c=>c.Name==categoryName,ct);
            if(savedCategory is null){savedCategory=new Category(categoryName);savedCategory.SetColor("#2563EB");db.Categories.Add(savedCategory);await db.SaveChangesAsync(ct);}
            var product=new Product("INTERNAL-"+Guid.NewGuid().ToString("N"),name,price,0,Unit.Piece,0,savedCategory.Id);
            product.MarkStockUntracked();product.SetRetailKind(kind);product.SetPurchaseCost(cost);product.MarkQuickEntry();
            if(reference is not null)
            {
                product.SetCatalogKey(reference.Key);
                if(reference.Kind=="model")product.SetRetailDetails(reference.Brand,reference.Model,"");
                else if(reference.Kind=="package")
                {
                    if(reference.Category is "directory" or "directory_only"||reference.State=="directory_not_sellable")throw new DomainException("Service_NoPackages");
                    var provider=await UseProviderAsync((await catalog.FindAsync("network:"+reference.Model))??throw new DomainException("Service_SelectProvider"));
                    var package=await db.MobileServicePackages.FirstOrDefaultAsync(p=>p.CatalogKey==reference.Key,ct);
                    if(package is null)
                    {
                        package=new(){CatalogKey=reference.Key,Name=reference.Name,ProviderId=provider.Id,Price=price,ProviderCost=cost??throw new DomainException("Validation_Required"),PackageIdentifier=reference.Key,Validity=await catalog.PackageValidityAsync(reference.Key),Benefits=reference.Details[..Math.Min(1000,reference.Details.Length)],AccountType="Prepaid"};
                        await digital.SavePackageAsync(package);
                    }
                    product.ConfigureService("PACKAGE",provider.Id,package.Id);
                }
            }
            db.Products.Add(product);await db.SaveChangesAsync(ct);db.Entry(product).State=EntityState.Detached;return product;
        });
    }
    public async Task<ServiceProviderAccount> UseProviderAsync(RetailReference reference)
    {
        await RequireAsync();
        if(await db.ServiceProviders.FirstOrDefaultAsync(p=>p.CatalogKey==reference.Key||p.Name==reference.Name) is{} saved)return saved;
        var account=new ServiceProviderAccount{CatalogKey=reference.Key,Name=reference.Name,AllowedTypes=reference.Kind=="network"?"LOAD,PACKAGE":"CASHIN,CASHOUT,TRANSFER"};
        await digital.SaveProviderAsync(account);return account;
    }
    public async Task<int> LoadCategoryTemplateAsync()
    {
        await RequireAsync();var existing=(await db.Categories.Select(c=>c.Name).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);var count=0;
        foreach(var(name,_) in await catalog.CategoriesAsync())if(existing.Add(name)){var c=new Category(name);c.SetColor("#2563EB");db.Categories.Add(c);count++;}
        await db.SaveChangesAsync();return count;
    }
    public async Task RememberAsync(Product requested,decimal price,CancellationToken ct=default)
    {
        await RequireAsync();var product=await db.Products.SingleAsync(p=>p.Id==requested.Id,ct);
        if(!product.QuickEntry)return;
        var profile=(await setup.LoadAsync()).Profile;
        if(requested.Kind=="loose"&&!profile.LooseGoodsAvailable)throw new DomainException("Service_Disabled");
        if(price<=0||price!=Money.Round2(price))throw new DomainException("Retail_EnterPrice");
        if(await db.SaleLines.AnyAsync(l=>l.ProductId==product.Id,ct))throw new DomainException("Measure_ChangedUnit");
        product.SetUnit(requested.Unit);product.SetRetailKind(requested.Kind);
        if(requested.Kind=="digital")
        {
            if(requested.ServiceProviderId is{} id&&!await db.ServiceProviders.AnyAsync(p=>p.Id==id&&p.IsActive,ct))throw new DomainException("Service_SelectProvider");
            product.ConfigureService(requested.ServiceType,requested.ServiceProviderId,requested.ServicePackageId);
        }
        product.SetPrice(price);product.CompleteQuickEntry();await db.SaveChangesAsync(ct);
    }
    public Task SaveServiceAsync(int id,string name,decimal price,string type,int? provider)=>work.ExecuteInTransactionAsync(async ct=>
    {
        await RequireAsync();
        if(type!="SERVICE"&&!await db.ServiceProviders.AnyAsync(p=>p.Id==provider&&p.IsActive&&p.AllowedTypes.Contains(type)))throw new DomainException("Service_SelectProvider");
        var product=id==0?await QuickAddAsync(name,"digital"):await db.Products.SingleAsync(p=>p.Id==id,ct);
        if(product.Kind!="digital")throw new DomainException("Retail_InvalidKind");
        if(db.Entry(product).State==EntityState.Detached)db.Attach(product);
        product.SetName(name);product.SetPrice(price);product.ConfigureService(type,provider);await db.SaveChangesAsync();return true;
    });
}
