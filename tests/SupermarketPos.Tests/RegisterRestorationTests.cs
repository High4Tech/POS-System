using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Infrastructure.Persistence;

namespace SupermarketPos.Tests;
public sealed class RegisterRestorationTests
{
    [Fact]public void InlineUnitChangesKeepDisplayedQuantityAndIndependentPricesWithoutMutatingStockOrMasterPrice()
    {
        var product=new Product("QA","Medicine",12,0,Unit.Piece,100,1);product.ConfigurePackaging("pack",30,null,null,330,null);product.ConfigureMedicineUnits("tablet",10,115);product.SetDefaultSellingUnit("strip");
        var line=new CartLine(product,2,medicineUnit:"tablet");line.SetUnitPrice(14);Assert.Equal(28,line.GrossTotal);line.SetSellingUnit("strip");Assert.Equal(2,line.DisplayQuantity);Assert.Equal(20,line.Quantity);Assert.Equal(115,line.GrossUnitPrice);Assert.Equal(230,line.GrossTotal);Assert.False(line.HasPriceOverride);
        line.SetSellingUnit("pack");Assert.Equal(60,line.Quantity);Assert.Equal(660,line.GrossTotal);Assert.Equal(100,product.StockQuantity);Assert.Equal(12,product.UnitPrice);Assert.Equal("strip",product.DefaultSellingUnit);
    }
    [Fact]public void MissingUnitPriceRemainsUnpricedInsteadOfInventingAPackPrice()
    {
        var p=new Product("QA","Medicine",12,0,Unit.Piece,30,1);p.ConfigurePackaging("pack",30,null,null,null,null);p.ConfigureMedicineUnits("tablet",10,null);var line=new CartLine(p,1,medicineUnit:"tablet");line.SetSellingUnit("strip");Assert.Equal(0,line.GrossUnitPrice);Assert.Equal(10,line.Quantity);line.SetUnitPrice(110);Assert.Equal(110,line.GrossTotal);Assert.Null(p.StripSellingPrice);
    }
    [Fact]public void FailedSerializedUnitChangeLeavesTheOriginalLineIntact()
    {
        using var db=new TestDb();db.Context.Categories.Add(new Category("Medicine"));db.Context.SaveChanges();var p=new Product("QA","Medicine",12,0,Unit.Piece,30,1);p.ConfigurePackaging("pack",30,null,null,330,null);p.ConfigureMedicineUnits("tablet",10,115);p.SetExpiryTracking(true);db.Context.Products.Add(p);db.Context.SaveChanges();var batch=p.RecordBatch("LOT",DateOnly.FromDateTime(DateTime.Today.AddMonths(3)),30,null,DateTime.Now,true);db.Context.SaveChanges();var line=new CartLine(p,1,batch,"SERIAL",medicineUnit:"tablet");Assert.Throws<DomainException>(()=>line.SetSellingUnit("strip"));Assert.Equal(1,line.Quantity);Assert.Equal("tablet",line.UnitCode);Assert.Equal(12,line.GrossUnitPrice);
    }
    [Fact]public async Task FreshSchemaCreatesNoDemoStockAndLegacyUpgradePreservesExistingProducts()
    {
        using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();await using var db=new PosDbContext(new DbContextOptionsBuilder<PosDbContext>().UseSqlite(connection).Options);await db.GetService<IMigrator>().MigrateAsync("20261117140000_AddPurchases");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Categories(Name) VALUES('Real category'); INSERT INTO Products(Barcode,Name,UnitPrice,VatRate,Unit,StockQuantity,CategoryId) VALUES('REAL','Real product','42.0','0.0',0,'37.0',1);");
        await db.Database.MigrateAsync();Assert.False(db.Database.HasPendingModelChanges());var product=await db.Products.SingleAsync();Assert.Equal("Real product",product.Name);Assert.Equal(37,product.StockQuantity);Assert.Equal(42,product.UnitPrice);Assert.Equal("Real category",(await db.Categories.SingleAsync()).Name);
        using var other=new SqliteConnection("Data Source=:memory:");await other.OpenAsync();await using var fresh=new PosDbContext(new DbContextOptionsBuilder<PosDbContext>().UseSqlite(other).Options);await fresh.Database.MigrateAsync();Assert.Empty(await fresh.Products.ToArrayAsync());Assert.Empty(await fresh.Categories.ToArrayAsync());
    }
}
