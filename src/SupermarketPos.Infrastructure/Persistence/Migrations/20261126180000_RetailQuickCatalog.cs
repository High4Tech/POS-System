using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace SupermarketPos.Infrastructure.Persistence.Migrations;
[DbContext(typeof(PosDbContext))]
[Migration(RetailQuickCatalog.MigrationId)]
public sealed class RetailQuickCatalog:Migration
{
    public const string MigrationId="20261126180000_RetailQuickCatalog";
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<string>("Kind","Products",type:"TEXT",nullable:false,defaultValue:"labeled");
        m.AddColumn<string>("ServiceType","Products",type:"TEXT",nullable:false,defaultValue:"SERVICE");
        m.AddColumn<string>("CatalogKey","Products",type:"TEXT",nullable:false,defaultValue:"");
        m.AddColumn<bool>("QuickEntry","Products",type:"INTEGER",nullable:false,defaultValue:false);
        foreach(var name in new[]{"ServiceProviderId","ServicePackageId"})m.AddColumn<int>(name,"Products",type:"INTEGER",nullable:true);
        m.CreateIndex("IX_Products_CatalogKey","Products","CatalogKey",unique:true,filter:"\"CatalogKey\" <> ''");
        foreach(var table in new[]{"ServiceProviders","MobileServicePackages"})
            m.AddColumn<string>("CatalogKey",table,type:"TEXT",nullable:false,defaultValue:"");
        m.Sql("UPDATE Products SET Kind='loose' WHERE Unit<>0;");
    }
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Restore a matching backup instead.");
}
