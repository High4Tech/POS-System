using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Application.Services;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Infrastructure.Persistence;

namespace SupermarketPos.Tests;
public sealed class MedicineCatalogTests
{
    private static async Task<MedicineCatalogService> Catalog(TestDb db)
    {
        var hasher=new BCryptPasswordHasher();db.Context.Users.Add(new User("catalog-owner",hasher.Hash("TestPassword123"),"Owner",UserRole.Admin));await db.Context.SaveChangesAsync();
        var auth=new AuthService(new UserRepository(db.Context),hasher);await auth.LoginAsync("catalog-owner","TestPassword123");return new(db.Context,auth);
    }
    [Fact]public async Task SuppliedCatalogImportsAllRowsIdempotentlyWithoutCreatingInventory()
    {
        using var db=new TestDb();var catalog=await Catalog(db);Assert.True(MedicineCatalogService.BuiltinAvailable);
        Assert.Equal(10068,await catalog.ImportBuiltinAsync());Assert.Equal(10068,await catalog.ImportBuiltinAsync());Assert.Equal(10068,await db.Context.MedicineReferences.CountAsync());
        Assert.Empty(db.Context.Products);Assert.Empty(db.Context.Purchases);Assert.Empty(db.Context.InventoryBatches);Assert.Empty(db.Context.StockMovements);
        var timer=Stopwatch.StartNew();var rows=await catalog.SearchAsync("moxiflox",20);timer.Stop();Assert.NotEmpty(rows);Assert.True(rows.Count<=20);Assert.All(rows,r=>{Assert.Null(r.Price);Assert.Null(r.Stock);Assert.Null(r.ProductId);Assert.Equal("CatalogOnly",r.Status);Assert.Empty(r.Barcode);});Assert.True(timer.Elapsed<TimeSpan.FromSeconds(2));
        var groups=await db.Context.MedicineReferences.GroupBy(m=>m.RegistrationNumber).Where(g=>g.Count()>1).Select(g=>g.Count()).ToArrayAsync();Assert.NotEmpty(groups);
    }
    [Fact]public async Task ConfiguredExactMedicineRanksFirstAndHasRealStockOnly()
    {
        using var db=new TestDb();var catalog=await Catalog(db);var csv=MedicineCatalogService.Header+"\n1,001,Alpha 10mg,Generic A,Company\n2,001,Alpha 20mg,Generic A,Company\n";await catalog.ImportFileAsync(Encoding.UTF8.GetBytes(csv),"test.csv");
        var refs=await db.Context.MedicineReferences.OrderBy(m=>m.Id).ToArrayAsync();Assert.Equal(2,refs.Length);var cat=new Category("Medicines");db.Context.Categories.Add(cat);await db.Context.SaveChangesAsync();var p=new Product("test-code","Alpha 20mg",125,0,Unit.Piece,7,cat.Id);p.LinkMedicine(refs[1].Id);db.Context.Products.Add(p);await db.Context.SaveChangesAsync();
        var found=await catalog.SearchAsync("alpha");Assert.Equal(p.Id,found[0].ProductId);Assert.Equal(125,found[0].Price);Assert.Equal(7,found[0].Stock);Assert.Equal("InStock",found[0].Status);Assert.Equal(2,found.Count);
    }
    [Fact]public async Task BadImportRollsBackAndSearchTextCannotBecomeSqlOrFtsSyntax()
    {
        using var db=new TestDb();var catalog=await Catalog(db);var csv=MedicineCatalogService.Header+"\n1,1,Medicine,G,C\n1,1,Different Medicine,G,C\n";
        await Assert.ThrowsAsync<DomainException>(()=>catalog.ImportFileAsync(Encoding.UTF8.GetBytes(csv),"bad.csv"));Assert.Empty(db.Context.MedicineReferences);Assert.Empty(db.Context.CatalogImports);
        await catalog.ImportFileAsync(Encoding.UTF8.GetBytes(MedicineCatalogService.Header+"\n1,1,Medicine,G,C\n"),"valid.csv");Assert.Empty(await catalog.SearchAsync("\"; DROP TABLE Products; --"));Assert.True(await db.Context.MedicineReferences.AnyAsync());
    }
}
