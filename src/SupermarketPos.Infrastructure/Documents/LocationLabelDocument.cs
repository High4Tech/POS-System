using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SupermarketPos.Domain.Entities;
namespace SupermarketPos.Infrastructure.Documents;
public sealed class LocationLabelDocument(IReadOnlyList<StockLocation> locations,string language,bool qr=false,IReadOnlyList<StockLocation>? allLocations=null):IDocument
{
    public DocumentMetadata GetMetadata()=>DocumentMetadata.Default;
    public void Compose(IDocumentContainer container)=>container.Page(page=>
    {
        page.Size(PageSizes.A4);page.Margin(24);page.DefaultTextStyle(t=>t.FontFamily(ReceiptFonts.Family(language)).FontSize(12));
        page.Content().Table(table=>
        {
            table.ColumnsDefinition(c=>{c.RelativeColumn();if(qr)c.RelativeColumn();});
            foreach(var location in locations)table.Cell().Padding(8).Border(1).Padding(10).Column(col=>
            {
                var parent=(allLocations??locations).FirstOrDefault(l=>l.Id==location.ParentId);col.Item().Text((parent is null?"":parent.Name+" / ")+location.Name).Bold();
                var png=(qr?BarcodeImage.TryRenderQr(location.Barcode):BarcodeImage.TryRenderCode128(location.Barcode,1200,180))??throw new InvalidOperationException("Location label could not be encoded.");
                col.Item().Height(qr?100:68).Image(png).FitArea();col.Item().Text(location.Barcode).FontFamily("Segoe UI").FontSize(8);
            });
        });
    });
}
