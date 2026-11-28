using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace SupermarketPos.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PosDbContext))]
[Migration(RetailFinancialCompletion.MigrationId)]
public sealed class RetailFinancialCompletion:Migration
{
    public const string MigrationId="20261128180000_RetailFinancialCompletion";
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<string>("RequestFingerprint","Purchases",type:"TEXT",nullable:false,defaultValue:"");
        m.AddColumn<decimal>("PurchaseCost","SerializedUnits",type:"TEXT",nullable:true);
        m.AddColumn<int>("SaleCycle","SerializedUnits",type:"INTEGER",nullable:false,defaultValue:0);
        m.AddColumn<int>("SerialCycle","SaleLines",type:"INTEGER",nullable:false,defaultValue:0);
        m.DropIndex("IX_SaleLines_ProductId_SerialNumber","SaleLines");
        m.CreateIndex("IX_SaleLines_ProductId_SerialNumber","SaleLines",new[]{"ProductId","SerialNumber"},filter:"SerialNumber IS NOT NULL");
        m.CreateIndex("IX_SaleLines_ProductId_SerialNumber_SerialCycle","SaleLines",new[]{"ProductId","SerialNumber","SerialCycle"},unique:true,filter:"SerialNumber IS NOT NULL");
        m.AddColumn<decimal>("PurchaseCost","InventoryBatches",type:"TEXT",nullable:true);
        m.AddColumn<DateTime>("PostedAt","ServicePostings",type:"TEXT",nullable:false,defaultValue:DateTime.MinValue);
        m.Sql("UPDATE ServicePostings SET PostedAt=(SELECT OccurredAt FROM ServiceTransactions t WHERE t.Id=ServicePostings.TransactionId)");
        m.CreateIndex("IX_ServicePostings_PostedAt","ServicePostings","PostedAt");
        // Only an unambiguous original receiving cost can backfill a device snapshot.
        m.Sql("UPDATE SerializedUnits SET PurchaseCost=(SELECT MIN(p.UnitCost) FROM PurchaseLines p WHERE p.PurchaseId=SerializedUnits.PurchaseId AND p.ProductId=SerializedUnits.ProductId HAVING COUNT(DISTINCT p.UnitCost)=1) WHERE PurchaseId IS NOT NULL");
        m.CreateTable("CustomerCreditMovements",columns:t=>new{Id=t.Column<int>(type:"INTEGER",nullable:false).Annotation("Sqlite:Autoincrement",true),ConfirmationId=t.Column<Guid>(type:"TEXT",nullable:false),SaleReturnId=t.Column<int>(type:"INTEGER",nullable:false),TargetSaleId=t.Column<int>(type:"INTEGER",nullable:true),Amount=t.Column<decimal>(type:"TEXT",nullable:false),Method=t.Column<string>(type:"TEXT",nullable:false),Reference=t.Column<string>(type:"TEXT",nullable:false),Operator=t.Column<string>(type:"TEXT",nullable:false),RecordedAt=t.Column<DateTime>(type:"TEXT",nullable:false)},constraints:t=>{t.PrimaryKey("PK_CustomerCreditMovements",x=>x.Id);t.ForeignKey("FK_CustomerCreditMovements_SaleReturns_SaleReturnId",x=>x.SaleReturnId,"SaleReturns","Id",onDelete:ReferentialAction.Restrict);t.ForeignKey("FK_CustomerCreditMovements_Sales_TargetSaleId",x=>x.TargetSaleId,"Sales","Id",onDelete:ReferentialAction.Restrict);});
        m.CreateIndex("IX_CustomerCreditMovements_ConfirmationId","CustomerCreditMovements","ConfirmationId",unique:true);m.CreateIndex("IX_CustomerCreditMovements_SaleReturnId","CustomerCreditMovements","SaleReturnId");m.CreateIndex("IX_CustomerCreditMovements_TargetSaleId","CustomerCreditMovements","TargetSaleId");
        m.CreateTable("DeviceReturnReleases",columns:t=>new{Id=t.Column<int>(type:"INTEGER",nullable:false).Annotation("Sqlite:Autoincrement",true),ConfirmationId=t.Column<Guid>(type:"TEXT",nullable:false),SaleReturnId=t.Column<int>(type:"INTEGER",nullable:false),SaleLineId=t.Column<int>(type:"INTEGER",nullable:false),SerializedUnitId=t.Column<int>(type:"INTEGER",nullable:false),Inspection=t.Column<string>(type:"TEXT",nullable:false),Operator=t.Column<string>(type:"TEXT",nullable:false),ReleasedAt=t.Column<DateTime>(type:"TEXT",nullable:false)},constraints:t=>{t.PrimaryKey("PK_DeviceReturnReleases",x=>x.Id);t.ForeignKey("FK_DeviceReturnReleases_SaleReturns_SaleReturnId",x=>x.SaleReturnId,"SaleReturns","Id",onDelete:ReferentialAction.Restrict);t.ForeignKey("FK_DeviceReturnReleases_SaleLines_SaleLineId",x=>x.SaleLineId,"SaleLines","Id",onDelete:ReferentialAction.Restrict);t.ForeignKey("FK_DeviceReturnReleases_SerializedUnits_SerializedUnitId",x=>x.SerializedUnitId,"SerializedUnits","Id",onDelete:ReferentialAction.Restrict);});
        m.CreateIndex("IX_DeviceReturnReleases_ConfirmationId","DeviceReturnReleases","ConfirmationId",unique:true);m.CreateIndex("IX_DeviceReturnReleases_SaleLineId","DeviceReturnReleases","SaleLineId",unique:true);m.CreateIndex("IX_DeviceReturnReleases_SaleReturnId","DeviceReturnReleases","SaleReturnId");m.CreateIndex("IX_DeviceReturnReleases_SerializedUnitId","DeviceReturnReleases","SerializedUnitId");
    }
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Restore a matching application and database backup.");
}
