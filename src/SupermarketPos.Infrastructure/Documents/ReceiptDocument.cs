using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Localization;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Business;
using SupermarketPos.Domain.Pricing;

namespace SupermarketPos.Infrastructure.Documents;

/// <summary>QuestPDF thermal-style (80mm) receipt for a single sale, receipt layout.</summary>
public sealed class ReceiptDocument : IDocument
{
    public const string SoftwareFooter="Software by: High4Tech || 03256138361";
    private readonly CultureInfo _culture;
    private readonly Sale _sale;
    private readonly AppSettings _settings;
    private readonly (byte[] Data, float Width)? _logo;
    private readonly BusinessProfile? _business;

    public ReceiptDocument(Sale sale, AppSettings settings, string? logoPath = null, BusinessProfile? business = null)
    {
        _sale = sale;
        _settings = settings;
        _business = business;
        _culture = PosText.Culture(business?.InvoiceLanguage ?? settings.Language);
        _logo = LoadMonochromeLogo(logoPath ?? settings.LogoPath);
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            if (_business?.Paper == "A4") page.Size(PageSizes.A4);
            else page.ContinuousSize(_business?.Paper == "58" ? 58 : 80, QuestPDF.Infrastructure.Unit.Millimetre);
            page.Margin(6);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(t => t.FontSize(8).FontFamily(ReceiptFonts.Family(_culture.TwoLetterISOLanguageName)).FontColor(Colors.Black));
            if (_culture.TextInfo.IsRightToLeft) page.ContentFromRightToLeft();

            page.Content().Column(col =>
            {
                void Identifier(string key,string value,float size=8)=>col.Item().Row(row=>
                {
                    row.AutoItem().Text(Text(key)+": ").FontSize(size);
                    row.RelativeItem().ContentFromLeftToRight().Text(value).FontFamily("Segoe UI").FontSize(size);
                });
                if (_logo is { } logo)
                    col.Item().AlignCenter().Width(logo.Width).PaddingBottom(3).Image(logo.Data).FitWidth();
                col.Item().Text(_business?.StoreName??_settings.StoreName).AlignCenter().Bold().FontSize(11);
                if (!string.IsNullOrWhiteSpace(_business?.InvoiceTitle)) col.Item().Text(_business.InvoiceTitle).AlignCenter().Bold();
                if (_business is { Registered: true } business)
                {
                    if (business.LegalName != business.StoreName) col.Item().Text(business.LegalName).AlignCenter();
                    if (business.Ntn.Length > 0) col.Item().Text($"NTN: {business.Ntn}").AlignCenter().FontSize(7);
                    if (business.SalesTaxRegistered && business.Strn.Length > 0) col.Item().Text($"STRN: {business.Strn}").AlignCenter().FontSize(7);
                }
                if (!string.IsNullOrWhiteSpace(_business?.Address??_settings.StoreAddress))
                    col.Item().AlignCenter().Text(_business?.Address??_settings.StoreAddress).FontSize(7);
                var phones=string.Join(" || ",new[]{_business?.Phone,_business?.Phone2}.Where(p=>!string.IsNullOrWhiteSpace(p)));
                if(phones.Length>0)col.Item().ContentFromLeftToRight().AlignCenter().Text(phones).FontFamily("Segoe UI").FontSize(7);
                col.Item().PaddingVertical(3).LineHorizontal(0.5f);

                Identifier("Reports_ReceiptNo",_sale.ReceiptNo);
                if (_sale.SourceReceiptNo is { } reference)
                    Identifier("History_SourceReceipt",reference);
                Identifier("Reports_Date",_sale.CreatedAt.ToString("dd.MM.yyyy HH:mm",_culture));
                if (!string.IsNullOrWhiteSpace(_sale.CustomerName))
                    col.Item().Text($"{Text("Customer_Name")}: {_sale.CustomerName}");
                col.Item().PaddingVertical(3).LineHorizontal(0.5f);

                foreach (var line in _sale.Lines)
                {
                    col.Item().Text(line.Name).Bold();
                    if(line.BatchLot is{} lot){Identifier("Batch_Lot",lot,7);Identifier("Batch_Expiry",$"{line.BatchExpiry:yyyy-MM-dd}",7);}
                    if (line.SerialNumber is { } serial) Identifier("Trade_Serial",serial,7);
                    if (line.Imei1 is { } imei1) Identifier("IMEI 1",imei1,7);
                    if (line.Imei2 is { } imei2) Identifier("IMEI 2",imei2,7);
                    if (line.WarrantyUntil is { } warranty) Identifier("Trade_ReceiptWarranty",$"{warranty:yyyy-MM-dd}",7);
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().ContentFromLeftToRight().Text($"  {Qty(line)} {UnitLabel(line.UnitCode)} x {Price(line.UnitPrice)}").FontFamily("Segoe UI").FontSize(7);
                        r.ConstantItem(80).ContentFromLeftToRight().AlignRight().Text(Price(line.LineGross)).FontFamily("Segoe UI");
                    });
                    if (!string.IsNullOrWhiteSpace(line.DiscountDescription))
                        col.Item().Text($"{Text("Pos_LineDiscount")}: {line.DiscountDescription}").FontSize(7).Italic();
                }

                col.Item().PaddingVertical(3).LineHorizontal(0.5f);

                col.Item().Row(r =>
                {
                    r.RelativeItem().Text(Text("Pos_Total")).Bold().FontSize(11);
                    r.ConstantItem(102).ContentFromLeftToRight().AlignRight().Text(Price(_sale.GrossTotal)).Bold().FontSize(11).FontFamily("Segoe UI");
                });

                col.Item().PaddingTop(3).Text(Text("Pos_VatBreakdown")).Bold().FontSize(8);
                foreach (var g in _sale.VatBreakdown)
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Column(net=>
                        {
                            net.Item().Text($"{g.Rate * 100:0.####}% · {Text("Reports_Net")}").FontSize(7);
                            net.Item().ContentFromLeftToRight().Text(Price(g.Net)).FontFamily("Segoe UI").FontSize(7);
                        });
                        r.ConstantItem(80).ContentFromLeftToRight().AlignRight().Text(Price(g.Vat)).FontSize(7).FontFamily("Segoe UI");
                    });
                }
                col.Item().Row(r =>
                {
                    r.RelativeItem().Text(Text("Reports_Vat")).FontSize(7);
                    r.ConstantItem(80).ContentFromLeftToRight().AlignRight().Text(Price(_sale.VatTotal)).FontSize(7).FontFamily("Segoe UI");
                });

                col.Item().PaddingVertical(3).LineHorizontal(0.5f);

                col.Item().Row(r =>
                {
                    r.RelativeItem().Text(Text(Domain.Pricing.Payment.LabelKey(_sale.PaymentMethod)));
                    r.ConstantItem(80).ContentFromLeftToRight().AlignRight().Text(Price(_sale.AmountPaid)).FontFamily("Segoe UI");
                });
                if (_sale.CashTendered is { } tendered)
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text(Text("Receipt_Received"));
                        r.ConstantItem(80).ContentFromLeftToRight().AlignRight().Text(Price(tendered)).FontFamily("Segoe UI");
                    });
                }

                if (_sale.PaymentMethod == "CREDIT" || _sale.BalancePaid > 0)
                {
                    Identifier("Trade_ReceiptPaid",Price(_sale.AmountPaid));
                    Identifier("Trade_ReceiptDue",Price(Money.Round2(_sale.GrossTotal-_sale.AmountPaid)));
                    if(_sale.CreditDueDate is{} due)Identifier("Trade_DueDate",$"{due:yyyy-MM-dd}",7);
                }

                if(_business?.ReceiptNotes.Count>0)
                {foreach(var note in _business.ReceiptNotes.Where(n=>n.Enabled&&!string.IsNullOrWhiteSpace(n.Text)).OrderBy(n=>n.Order))col.Item().PaddingTop(3).AlignCenter().Text(note.Text).FontSize(7);}
                else col.Item().PaddingTop(6).AlignCenter().Text(Text("Receipt_ThankYou")).FontSize(7).Italic();
                col.Item().PaddingTop(6).ContentFromLeftToRight().AlignCenter().Text(SoftwareFooter).FontFamily("Segoe UI").FontSize(6);
            });
        });
    }

    internal static (byte[] Data, float Width)? LoadMonochromeLogo(string? path)
    {
        if (path is null || !File.Exists(path)) return null;
        using var source = SKBitmap.Decode(path);
        if (source is null) return null;

        var scale = Math.Min(1f, Math.Min(440f / source.Width, 250f / source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        using var monochrome = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var grayscale = SKColorFilter.CreateColorMatrix(
        [
            0.2126f, 0.7152f, 0.0722f, 0, 0,
            0.2126f, 0.7152f, 0.0722f, 0, 0,
            0.2126f, 0.7152f, 0.0722f, 0, 0,
            0, 0, 0, 1, 0
        ]);
        using var paint = new SKPaint { ColorFilter = grayscale };
        using var canvas = new SKCanvas(monochrome);
        canvas.Clear(SKColors.White);
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, SKRect.Create(width, height), new SKSamplingOptions(SKFilterMode.Linear), paint);
        using var rendered = SKImage.FromBitmap(monochrome);
        using var png = rendered.Encode(SKEncodedImageFormat.Png, 100);
        return (png.ToArray(), Math.Min(88f, 50f * source.Width / source.Height));
    }

    private static string Qty(SaleLine l) =>
        l.Quantity == Math.Truncate(l.Quantity)
            ? l.Quantity.ToString("0", CultureInfo.InvariantCulture)
            : l.Quantity.ToString("0.000", CultureInfo.InvariantCulture);

    private string Text(string key) => PosText.Get(key, _culture);
    private string UnitLabel(string code)=>code.Length==0?"":Text("Measure_"+code);

    private static string Price(decimal value) => Money.Format(value);
}
