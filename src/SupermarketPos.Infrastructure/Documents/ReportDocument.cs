using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SupermarketPos.Application.Dtos;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Localization;

namespace SupermarketPos.Infrastructure.Documents;

/// <summary>QuestPDF A4 layout for an X/Z or date-range sales report.</summary>
public sealed class ReportDocument : IDocument
{
    private readonly CultureInfo _culture;
    private readonly SalesReport _report;
    private readonly string _storeName;

    public ReportDocument(SalesReport report, string storeName, string language)
    {
        _report = report;
        _storeName = storeName;
        _culture = PosText.Culture(language);
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(30);
            page.DefaultTextStyle(t => t.FontSize(10).FontFamily(ReceiptFonts.Family(_culture.TwoLetterISOLanguageName)));
            if (_culture.TextInfo.IsRightToLeft) page.ContentFromRightToLeft();

            page.Header().Column(col =>
            {
                col.Item().Text(_storeName).Bold().FontSize(16);
                col.Item().Text(_report.Title switch
                {
                    "X-Report" => Text("Reports_XReport"),
                    "Z-Report" => Text("Reports_ZReport"),
                    _ => _report.Title
                }).FontSize(13).Bold();
                col.Item().Text($"{_report.FromInclusive:dd.MM.yyyy} – {_report.ToExclusive.AddSeconds(-1):dd.MM.yyyy}").FontSize(9);
                col.Item().PaddingTop(6).LineHorizontal(1);
            });

            page.Content().PaddingVertical(10).Column(col =>
            {
                col.Spacing(4);
                col.Item().Text($"{Text("Reports_SaleCount")}: {_report.SaleCount}");
                col.Item().Row(r =>
                {
                    r.RelativeItem().Text(Text("Reports_Gross")).Bold();
                    r.ConstantItem(120).AlignRight().Text(Price(_report.GrossTotal)).Bold();
                });
                col.Item().Row(r =>
                {
                    r.RelativeItem().Text(Text("Reports_Net"));
                    r.ConstantItem(120).AlignRight().Text(Price(_report.NetTotal));
                });
                col.Item().Row(r =>
                {
                    r.RelativeItem().Text(Text("Reports_Vat"));
                    r.ConstantItem(120).AlignRight().Text(Price(_report.VatTotal));
                });

                col.Item().PaddingTop(10).Text(Text("Pos_VatBreakdown")).Bold().FontSize(12);
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn();
                        c.RelativeColumn();
                        c.RelativeColumn();
                        c.RelativeColumn();
                    });

                    table.Cell().Element(HeaderCell).Text(Text("Report_Rate"));
                    table.Cell().Element(HeaderCell).AlignRight().Text(Text("Reports_Net"));
                    table.Cell().Element(HeaderCell).AlignRight().Text(Text("Reports_Vat"));
                    table.Cell().Element(HeaderCell).AlignRight().Text(Text("Report_GrossShort"));

                    foreach (var g in _report.VatBreakdown)
                    {
                        table.Cell().Element(BodyCell).Text($"{g.Rate * 100:0.####}%");
                        table.Cell().Element(BodyCell).AlignRight().Text(Price(g.Net));
                        table.Cell().Element(BodyCell).AlignRight().Text(Price(g.Vat));
                        table.Cell().Element(BodyCell).AlignRight().Text(Price(g.Gross));
                    }
                });

                col.Item().PaddingTop(10).Text(Text("Reports_PaymentSplit")).Bold().FontSize(12);
                col.Item().Row(r =>
                {
                    r.RelativeItem().Text($"{Text("Payment_Cash")} ({_report.PaymentSplit.CashCount})");
                    r.ConstantItem(120).AlignRight().Text(Price(_report.PaymentSplit.CashTotal));
                });
                col.Item().Row(r =>
                {
                    r.RelativeItem().Text($"{Text("Payment_Card")} ({_report.PaymentSplit.CardCount})");
                    r.ConstantItem(120).AlignRight().Text(Price(_report.PaymentSplit.CardTotal));
                });
                if (_report.PaymentSplit.OtherCount > 0)
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text($"{Text("Payment_Other")} ({_report.PaymentSplit.OtherCount})");
                        r.ConstantItem(120).AlignRight().Text(Price(_report.PaymentSplit.OtherTotal));
                    });
                col.Item().Row(r =>
                {
                    r.RelativeItem().Text(Text("Pos_Total")).Bold();
                    r.ConstantItem(120).AlignRight().Text(Price(_report.PaymentSplit.TotalAmount)).Bold();
                });
                if (_report.CreditCashRefunds != 0 || _report.CreditCardRefunds != 0)
                    col.Item().PaddingTop(10).Text(string.Format(_culture, Text("Credit_RefundSummary"),
                        Price(_report.CreditCashRefunds), Price(_report.CreditCardRefunds)));
            });

            page.Footer().AlignRight().Text(t =>
            {
                t.Span($"{Text("Report_Created")}: ").FontSize(8);
                t.Span(DateTime.Now.ToString("dd.MM.yyyy HH:mm", _culture)).FontSize(8);
            });
        });
    }

    private static IContainer HeaderCell(IContainer c) =>
        c.BorderBottom(1).PaddingVertical(4).DefaultTextStyle(t => t.Bold());

    private static IContainer BodyCell(IContainer c) =>
        c.BorderBottom(0.5f).PaddingVertical(3);

    private string Text(string key) => PosText.Get(key, _culture);

    private static string Price(decimal value) => Money.Format(value);
}
