using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace SupermarketPos.Infrastructure.Persistence.Migrations;
[DbContext(typeof(PosDbContext))]
[Migration(DigitalCustomerHistory.MigrationId)]
public sealed class DigitalCustomerHistory:Migration
{
    public const string MigrationId="20261128130000_DigitalCustomerHistory";
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<string>("RecipientKey","ServiceTransactions",type:"TEXT",nullable:false,defaultValue:"");
        m.AddColumn<string>("CustomerName","ServiceTransactions",type:"TEXT",nullable:false,defaultValue:"");
        m.CreateTable("DigitalCustomers",columns:t=>new{RecipientKey=t.Column<string>(type:"TEXT",maxLength:160,nullable:false),Name=t.Column<string>(type:"TEXT",maxLength:150,nullable:false)},constraints:t=>t.PrimaryKey("PK_DigitalCustomers",x=>x.RecipientKey));
        var clean="upper(replace(replace(replace(replace(replace(trim(CustomerPhone),' ',''),'-',''),'(',''),')',''),'+',''))";
        var phone=$"CASE WHEN length({clean})=14 AND substr({clean},1,4)='0092' THEN substr({clean},3) WHEN length({clean})=11 AND substr({clean},1,2)='03' THEN '92'||substr({clean},2) ELSE {clean} END";
        m.Sql($"UPDATE ServiceTransactions SET RecipientKey=CASE WHEN length({phone})=12 AND substr({phone},1,3)='923' AND {phone} NOT GLOB '*[^0-9]*' THEN 'phone:'||({phone}) WHEN substr({clean},1,2)='PK' THEN 'iban:'||({clean}) ELSE 'account:'||ProviderId||':'||({clean}) END WHERE trim(CustomerPhone)<>''");
        m.Sql("UPDATE ServiceTransactions SET RecipientKey=(SELECT original.RecipientKey FROM ServiceTransactions original WHERE original.Id=ServiceTransactions.ReversesId) WHERE ReversesId IS NOT NULL AND EXISTS(SELECT 1 FROM ServiceTransactions original WHERE original.Id=ServiceTransactions.ReversesId)");
        m.CreateIndex("IX_ServiceTransactions_RecipientKey_OccurredAt","ServiceTransactions",new[]{"RecipientKey","OccurredAt"});
    }
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Restore a matching backup instead.");
}
