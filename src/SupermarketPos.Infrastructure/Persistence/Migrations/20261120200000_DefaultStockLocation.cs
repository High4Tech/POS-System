using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace SupermarketPos.Infrastructure.Persistence.Migrations;
[DbContext(typeof(PosDbContext))][Migration(MigrationId)]
public sealed class DefaultStockLocation:Migration
{
    public const string MigrationId="20261120200000_DefaultStockLocation";
    protected override void Up(MigrationBuilder m){m.Sql("ALTER TABLE Products ADD COLUMN DefaultLocationId INTEGER NULL REFERENCES StockLocations(Id) ON DELETE RESTRICT;");m.CreateIndex("IX_Products_DefaultLocationId","Products","DefaultLocationId");}
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Restore the matching application and data backup.");
}
