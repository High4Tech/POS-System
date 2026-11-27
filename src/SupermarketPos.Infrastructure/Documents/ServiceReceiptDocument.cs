using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Localization;

namespace SupermarketPos.Infrastructure.Documents;
public sealed class ServiceReceiptDocument:IDocument
{
    private readonly IReadOnlyList<ServiceTransaction> _lines;
    private readonly IReadOnlyList<ServicePosting> _postings;
    private readonly string _language,_paper;
    private readonly ServiceCheckout? _checkout;
    public ServiceReceiptDocument(ServiceTransaction transaction,ServicePosting posting,string language,string paper):this([transaction],[posting],language,paper){}
    public ServiceReceiptDocument(IReadOnlyList<ServiceTransaction> lines,IReadOnlyList<ServicePosting> postings,string language,string paper,ServiceCheckout? checkout=null){_lines=lines;_postings=postings;_language=language;_paper=paper;_checkout=checkout;}
    private string T(string key)=>PosText.Get(key,PosText.Culture(_language));
    public DocumentMetadata GetMetadata()=>DocumentMetadata.Default;
    public void Compose(IDocumentContainer container)=>container.Page(page=>
    {
        if(_paper=="A4")page.Size(QuestPDF.Helpers.PageSizes.A4);else page.ContinuousSize(_paper=="58"?58:80,QuestPDF.Infrastructure.Unit.Millimetre);
        page.Margin(8);page.DefaultTextStyle(t=>t.FontFamily(ReceiptFonts.Family(_language)).FontSize(9));if(_language=="ur")page.ContentFromRightToLeft();
        page.Content().Column(c=>
        {
            var first=_lines.First();var profile=_checkout?.InvoiceProfileJson??first.InvoiceProfileJson;
            var business=profile is{} json?System.Text.Json.JsonSerializer.Deserialize<SupermarketPos.Domain.Business.BusinessProfile>(json):null;
            c.Item().Text(_checkout?.StoreName??first.StoreName).AlignCenter().Bold().FontSize(12);c.Item().Text(T("Service_Receipt")).AlignCenter();
            if(!string.IsNullOrWhiteSpace(business?.Address))c.Item().Text(business.Address).AlignCenter().FontSize(8);
            var phones=string.Join(" || ",new[]{business?.Phone,business?.Phone2}.Where(p=>!string.IsNullOrWhiteSpace(p)));if(phones.Length>0)c.Item().ContentFromLeftToRight().Text(phones).AlignCenter().FontFamily("Segoe UI").FontSize(7);
            c.Item().Text(_checkout?.ReceiptNumber??first.ReceiptNumber);c.Item().Text((_checkout?.CreatedAt??first.OccurredAt).ToString("yyyy-MM-dd HH:mm"));
            foreach(var transaction in _lines)
            {
                c.Item().PaddingVertical(5).LineHorizontal(.5f);
                c.Item().Text(transaction.ProviderName+" · "+T(transaction.Type=="TRANSFER"?"Digital_OnlineTransfer":"Service_"+transaction.Type)).Bold();
                if(transaction.PackageName.Length>0)c.Item().Text(transaction.PackageName);
                if(transaction.PackageIdentifier.Length>0)c.Item().Text(transaction.PackageIdentifier+" · "+T("Service_"+transaction.AccountType));
                if(transaction.PackageValidity.Length>0)c.Item().Text(T("Service_Validity")+": "+transaction.PackageValidity);
                if(transaction.Status!="Confirmed")c.Item().Text(T("Service_Status")+": "+T("Service_"+transaction.Status));
                if(transaction.Reference.Length>0)c.Item().Text(T("Service_Reference")+": "+transaction.Reference);
                if(transaction.CustomerPhone.Length>0)c.Item().Text(T(transaction.Type is "LOAD" or "PACKAGE"?"Customer_Phone":"Digital_Recipient")+": "+transaction.CustomerPhone);
                c.Item().Text(T("Digital_Price")+": "+Money.Format(transaction.Type=="PACKAGE"?transaction.Collected:transaction.Principal));
                if(transaction.Type!="PACKAGE"&&transaction.CustomerFee>0)c.Item().Text(T("Digital_Fee")+": "+Money.Format(transaction.CustomerFee));
            }
            var total=_checkout?.Total??(_lines.Any(t=>t.Type=="REVERSAL")?_postings.Sum(p=>p.CashChange):_lines.Sum(t=>t.SettlementAmount));
            c.Item().PaddingVertical(5).LineHorizontal(.5f);c.Item().Text(T(total<0?"Digital_ToPay":"Digital_ToCollect")+": "+Money.Format(Math.Abs(total))).Bold().FontSize(12);
            if(_checkout is{} checkout)
            {
                c.Item().Text(T("Reports_Payment")+": "+T("Payment_"+(checkout.PaymentMethod=="CARD"?"Card":"Cash")));
                if(checkout.PaymentMethod=="CASH"&&total>0){c.Item().Text(T("Pos_Tendered")+": "+Money.Format(checkout.Tendered));c.Item().Text(T("Pos_Change")+": "+Money.Format(checkout.Change));}
            }
            c.Item().PaddingTop(6).Text(T("Digital_ManualReceipt")).FontSize(7);
            if(business is not null)foreach(var note in business.ReceiptNotes.Where(n=>n.Enabled&&!string.IsNullOrWhiteSpace(n.Text)).OrderBy(n=>n.Order))c.Item().PaddingTop(3).Text(note.Text).FontSize(7);
            c.Item().PaddingTop(6).ContentFromLeftToRight().AlignCenter().Text(ReceiptDocument.SoftwareFooter).FontFamily("Segoe UI").FontSize(6);
        });
    });
}
