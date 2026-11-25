using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace SupermarketPos.Infrastructure.Persistence.Migrations;
[DbContext(typeof(PosDbContext))][Migration(MigrationId)]
public sealed class MedicineCatalog : Migration
{
    public const string MigrationId="20261120150000_MedicineCatalog";
    protected override void Up(MigrationBuilder m)
    {
        m.Sql("""
            CREATE TABLE MedicineReferences(Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,SourceKey TEXT NOT NULL,RegistrationNumber TEXT NOT NULL,SourceRow TEXT NOT NULL,Name TEXT NOT NULL,GenericName TEXT NOT NULL,Manufacturer TEXT NOT NULL,Strength TEXT NOT NULL,Formulation TEXT NOT NULL,PackDescription TEXT NOT NULL,Barcode TEXT NOT NULL,SearchText TEXT NOT NULL);
            CREATE UNIQUE INDEX IX_MedicineReferences_SourceKey ON MedicineReferences(SourceKey);
            CREATE INDEX IX_MedicineReferences_RegistrationNumber ON MedicineReferences(RegistrationNumber);
            CREATE INDEX IX_MedicineReferences_SearchText ON MedicineReferences(SearchText);
            CREATE TABLE CatalogImports(TemplateId TEXT NOT NULL PRIMARY KEY,ContentHash TEXT NOT NULL,RowCount INTEGER NOT NULL,ImportedAt TEXT NOT NULL,SourceFile TEXT NOT NULL,RedistributionStatus TEXT NOT NULL);
            """);
        m.Sql("ALTER TABLE Products ADD COLUMN MedicineReferenceId INTEGER NULL REFERENCES MedicineReferences(Id) ON DELETE RESTRICT;");
        m.AddColumn<bool>("StockTracked","Products",nullable:false,defaultValue:true);
        m.CreateIndex("IX_Products_MedicineReferenceId","Products","MedicineReferenceId",unique:true);
        m.Sql(MedicineSearchSchema.Sql);
    }
    protected override void Down(MigrationBuilder m)=>throw new NotSupportedException("Restore the matching application and data backup.");
}
