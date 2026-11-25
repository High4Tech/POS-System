using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace SupermarketPos.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PosDbContext))][Migration(MigrationId)]
public sealed class SaleReturns : Migration
{
    public const string MigrationId = "20261125110000_SaleReturns";
    protected override void Up(MigrationBuilder migration)
    {
        migration.AddColumn<decimal>("ReturnCreditApplied", "Sales", type: "TEXT", nullable: false, defaultValue: 0m);
        migration.Sql("""
            CREATE TABLE SaleReturns (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ConfirmationId TEXT NOT NULL, SaleId INTEGER NOT NULL REFERENCES Sales(Id) ON DELETE RESTRICT,
                CreatedAt TEXT NOT NULL, PostedAt TEXT, Operator TEXT NOT NULL, Reason TEXT NOT NULL,
                Status TEXT NOT NULL, Method TEXT NOT NULL, ExternalReference TEXT NOT NULL,
                Gross TEXT NOT NULL, Tax TEXT NOT NULL, CashRefund TEXT NOT NULL, CardRefund TEXT NOT NULL,
                ReceivableReduction TEXT NOT NULL, CustomerCredit TEXT NOT NULL, ItemsJson TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IX_SaleReturns_ConfirmationId ON SaleReturns(ConfirmationId);
            CREATE INDEX IX_SaleReturns_SaleId_Status ON SaleReturns(SaleId,Status);
            """);
    }

    protected override void Down(MigrationBuilder migration) => throw new NotSupportedException("Restore a matching backup instead.");
}
