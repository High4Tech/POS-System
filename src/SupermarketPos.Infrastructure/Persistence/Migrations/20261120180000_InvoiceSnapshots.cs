using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace SupermarketPos.Infrastructure.Persistence.Migrations;
[DbContext(typeof(PosDbContext))][Migration(MigrationId)]
public sealed class InvoiceSnapshots:Migration
{
    public const string MigrationId="20261120180000_InvoiceSnapshots";
    protected override void Up(MigrationBuilder m){m.AddColumn<string>("InvoiceProfileJson","Sales",nullable:true);m.AddColumn<string>("InvoiceProfileJson","ServiceTransactions",nullable:true);}
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Restore the matching application and data backup.");
}
