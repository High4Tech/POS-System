using System.Globalization;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SupermarketPos.Domain.Common;

namespace SupermarketPos.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PosDbContext))]
[Migration("20261117150000_TechStoreProfileAndCatalog")]
public sealed class TechStoreProfileAndCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("LogoPath", "Settings", type: "TEXT", maxLength: 500, nullable: true);
        migrationBuilder.Sql("""
            UPDATE Settings
            SET StoreName = 'Tech Store',
                StoreAddress = CASE WHEN StoreAddress = 'Atatürk Cad. No:42, İstanbul' THEN '' ELSE StoreAddress END
            WHERE StoreName IN ('FreshMarket Süpermarket', 'FreshMarket Supermarket', 'Süpermarket', 'Supermarket');
            """);

        // Sales and purchases contain product snapshots. Keep their rows and the product ID sequence.
        migrationBuilder.Sql("DELETE FROM Products;");
        migrationBuilder.Sql("DELETE FROM Categories;");

        var sequence = 0;
        foreach (var (category, products) in TechCatalog.Groups)
        {
            migrationBuilder.Sql($"INSERT INTO Categories (Name) VALUES ({SqlText(category)});");
            foreach (var (name, price) in products)
            {
                sequence++;
                var stock = category == "Mobile Phones" ? 3 + sequence % 6 : 12 + sequence * 7 % 27;
                var barcode = TechCatalog.Barcode(sequence);
                migrationBuilder.Sql(FormattableString.Invariant($"""
                    INSERT INTO Products (Barcode, Name, UnitPrice, VatRate, Unit, StockQuantity, CategoryId)
                    SELECT {SqlText(barcode)}, {SqlText(name)}, '{price}.0', '0.2', {(int)Unit.Piece}, '{stock}.0', Id
                    FROM Categories WHERE Name = {SqlText(category)};
                    """));
            }
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Restore the pre-upgrade database backup to undo the catalog replacement.");

    private static string SqlText(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
