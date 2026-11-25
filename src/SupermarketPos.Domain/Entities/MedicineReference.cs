namespace SupermarketPos.Domain.Entities;

public sealed class MedicineReference
{
    public int Id{get;set;}
    public string SourceKey{get;set;}="";
    public string RegistrationNumber{get;set;}="";
    public string SourceRow{get;set;}="";
    public string Name{get;set;}="";
    public string GenericName{get;set;}="";
    public string Manufacturer{get;set;}="";
    public string Strength{get;set;}="";
    public string Formulation{get;set;}="";
    public string PackDescription{get;set;}="";
    public string Barcode{get;set;}="";
    public string SearchText{get;set;}="";
}
public sealed class CatalogImport
{
    public string TemplateId{get;set;}="";
    public string ContentHash{get;set;}="";
    public int RowCount{get;set;}
    public DateTime ImportedAt{get;set;}
    public string SourceFile{get;set;}="";
    public string RedistributionStatus{get;set;}="unverified";
}
public sealed record MedicineMatch(int? ReferenceId,int? ProductId,string Name,string GenericName,string Manufacturer,string Strength,string Formulation,string PackDescription,string Barcode,decimal? Price,decimal? Stock,string Status,string Locations="")
{
    public string Details=>string.Join(" · ",new[]{Strength,Formulation,PackDescription,GenericName,Manufacturer}.Where(s=>!string.IsNullOrWhiteSpace(s)));
}
