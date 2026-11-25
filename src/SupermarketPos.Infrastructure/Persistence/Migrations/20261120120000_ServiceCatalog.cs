using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace SupermarketPos.Infrastructure.Persistence.Migrations;
[DbContext(typeof(PosDbContext))][Migration(MigrationId)]
public sealed class ServiceCatalog : Migration
{
    public const string MigrationId="20261120120000_ServiceCatalog";
    protected override void Up(MigrationBuilder m)
    {
        foreach(var name in new[]{"DefaultCustomerFee","DefaultProviderFee","DefaultCommission"})m.AddColumn<decimal>(name,"ServiceProviders",type:"TEXT",nullable:false,defaultValue:0m);
        m.AddColumn<decimal>("LoadCostRate","ServiceProviders",type:"TEXT",nullable:false,defaultValue:1m);
        m.AddColumn<string>("AccountType","MobileServicePackages",type:"TEXT",nullable:false,defaultValue:"Unspecified");
        // Only carry forward an explicit legacy account type. An unknown type must
        // be classified by the owner, rather than sold under an invented filter.
        m.Sql("UPDATE MobileServicePackages SET AccountType = CASE lower(trim(Kind)) WHEN 'postpaid' THEN 'Postpaid' WHEN 'post-paid' THEN 'Postpaid' WHEN 'prepaid' THEN 'Prepaid' WHEN 'pre-paid' THEN 'Prepaid' ELSE 'Unspecified' END;");
        foreach(var name in new[]{"PackageIdentifier","Benefits"})m.AddColumn<string>(name,"MobileServicePackages",type:"TEXT",nullable:false,defaultValue:"");
        foreach(var name in new[]{"CustomerFee","ProviderFee"})m.AddColumn<decimal>(name,"MobileServicePackages",type:"TEXT",nullable:false,defaultValue:0m);
        foreach(var name in new[]{"PackageIdentifier","AccountType","PackageBenefits","PackageValidity"})m.AddColumn<string>(name,"ServiceTransactions",type:"TEXT",nullable:false,defaultValue:"");
    }
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Restore the matching application and data backup.");
}
