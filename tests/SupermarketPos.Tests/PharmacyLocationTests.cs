using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SupermarketPos.Application.Services;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Infrastructure.Persistence;
namespace SupermarketPos.Tests;
public sealed class PharmacyLocationTests
{
    private static async Task<(StockLocationService Locations,SaleService Sales,User User,Product Product)> Setup(TestDb db)
    {
        var hash=new BCryptPasswordHasher();var user=new User("owner",hash.Hash("test-owner-123"),"Owner",UserRole.Admin);db.Context.Users.Add(user);db.Context.Categories.Add(new Category("Medicine"));await db.Context.SaveChangesAsync();
        var p=new Product("","Medicine",10,0,Unit.Piece,30,1);db.Context.Products.Add(p);await db.Context.SaveChangesAsync();var auth=new AuthService(new UserRepository(db.Context),hash);await auth.LoginAsync("owner","test-owner-123");var work=new UnitOfWork(db.Context);var locations=new StockLocationService(db.Context,work,auth);
        return(locations,new SaleService(new ProductRepository(db.Context),new SaleRepository(db.Context),new ReceiptNumberGenerator(db.Context),work,locations:locations),user,p);
    }
    [Fact]public async Task LocationBarcodesStayStableAndShelvesCannotContainMoreShelves()
    {
        using var db=new TestDb();var (locations,_,_,p)=await Setup(db);var rack=await locations.SaveLocationAsync("Rack A");var barcode=rack.Barcode;Assert.StartsWith("POSLOC:",barcode);var shelf=await locations.SaveLocationAsync("Shelf 1",rack.Id);await locations.SaveLocationAsync("Renamed",id:rack.Id);Assert.Equal(barcode,(await locations.FindAsync(barcode))!.Barcode);Assert.Throws<DomainException>(()=>p.SetBarcode(barcode));await Assert.ThrowsAsync<DomainException>(()=>locations.SaveLocationAsync("Invalid child",shelf.Id));Assert.Equal(2,await db.Context.StockLocations.CountAsync());
    }
    [Fact]public async Task MultiLocationSaleRequiresSourceAndRollsBackOnShortage()
    {
        using var db=new TestDb();var (locations,sales,user,p)=await Setup(db);var a=await locations.SaveLocationAsync("A");var b=await locations.SaveLocationAsync("B");await locations.AssignOpeningAsync(p.Id,null,a.Id,10,"Count A");await locations.AssignOpeningAsync(p.Id,null,b.Id,20,"Count B");
        var cart=new Cart();cart.AddOrIncrement(p,2);await Assert.ThrowsAsync<DomainException>(()=>sales.CheckoutAsync(cart,new CashPayment(20,20),user));Assert.Empty(db.Context.Sales);Assert.Equal(30,(await db.Context.Products.SingleAsync()).StockQuantity);
        p=await db.Context.Products.SingleAsync();cart.Clear();cart.AddOrIncrement(p,2,locationId:a.Id);var sale=await sales.CheckoutAsync(cart,new CashPayment(20,20),user);Assert.Equal("A",sale.Lines.Single().StockLocation);Assert.Equal(28,p.StockQuantity);Assert.Equal(8,(await locations.StockAsync(p.Id)).Single(l=>l.Id==a.Id).BaseQuantity);
        cart.Clear();cart.AddOrIncrement(p,9,locationId:a.Id);await Assert.ThrowsAsync<DomainException>(()=>sales.CheckoutAsync(cart,new CashPayment(90,90),user));Assert.Equal(28,(await db.Context.Products.SingleAsync()).StockQuantity);Assert.Single(db.Context.Sales);
    }
    [Fact]public async Task ReceivingOneOuterPackCreatesThirtyBaseUnitsInSelectedShelfOnlyOnce()
    {
        using var db=new TestDb();var (locations,_,user,p)=await Setup(db);p.SetStock(0);p.ConfigurePackaging("pack",30,null,null,250,null);p.ConfigureMedicineUnits("tablet",10,90);await db.Context.SaveChangesAsync();var shelf=await locations.SaveLocationAsync("Receiving shelf");var purchases=new PurchaseService(new ProductRepository(db.Context),new PurchaseRepository(db.Context),new UnitOfWork(db.Context),locations:locations);var supplier=await purchases.AddSupplierAsync("Supplier");var confirmation=Guid.NewGuid();var item=new SupermarketPos.Application.Abstractions.PurchaseItem(p.Id,1,180,MedicineUnit:"pack",LocationId:shelf.Id);
        var purchase=await purchases.ReceiveAsync(supplier.Id,"INV1",DateTime.Today,null,[item],user,confirmation);await purchases.ReceiveAsync(supplier.Id,"INV1",DateTime.Today,null,[item],user,confirmation);Assert.Equal(30,p.StockQuantity);Assert.Equal(30,(await locations.StockAsync(p.Id)).Single().BaseQuantity);Assert.Equal(6,p.PurchaseCost);Assert.Equal("Receiving shelf",purchase.Lines.Single().StockLocation);Assert.Single(db.Context.Purchases);
    }
    [Fact]public async Task DefaultShelfDoesNotInventStockAndPartialAssignmentNeedsExplicitSaleSource()
    {
        using var db=new TestDb();var (locations,sales,user,p)=await Setup(db);var rack=await locations.SaveLocationAsync("Rack");var shelf=await locations.SaveLocationAsync("Shelf",rack.Id);p.SetDefaultLocation(shelf.Id);p.SetMedicineDetails("Ingredient","10 mg","Tablet","Local company");await db.Context.SaveChangesAsync();
        Assert.Empty(await locations.StockAsync(p.Id));Assert.Equal(30,p.StockQuantity);Assert.Contains(p.Id,await locations.ProductIdsAtAsync(rack.Id));
        await locations.AssignOpeningAsync(p.Id,null,shelf.Id,10,"Counted ten on this shelf");Assert.Equal(10,(await locations.QuantitiesAtAsync(rack.Id))[p.Id]);Assert.Equal(10,(await locations.QuantitiesAtAsync(null))[p.Id]);
        var cart=new Cart();cart.AddOrIncrement(p,1);await Assert.ThrowsAsync<DomainException>(()=>sales.CheckoutAsync(cart,new CashPayment(10,10),user));Assert.Empty(db.Context.Sales);
        Assert.Equal("Ingredient",(await db.Context.Products.AsNoTracking().SingleAsync()).MedicineGeneric);
    }
    [Fact]public async Task ChangedMedicinePackSizeRejectsReceivingDraftWithoutChangingStock()
    {
        using var db=new TestDb();var (locations,_,user,p)=await Setup(db);p.ConfigurePackaging("pack",30,null,null,250,null);p.ConfigureMedicineUnits("tablet",10,90);await db.Context.SaveChangesAsync();
        var purchases=new PurchaseService(new ProductRepository(db.Context),new PurchaseRepository(db.Context),new UnitOfWork(db.Context),locations:locations);var supplier=await purchases.AddSupplierAsync("Supplier");
        var draft=new SupermarketPos.Application.Abstractions.PurchaseItem(p.Id,1,180,MedicineUnit:"pack",ExpectedUnitSize:30,ExpectedUnit:Unit.Piece);p.ConfigurePackaging("pack",20,null,null,200,null);await db.Context.SaveChangesAsync();
        await Assert.ThrowsAsync<DomainException>(()=>purchases.ReceiveAsync(supplier.Id,"Changed pack",DateTime.Today,null,[draft],user,Guid.NewGuid()));Assert.Equal(30,(await db.Context.Products.SingleAsync()).StockQuantity);Assert.Empty(db.Context.Purchases);
    }
    [Fact]public async Task RackBatchTotalsExcludeQuantitiesOnOtherShelves()
    {
        using var db=new TestDb();var (locations,_,_,p)=await Setup(db);p.SetExpiryTracking(true);var a=p.RecordBatch("A",DateOnly.FromDateTime(DateTime.Today.AddMonths(2)),10,null,DateTime.Now,true);var b=p.RecordBatch("B",DateOnly.FromDateTime(DateTime.Today.AddMonths(3)),20,null,DateTime.Now,true);await db.Context.SaveChangesAsync();
        var rack=await locations.SaveLocationAsync("Rack A");var shelf=await locations.SaveLocationAsync("Shelf A1",rack.Id);var other=await locations.SaveLocationAsync("Rack B");await locations.AssignOpeningAsync(p.Id,a.Id,shelf.Id,10,"Counted A1");await locations.AssignOpeningAsync(p.Id,b.Id,other.Id,15,"Counted B");
        var scoped=await locations.BatchQuantitiesAtAsync(rack.Id);Assert.Equal(10,scoped[a.Id]);Assert.False(scoped.ContainsKey(b.Id));Assert.Equal(15,(await locations.BatchQuantitiesAtAsync(null))[b.Id]);Assert.Equal(30,p.StockQuantity);
    }
    [Fact]public async Task TransfersPreservePhysicalTotalsRejectShortagesAndApplyEachConfirmationOnce()
    {
        using var db=new TestDb();var (locations,_,_,p)=await Setup(db);var a=await locations.SaveLocationAsync("A");var b=await locations.SaveLocationAsync("B");var confirmation=Guid.NewGuid();
        await locations.TransferAsync(p.Id,null,0,a.Id,20,"Counted on shelf",confirmation);await locations.TransferAsync(p.Id,null,0,a.Id,20,"Counted on shelf",confirmation);Assert.Equal(20,(await locations.StockAsync(p.Id)).Single().BaseQuantity);Assert.Equal(30,p.StockQuantity);Assert.Single(await db.Context.StockMovements.Where(m=>m.Kind==StockMovementKind.StockTransfer).ToArrayAsync());
        await Assert.ThrowsAsync<DomainException>(()=>locations.TransferAsync(p.Id,null,0,a.Id,21,"Changed",confirmation));await Assert.ThrowsAsync<DomainException>(()=>locations.TransferAsync(p.Id,null,a.Id,b.Id,21,"Too many",Guid.NewGuid()));
        await locations.TransferAsync(p.Id,null,a.Id,b.Id,7,"Moved seven",Guid.NewGuid());var places=await locations.StockAsync(p.Id);Assert.Equal(13,places.Single(l=>l.Id==a.Id).BaseQuantity);Assert.Equal(7,places.Single(l=>l.Id==b.Id).BaseQuantity);Assert.Equal(30,(await db.Context.Products.SingleAsync()).StockQuantity);
        await locations.TransferAsync(p.Id,null,b.Id,0,7,"Remove shelf allocation",Guid.NewGuid());Assert.Equal(13,(await locations.StockAsync(p.Id)).Single().BaseQuantity);Assert.Equal(30,(await db.Context.Products.SingleAsync()).StockQuantity);
    }
    [Fact]public async Task BatchTransfersKeepExpiryAndHoldAndDeactivationNeverRemovesHistory()
    {
        using var db=new TestDb();var (locations,_,_,p)=await Setup(db);p.SetExpiryTracking(true);var batch=p.RecordBatch("HELD",DateOnly.FromDateTime(DateTime.Today.AddDays(-1)),30,null,DateTime.Now,true);batch.SetHold("Quarantine");await db.Context.SaveChangesAsync();var rack=await locations.SaveLocationAsync("Rack");var shelf=await locations.SaveLocationAsync("Shelf",rack.Id);var other=await locations.SaveLocationAsync("Other");
        await Assert.ThrowsAsync<DomainException>(()=>locations.DeactivateAsync(rack.Id));await locations.TransferAsync(p.Id,batch.Id,0,shelf.Id,30,"Quarantine location",Guid.NewGuid());await Assert.ThrowsAsync<DomainException>(()=>locations.DeactivateAsync(shelf.Id));await locations.TransferAsync(p.Id,batch.Id,shelf.Id,other.Id,30,"Move held batch",Guid.NewGuid());
        var saved=await db.Context.InventoryBatches.SingleAsync();Assert.Equal(30,saved.BaseQuantity);Assert.Equal(DateOnly.FromDateTime(DateTime.Today.AddDays(-1)),saved.ExpiryDate);Assert.Equal("Quarantine",saved.HoldReason);p=await db.Context.Products.SingleAsync();p.SetDefaultLocation(shelf.Id);await db.Context.SaveChangesAsync();await Assert.ThrowsAsync<DomainException>(()=>locations.DeactivateAsync(shelf.Id));p=await db.Context.Products.SingleAsync();p.SetDefaultLocation(null);await db.Context.SaveChangesAsync();await locations.DeactivateAsync(shelf.Id);await locations.DeactivateAsync(rack.Id);Assert.Equal(3,await db.Context.StockLocations.CountAsync());Assert.Single(await locations.LocationsAsync());Assert.Equal(2,await db.Context.StockMovements.CountAsync(m=>m.Kind==StockMovementKind.StockTransfer));
    }
    [Theory][InlineData(false)][InlineData(true)]public void RackLabelsRoundTripTheirReservedCode(bool qr)
    {
        var location=new StockLocation("Rack label");var png=qr?SupermarketPos.Infrastructure.Documents.BarcodeImage.TryRenderQr(location.Barcode):SupermarketPos.Infrastructure.Documents.BarcodeImage.TryRenderCode128(location.Barcode);Assert.NotNull(png);
        using var bitmap=SkiaSharp.SKBitmap.Decode(png);var pixels=new byte[bitmap.ByteCount];System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(),pixels,0,pixels.Length);
        var reader=new ZXing.BarcodeReaderGeneric();var result=reader.Decode(new ZXing.RGBLuminanceSource(pixels,bitmap.Width,bitmap.Height,ZXing.RGBLuminanceSource.BitmapFormat.BGRA32));Assert.NotNull(result);Assert.Equal(location.Barcode,result.Text);
        Assert.Null(SupermarketPos.Infrastructure.Documents.BarcodeImage.TryRenderEan13("POSLOC:1234567890123"));
    }
    [Fact]public async Task FullMigrationCreatesCatalogSearchAndLocationSchema()
    {
        using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();await using var db=new PosDbContext(new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options);await db.Database.MigrateAsync();Assert.False(db.Database.HasPendingModelChanges());Assert.Empty(await db.StockLocations.ToArrayAsync());Assert.Empty(await db.MedicineReferences.ToArrayAsync());using var command=connection.CreateCommand();command.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE name='MedicineReferenceSearch'";Assert.Equal(1L,await command.ExecuteScalarAsync());
    }
}
