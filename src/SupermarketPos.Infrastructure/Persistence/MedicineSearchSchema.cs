namespace SupermarketPos.Infrastructure.Persistence;
public static class MedicineSearchSchema
{
    public const string Sql="""
        CREATE VIRTUAL TABLE IF NOT EXISTS MedicineReferenceSearch USING fts5(Name,GenericName,Manufacturer,SearchText,content='MedicineReferences',content_rowid='Id',tokenize='unicode61 remove_diacritics 2');
        CREATE TRIGGER IF NOT EXISTS MedicineSearchInsert AFTER INSERT ON MedicineReferences BEGIN
          INSERT INTO MedicineReferenceSearch(rowid,Name,GenericName,Manufacturer,SearchText) VALUES(new.Id,new.Name,new.GenericName,new.Manufacturer,new.SearchText); END;
        CREATE TRIGGER IF NOT EXISTS MedicineSearchDelete AFTER DELETE ON MedicineReferences BEGIN
          INSERT INTO MedicineReferenceSearch(MedicineReferenceSearch,rowid,Name,GenericName,Manufacturer,SearchText) VALUES('delete',old.Id,old.Name,old.GenericName,old.Manufacturer,old.SearchText); END;
        CREATE TRIGGER IF NOT EXISTS MedicineSearchUpdate AFTER UPDATE ON MedicineReferences BEGIN
          INSERT INTO MedicineReferenceSearch(MedicineReferenceSearch,rowid,Name,GenericName,Manufacturer,SearchText) VALUES('delete',old.Id,old.Name,old.GenericName,old.Manufacturer,old.SearchText);
          INSERT INTO MedicineReferenceSearch(rowid,Name,GenericName,Manufacturer,SearchText) VALUES(new.Id,new.Name,new.GenericName,new.Manufacturer,new.SearchText); END;
        """;
}
