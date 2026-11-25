using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace SupermarketPos.Infrastructure.Persistence.Migrations;
[DbContext(typeof(PosDbContext))][Migration(MigrationId)]
public sealed class OptionalPharmacySetup:Migration
{
    public const string MigrationId="20261121100000_OptionalPharmacySetup";
    protected override void Up(MigrationBuilder m)=>m.AddColumn<bool>("SellingUnitConfigured","Products",nullable:false,defaultValue:true);
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Restore the matching application and data backup.");
}
