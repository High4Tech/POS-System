using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Common;
using SupermarketPos.Infrastructure.Persistence;

internal static partial class Program
{
    private static async Task SeedUiCatalogFixture(IServiceProvider sp)
    {
        var db=sp.GetRequiredService<PosDbContext>();Assert(!await db.Products.AnyAsync(),"Fresh application schema contains no invented store products or stock");
        var type=typeof(PosDbContext).Assembly.GetType("SupermarketPos.Infrastructure.Persistence.Migrations.TechCatalog")!;
        var groups=((string Category,(string Name,int Price)[] Products)[])type.GetField("Groups",BindingFlags.Public|BindingFlags.Static)!.GetValue(null)!;var barcode=type.GetMethod("Barcode",BindingFlags.Public|BindingFlags.Static)!;var sequence=0;
        // Explicit test data reproduces the old 100-product benchmark; the production migration no longer seeds it.
        foreach(var (name,items) in groups)
        {
            var category=new Category(name);db.Categories.Add(category);await db.SaveChangesAsync();
            foreach(var (product,price) in items){sequence++;db.Products.Add(new Product((string)barcode.Invoke(null,[sequence])!,product,price,.2m,Unit.Piece,name=="Mobile Phones"?3+sequence%6:12+sequence*7%27,category.Id));}
        }
        await db.SaveChangesAsync();
    }
}
