using System.Windows;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Wpf.Views;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using SupermarketPos.Application.Services;
using SupermarketPos.Domain.Business;
using SupermarketPos.Infrastructure.Persistence;

namespace SupermarketPos.Wpf.Common;

public sealed class ReceiptPreviewDialog(IReceiptService receipts, PosStorage storage,IBusinessSetup setup,ISettingsRepository settings,BulkRecordService records,PosDbContext db)
{
    private bool _open;
    public async Task ShowCheckoutAsync(ServiceCheckout checkout)
    {
        if(_open)return;_open=true;
        try
        {
            var business=JsonSerializer.Deserialize<BusinessProfile>(checkout.InvoiceProfileJson)??(await setup.LoadAsync()).Profile;
            var ids=checkout.Lines.Select(t=>t.Id).ToArray();var posts=await db.ServicePostings.AsNoTracking().Where(p=>ids.Contains(p.TransactionId)).ToArrayAsync();
            var preview=await Task.Run(()=>{var document=new SupermarketPos.Infrastructure.Documents.ServiceReceiptDocument(checkout.Lines,posts,business.InvoiceLanguage,business.Paper,checkout);return new ReceiptPreview(QuestPDF.Fluent.GenerateExtensions.GeneratePdf(document),QuestPDF.Fluent.GenerateExtensions.GenerateImages(document,new QuestPDF.Infrastructure.ImageGenerationSettings{RasterDpi=144}).ToArray());});
            var phones=checkout.Lines.Where(t=>t.Type is "LOAD" or "PACKAGE").Select(t=>t.CustomerPhone).Distinct().ToArray();var phone=phones.Length==1&&checkout.Lines.All(t=>t.Type is "LOAD" or "PACKAGE")?phones[0]:null;
            var info=new InvoiceActionInfo(checkout.ReceiptNumber,checkout.CreatedAt,Math.Abs(checkout.Total),checkout.StoreName,null,phone,business.InvoiceLanguage,business.Paper,business.Printer,business.InvoiceExportFolder,business.WhatsAppTemplate,JsonSerializer.Serialize(new{checkout,posts,business}));
            info=info with{SavePrinter=SavePrinterAsync};var panel=new ReceiptPreviewWindow(preview,checkout.ReceiptNumber,storage,info);await InAppDialog.ShowAsync(panel,c=>panel.RequestClose=c);
        }
        finally{_open=false;}
    }
    public async Task ShowServiceAsync(ServiceTransaction transaction,ServicePosting posting,SupermarketPos.Domain.Business.BusinessProfile business)
    {
        if(transaction.CheckoutId is{} checkoutId){var checkout=await db.ServiceCheckouts.AsNoTracking().Include(c=>c.Lines.OrderBy(t=>t.Id)).SingleAsync(c=>c.Id==checkoutId);await ShowCheckoutAsync(checkout);return;}
        if(_open)return;_open=true;try{
        var preview=await Task.Run(()=>
        {
            var document=new SupermarketPos.Infrastructure.Documents.ServiceReceiptDocument(transaction,posting,business.InvoiceLanguage,business.Paper);
            return new ReceiptPreview(QuestPDF.Fluent.GenerateExtensions.GeneratePdf(document),QuestPDF.Fluent.GenerateExtensions.GenerateImages(document,new QuestPDF.Infrastructure.ImageGenerationSettings{RasterDpi=144}).ToArray());
        });
        var info=new InvoiceActionInfo(transaction.ReceiptNumber,posting.PostedAt==default?transaction.OccurredAt:posting.PostedAt,transaction.Type is "LOAD" or "PACKAGE"?transaction.Collected:transaction.Principal,transaction.StoreName,null,transaction.CustomerPhone,business.InvoiceLanguage,business.Paper,business.Printer,business.InvoiceExportFolder,business.WhatsAppTemplate,JsonSerializer.Serialize(new{transaction,posting,business}));
        info=info with{SavePrinter=SavePrinterAsync};var panel=new ReceiptPreviewWindow(preview,transaction.ReceiptNumber,storage,info);await InAppDialog.ShowAsync(panel,close=>panel.RequestClose=close);
        }finally{_open=false;}
    }
    public async Task ShowAsync(Sale sale,string? initialAction=null)
    {
        if(_open)return;_open=true;try{
        var preview = await receipts.RenderPreviewAsync(sale);
        var current=(await setup.LoadAsync()).Profile;var business=current;
        if(sale.InvoiceProfileJson is{} savedProfile)business=JsonSerializer.Deserialize<BusinessProfile>(savedProfile)??current;
        var fiscal=await db.FiscalSubmissions.AsNoTracking().SingleOrDefaultAsync(f=>f.SaleId==sale.Id);
        if(fiscal is not null){using var snapshot=JsonDocument.Parse(fiscal.InvoiceJson);if(snapshot.RootElement.TryGetProperty("businessProfile",out var p))business=p.Deserialize<BusinessProfile>()??current;}
        var info=new InvoiceActionInfo(sale.ReceiptNo,sale.CreatedAt,sale.GrossTotal,business.StoreName.Length>0?business.StoreName:(await settings.GetAsync()).StoreName,sale.CustomerName,sale.CustomerPhone,business.InvoiceLanguage,current.Paper,current.Printer,current.InvoiceExportFolder,current.WhatsAppTemplate,JsonSerializer.Serialize(new{sale,business}),phone=>records.SaveCustomerAsync(sale.Id,SupermarketPos.Domain.Entities.SaleCustomer.Create(sale.CustomerName,phone,sale.CustomerEmail,sale.CustomerAddress)));
        info=info with{SavePrinter=SavePrinterAsync};
        var window = new ReceiptPreviewWindow(preview, sale.ReceiptNo, storage,info);
        if(initialAction is not null)window.Loaded+=(_,_)=>window.InvokeAction(initialAction);
        await InAppDialog.ShowAsync(window,close=>window.RequestClose=close);
        }finally{_open=false;}
    }
    private async Task SavePrinterAsync(string name){var state=await setup.LoadAsync();await setup.SaveDraftAsync(state.Profile with{Printer=name},state.Step);}
}
