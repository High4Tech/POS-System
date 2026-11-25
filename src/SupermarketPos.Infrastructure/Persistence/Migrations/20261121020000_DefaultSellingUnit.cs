using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace SupermarketPos.Infrastructure.Persistence.Migrations;
[DbContext(typeof(PosDbContext))][Migration(MigrationId)]
public sealed class DefaultSellingUnit:Migration
{
    public const string MigrationId="20261121020000_DefaultSellingUnit";
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<string>("DefaultSellingUnitCode","Products",nullable:false,defaultValue:"");
        m.AddColumn<Guid>("ConfirmationId","StockMovements",nullable:true);
        m.CreateIndex("IX_StockMovements_ConfirmationId","StockMovements","ConfirmationId",unique:true);
    }
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Restore the matching application and data backup.");
}
