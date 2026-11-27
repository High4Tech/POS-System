using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace SupermarketPos.Infrastructure.Persistence.Migrations;
[DbContext(typeof(PosDbContext))]
[Migration(OptionalPackageCost.MigrationId)]
public sealed class OptionalPackageCost:Migration
{
    public const string MigrationId="20261127090000_OptionalPackageCost";
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<bool>("ProviderCostKnown","MobileServicePackages",type:"INTEGER",nullable:false,defaultValue:true);
        m.AddColumn<bool>("ProviderCostKnown","ServiceTransactions",type:"INTEGER",nullable:false,defaultValue:true);
    }
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Restore a matching backup instead.");
}
