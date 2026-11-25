using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace SupermarketPos.Infrastructure.Persistence.Migrations;
[DbContext(typeof(PosDbContext))][Migration(MigrationId)]
public sealed class CreditTerms:Migration
{
    public const string MigrationId="20261120190000_CreditTerms";
    protected override void Up(MigrationBuilder m){m.AddColumn<DateTime>("CreditDueDate","Sales",nullable:true);m.AddColumn<string>("CreditNote","Sales",nullable:false,defaultValue:"");}
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Restore the matching application and data backup.");
}
