using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Localization;
using SupermarketPos.Domain.Entities;

namespace SupermarketPos.Infrastructure.Documents;

/// <summary>QuestPDF A4 sheet of shelf labels (3 per row): name, price and an EAN-13 barcode.</summary>
public sealed class ShelfLabelDocument : IDocument
{
    private readonly CultureInfo _culture;
    private readonly IReadOnlyList<Product> _products;
    private readonly string _storeName;

    public ShelfLabelDocument(IReadOnlyList<Product> products, string storeName, string language)
    {
        _products = products;
        _storeName = storeName;
        _culture = PosText.Culture(language);
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(20);
            page.DefaultTextStyle(t => t.FontSize(9).FontFamily(ReceiptFonts.Family(_culture.TwoLetterISOLanguageName)));
            if (_culture.TextInfo.IsRightToLeft) page.ContentFromRightToLeft();

            page.Content().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn();
                    c.RelativeColumn();
                    c.RelativeColumn();
                });

                foreach (var p in _products)
                    table.Cell().Padding(5).Element(c => Label(c, p));
            });
        });
    }

    private void Label(IContainer container, Product product)
    {
        container.Border(1).BorderColor("#CBD5E1").Padding(8).Column(col =>
        {
            col.Item().Text(_storeName).FontSize(7).FontColor("#64748B");
            col.Item().PaddingTop(2).Text(product.Name).Bold().FontSize(12);
            col.Item().Text(Money.Format(product.UnitPrice) +
                            (product.Unit == SupermarketPos.Domain.Common.Unit.Kilogram ? $" / {PosText.Get("Unit_Kg", _culture)}" : product.Unit==SupermarketPos.Domain.Common.Unit.Litre?$" / {PosText.Get("Measure_litre",_culture)}":""))
                      .FontSize(15).Bold().FontColor("#2563EB");

            var png = BarcodeImage.TryRenderEan13(product.Barcode);
            if (png is not null)
                col.Item().PaddingTop(4).AlignCenter().Height(48).Image(png).FitHeight();
            else
                col.Item().PaddingTop(4).AlignCenter().Text(product.Barcode).FontSize(8);
        });
    }
}
