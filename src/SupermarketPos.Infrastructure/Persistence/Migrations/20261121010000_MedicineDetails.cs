using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace SupermarketPos.Infrastructure.Persistence.Migrations;
[DbContext(typeof(PosDbContext))][Migration(MigrationId)]
public sealed class MedicineDetails:Migration
{
    public const string MigrationId="20261121010000_MedicineDetails";
    protected override void Up(MigrationBuilder m){foreach(var field in new[]{"MedicineGeneric","MedicineStrength","MedicineForm","MedicineManufacturer"})m.AddColumn<string>(field,"Products",nullable:false,defaultValue:"");}
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Restore the matching application and data backup.");
}
