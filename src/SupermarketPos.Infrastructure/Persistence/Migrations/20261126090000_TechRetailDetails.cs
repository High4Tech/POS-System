using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SupermarketPos.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PosDbContext))]
[Migration(TechRetailDetails.MigrationId)]
public sealed class TechRetailDetails : Migration
{
    public const string MigrationId="20261126090000_TechRetailDetails";
    protected override void Up(MigrationBuilder migration)
    {
        foreach(var name in new[]{"Brand","Model","Sku"})
            migration.AddColumn<string>(name,"Products",type:"TEXT",nullable:false,defaultValue:"");
    }
    protected override void Down(MigrationBuilder migration) => throw new NotSupportedException("Restore a matching backup instead.");
}
