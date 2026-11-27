using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace SupermarketPos.Infrastructure.Persistence.Migrations;
[DbContext(typeof(PosDbContext))]
[Migration(DigitalServiceCheckout.MigrationId)]
public sealed class DigitalServiceCheckout:Migration
{
    public const string MigrationId="20261127120000_DigitalServiceCheckout";
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("ServiceCheckouts",columns:t=>new{Id=t.Column<int>("INTEGER",nullable:false).Annotation("Sqlite:Autoincrement",true),ConfirmationId=t.Column<Guid>("TEXT",nullable:false),CreatedAt=t.Column<DateTime>("TEXT",nullable:false),PaymentMethod=t.Column<string>("TEXT",nullable:false),Total=t.Column<decimal>("TEXT",nullable:false),Tendered=t.Column<decimal>("TEXT",nullable:false),InvoiceProfileJson=t.Column<string>("TEXT",nullable:false),StoreName=t.Column<string>("TEXT",nullable:false),Operator=t.Column<string>("TEXT",nullable:false)},constraints:t=>t.PrimaryKey("PK_ServiceCheckouts",c=>c.Id));
        m.Sql("ALTER TABLE \"ServiceTransactions\" ADD COLUMN \"CheckoutId\" INTEGER NULL REFERENCES \"ServiceCheckouts\" (\"Id\") ON DELETE RESTRICT;");
        m.AddColumn<string>("PaymentMethod","ServiceTransactions",type:"TEXT",nullable:false,defaultValue:"CASH");
        m.CreateIndex("IX_ServiceCheckouts_ConfirmationId","ServiceCheckouts","ConfirmationId",unique:true);
        m.CreateIndex("IX_ServiceTransactions_CheckoutId","ServiceTransactions","CheckoutId");
    }
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Restore a matching backup instead.");
}
