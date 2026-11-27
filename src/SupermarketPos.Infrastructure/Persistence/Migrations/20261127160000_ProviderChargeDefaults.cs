using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace SupermarketPos.Infrastructure.Persistence.Migrations;
[DbContext(typeof(PosDbContext))]
[Migration(ProviderChargeDefaults.MigrationId)]
public sealed class ProviderChargeDefaults:Migration
{
    public const string MigrationId="20261127160000_ProviderChargeDefaults";
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<string>("CustomerFeeMode","ServiceProviders",type:"TEXT",nullable:false,defaultValue:"FIXED");
        m.AddColumn<decimal>("CustomerFeePercent","ServiceProviders",type:"TEXT",nullable:false,defaultValue:0m);
        m.AddColumn<string>("AccountNumberLengths","ServiceProviders",type:"TEXT",nullable:false,defaultValue:"");
    }
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Restore a matching backup instead.");
}
