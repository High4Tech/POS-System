using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Localization;

namespace SupermarketPos.Infrastructure.Documents;
public sealed class ServiceReceiptDocument(ServiceTransaction transaction,ServicePosting posting,string language,string paper):IDocument
{
    private string T(string key)=>PosText.Get(key,PosText.Culture(language));
    public DocumentMetadata GetMetadata()=>DocumentMetadata.Default;
    public void Compose(IDocumentContainer container)=>container.Page(page=>
    {
        if(paper=="A4")page.Size(QuestPDF.Helpers.PageSizes.A4);else page.ContinuousSize(paper=="58"?58:80,QuestPDF.Infrastructure.Unit.Millimetre);
        page.Margin(8);page.DefaultTextStyle(t=>t.FontFamily(ReceiptFonts.Family(language)).FontSize(9));if(language=="ur")page.ContentFromRightToLeft();
        page.Content().Column(c=>
        {
            var business=transaction.InvoiceProfileJson is{} json?System.Text.Json.JsonSerializer.Deserialize<SupermarketPos.Domain.Business.BusinessProfile>(json):null;
            c.Item().Text(transaction.StoreName).AlignCenter().Bold().FontSize(12);c.Item().Text(T("Service_Receipt")).AlignCenter();
            if(!string.IsNullOrWhiteSpace(business?.Address))c.Item().Text(business.Address).AlignCenter().FontSize(8);
            var phones=string.Join(" || ",new[]{business?.Phone,business?.Phone2}.Where(p=>!string.IsNullOrWhiteSpace(p)));if(phones.Length>0)c.Item().ContentFromLeftToRight().Text(phones).AlignCenter().FontFamily("Segoe UI").FontSize(7);
            c.Item().Text(transaction.ReceiptNumber);c.Item().Text(transaction.OccurredAt.ToString("yyyy-MM-dd HH:mm"));
            c.Item().PaddingVertical(5).LineHorizontal(.5f);
            c.Item().Text(transaction.ProviderName+" · "+T("Service_"+transaction.Type));
            if(transaction.PackageName.Length>0)c.Item().Text(transaction.PackageName);
            if(transaction.PackageIdentifier.Length>0)c.Item().Text(transaction.PackageIdentifier+" · "+T("Service_"+transaction.AccountType));
            if(transaction.PackageBenefits.Length>0)c.Item().Text(transaction.PackageBenefits);
            if(transaction.PackageValidity.Length>0)c.Item().Text(T("Service_Validity")+": "+transaction.PackageValidity);
            c.Item().Text(T("Service_Status")+": "+T("Service_"+transaction.Status));
            c.Item().Text(T("Service_Reference")+": "+transaction.Reference);
            if(transaction.CustomerPhone.Length>0)c.Item().Text(T("Customer_Phone")+": "+transaction.CustomerPhone);
            foreach(var pair in new[]{("Service_Principal",transaction.Principal),("Service_CustomerFee",transaction.CustomerFee),("Service_CashChange",posting.CashChange)})c.Item().Text(T(pair.Item1)+": "+Money.Format(pair.Item2));
            c.Item().PaddingTop(6).Text(T("Service_ManualNotice")).FontSize(7);
            if(business is not null)foreach(var note in business.ReceiptNotes.Where(n=>n.Enabled&&!string.IsNullOrWhiteSpace(n.Text)).OrderBy(n=>n.Order))c.Item().PaddingTop(3).Text(note.Text).FontSize(7);
            c.Item().PaddingTop(6).ContentFromLeftToRight().AlignCenter().Text(ReceiptDocument.SoftwareFooter).FontFamily("Segoe UI").FontSize(6);
        });
    });
}
