using Microsoft.EntityFrameworkCore;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Persistence;
namespace SupermarketPos.Infrastructure.Business;
public sealed record LocationStock(int Id,string Name,long BaseQuantity);
public sealed class StockLocationService(PosDbContext db,IUnitOfWork uow,IAuthService auth):IStockLocations
{
    private async Task<T> ReadAsync<T>(Func<PosDbContext,Task<T>> query)
    {
        var options=new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(db.Database.GetConnectionString());
        if(options.DataSource==":memory:"||options.Mode==Microsoft.Data.Sqlite.SqliteOpenMode.Memory)return await query(db);
        await using var read=new PosDbContext(new DbContextOptionsBuilder<PosDbContext>().UseSqlite(options.ToString()).Options);return await query(read);
    }
    public Task<StockLocation[]> LocationsAsync(CancellationToken ct=default)=>ReadAsync(read=>read.StockLocations.AsNoTracking().Where(l=>l.IsActive).OrderBy(l=>l.ParentId).ThenBy(l=>l.Name).ToArrayAsync(ct));
    public Task<StockLocation?> FindAsync(string barcode,CancellationToken ct=default)=>ReadAsync(read=>read.StockLocations.AsNoTracking().SingleOrDefaultAsync(l=>l.Barcode==barcode&&l.IsActive,ct));
    public Task<int[]> ProductIdsAtAsync(int locationId,CancellationToken ct=default)=>ReadAsync(read=>
    {
        var locations=read.StockLocations.Where(l=>l.Id==locationId||l.ParentId==locationId).Select(l=>l.Id);
        return read.StockPlacements.AsNoTracking().Where(p=>locations.Contains(p.LocationId)).Select(p=>p.ProductId).Union(read.Products.Where(p=>p.DefaultLocationId.HasValue&&locations.Contains(p.DefaultLocationId.Value)).Select(p=>p.Id)).Distinct().ToArrayAsync(ct);
    });
    public Task<Dictionary<int,long>> QuantitiesAtAsync(int? locationId,CancellationToken ct=default)=>ReadAsync(read=>
    {
        var stocks=read.StockPlacements.AsNoTracking();if(locationId is{} id)stocks=stocks.Where(p=>read.StockLocations.Any(l=>(l.Id==id||l.ParentId==id)&&l.Id==p.LocationId));
        return stocks.GroupBy(p=>p.ProductId).Select(g=>new{Id=g.Key,Quantity=g.Sum(p=>p.BaseQuantity)}).ToDictionaryAsync(g=>g.Id,g=>g.Quantity,ct);
    });
    public Task<Dictionary<int,long>> BatchQuantitiesAtAsync(int? locationId,CancellationToken ct=default)=>ReadAsync(read=>
    {
        var stocks=read.StockPlacements.AsNoTracking().Where(p=>p.BatchId.HasValue);if(locationId is{} id)stocks=stocks.Where(p=>read.StockLocations.Any(l=>(l.Id==id||l.ParentId==id)&&l.Id==p.LocationId));
        return stocks.GroupBy(p=>p.BatchId!.Value).Select(g=>new{Id=g.Key,Quantity=g.Sum(p=>p.BaseQuantity)}).ToDictionaryAsync(g=>g.Id,g=>g.Quantity,ct);
    });
    public async Task<LocationStock[]> StockAsync(int productId,int? batchId=null,CancellationToken ct=default)
    {
        var options=new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(db.Database.GetConnectionString());
        if(options.DataSource==":memory:"||options.Mode==Microsoft.Data.Sqlite.SqliteOpenMode.Memory)return await StockCoreAsync(productId,batchId,ct);
        await using var read=new PosDbContext(new DbContextOptionsBuilder<PosDbContext>().UseSqlite(options.ToString()).Options);
        return await new StockLocationService(read,uow,auth).StockCoreAsync(productId,batchId,ct);
    }
    private async Task<LocationStock[]> StockCoreAsync(int productId,int? batchId,CancellationToken ct)
    {
        var locations=await db.StockLocations.AsNoTracking().ToDictionaryAsync(l=>l.Id,ct);
        string Label(StockLocation l)=>l.ParentId is{} parent&&locations.TryGetValue(parent,out var rack)?rack.Name+" / "+l.Name:l.Name;
        return (await db.StockPlacements.AsNoTracking().Where(p=>p.ProductId==productId&&(batchId==null||p.BatchId==batchId)&&p.BaseQuantity>0).ToArrayAsync(ct)).Select(p=>new LocationStock(p.LocationId,Label(locations[p.LocationId]),p.BaseQuantity)).ToArray();
    }
    public Task<StockLocation> SaveLocationAsync(string name,int? parentId=null,int? id=null,CancellationToken ct=default)
    {
        auth.RequireAdmin();return uow.ExecuteInTransactionAsync(async token=>
        {
            if(parentId is{} parent&& !await db.StockLocations.AnyAsync(l=>l.Id==parent&&l.ParentId==null&&l.IsActive,token))throw new DomainException("Pharmacy_SelectRack");
            if(await db.StockLocations.AnyAsync(l=>l.ParentId==parentId&&l.Name.ToLower()==name.Trim().ToLower()&&l.Id!=id,token))throw new DomainException("Pharmacy_DuplicateLocation");
            var location=id is{} existing?await db.StockLocations.SingleAsync(l=>l.Id==existing,token):new StockLocation(name,parentId);location.Rename(name);if(id is null)db.StockLocations.Add(location);await db.SaveChangesAsync(token);return location;
        },ct);
    }
    public Task AssignOpeningAsync(int productId,int? batchId,int locationId,long quantity,string reason,CancellationToken ct=default)
    {
        auth.RequireAdmin();return uow.ExecuteInTransactionAsync(async token=>
        {
            if(quantity<=0||string.IsNullOrWhiteSpace(reason)||reason.Length>1000)throw new DomainException("Pharmacy_ResolutionRequired");
            var p=await db.Products.Include(p=>p.Batches).SingleAsync(p=>p.Id==productId,token);if(!p.StockTracked)throw new DomainException("Pharmacy_VerifyStockFirst");
            var available=batchId is{} batch?p.Batches.Single(b=>b.Id==batch).BaseQuantity:p.TrackExpiry?throw new DomainException("Pharmacy_TraceabilityRequired"):p.BaseStockQuantity;
            var assigned=await db.StockPlacements.Where(s=>s.ProductId==productId&&s.BatchId==batchId).SumAsync(s=>s.BaseQuantity,token);if(quantity>available-assigned)throw new DomainException("Pharmacy_LocationStockShortage");
            await PlaceReceivedAsync(productId,batchId,quantity,locationId,token);db.StockMovements.Add(new StockMovement(p.Id,p.Name,0,StockMovementKind.ManualAdjustment,DateTime.Now,"Location assignment: "+reason));await db.SaveChangesAsync(token);return true;
        },ct);
    }
    public async Task<string?> AllocateSaleAsync(Product product,int? batchId,long quantity,int? locationId,CancellationToken ct)
    {
        var places=await db.StockPlacements.Where(s=>s.ProductId==product.Id&&s.BatchId==batchId&&s.BaseQuantity>0).ToArrayAsync(ct);
        var before=(batchId is{} batch?product.Batches.Single(b=>b.Id==batch).BaseQuantity:product.BaseStockQuantity)+quantity;
        if(locationId==0){if(quantity>before-places.Sum(p=>p.BaseQuantity))throw new DomainException("Pharmacy_LocationStockShortage");return null;}
        if(places.Length==0&&locationId is null)return null;
        var place=locationId is{} id?places.SingleOrDefault(p=>p.LocationId==id):places.Length==1&&before==places[0].BaseQuantity&&quantity<=places[0].BaseQuantity?places[0]:null;
        if(place is null)throw new DomainException("Pharmacy_SelectStockLocation");
        var location=await db.StockLocations.SingleAsync(l=>l.Id==place.LocationId,ct);if(!location.IsActive)throw new DomainException("Pharmacy_SelectStockLocation");place.Adjust(-quantity);return await LabelAsync(location,ct);
    }
    public async Task<string?> PlaceReceivedAsync(int productId,int? batchId,long quantity,int? locationId,CancellationToken ct)
    {
        if(locationId is null or 0)return null;
        if(quantity<=0)throw new DomainException("Pharmacy_LocationStockShortage");
        var location=await db.StockLocations.SingleOrDefaultAsync(l=>l.Id==locationId&&l.IsActive,ct)??throw new DomainException("Pharmacy_SelectStockLocation");
        if(batchId is{} batch&&!await db.InventoryBatches.AnyAsync(b=>b.Id==batch&&b.ProductId==productId,ct))throw new DomainException("Pharmacy_TraceabilityRequired");
        var physical=batchId is{} batchKey?await db.InventoryBatches.Where(b=>b.Id==batchKey).Select(b=>b.BaseQuantity).SingleAsync(ct):await db.Products.Where(p=>p.Id==productId&&p.StockTracked&&!p.TrackExpiry).Select(p=>p.BaseStockQuantity).SingleAsync(ct);
        var persisted=await db.StockPlacements.Where(p=>p.ProductId==productId&&p.BatchId==batchId).ToArrayAsync(ct);
        var all=persisted.Concat(db.StockPlacements.Local.Where(p=>p.ProductId==productId&&p.BatchId==batchId)).DistinctBy(p=>p.AllocationKey).ToArray();
        if(quantity>physical-all.Sum(p=>p.BaseQuantity))throw new DomainException("Pharmacy_LocationStockShortage");
        var key=$"{productId}:{batchId??0}:{locationId}";var placement=all.SingleOrDefault(p=>p.AllocationKey==key);
        if(placement is null){placement=new StockPlacement(productId,batchId,locationId.Value);db.StockPlacements.Add(placement);}placement.Adjust(quantity);return await LabelAsync(location,ct);
    }
    private async Task<string> LabelAsync(StockLocation location,CancellationToken ct)=>location.ParentId is{} parent?(await db.StockLocations.SingleAsync(l=>l.Id==parent,ct)).Name+" / "+location.Name:location.Name;
}
