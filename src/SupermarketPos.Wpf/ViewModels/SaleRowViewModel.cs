using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Wpf.Common;

namespace SupermarketPos.Wpf.ViewModels;

public sealed class SaleRowViewModel
{
    public Sale Sale { get; }
    public decimal ReturnedTotal { get; }
    public string ReturnedText => MoneyText.Format(ReturnedTotal);
    public string InvoiceMargin => Sale.GrossProfit is decimal profit && Sale.NetTotal>0
        ? $"{MoneyText.Format(profit)} · {profit/Sale.NetTotal:P2}" : "—";
    public SaleRowViewModel(Sale sale, decimal returnedTotal = 0)
    {
        Sale = sale;
        ReturnedTotal = returnedTotal;
        Items = sale.Lines.Select(line => new SaleItemRow(
            line.BatchLot is { } lot ? $"{line.Name} · {lot} · {line.BatchExpiry:yyyy-MM-dd} {line.SerialNumber}".TrimEnd() : line.Name, line.Quantity,
            MoneyText.Format(line.UnitPrice), MoneyText.Format(line.LineGross), line.Barcode,
            line.UnitCost is { } cost ? MoneyText.Format(cost) : "—", line.VatRate,
            line.SerialNumber, line.Imei1, line.Imei2, line.WarrantyUntil,line.UnitCode.Length>0?Localization.LocalizationManager.Instance["Measure_"+line.UnitCode]:"")).ToArray();
    }

    public string ReceiptNo => Sale.ReceiptNo;
    public string? SourceReceiptNo => Sale.SourceReceiptNo;
    public string? CustomerName => Sale.CustomerName;
    public string? CustomerLabel => !string.IsNullOrWhiteSpace(Sale.CustomerName) ? Sale.CustomerName :
        Sale.Customer is null ? null : Localization.LocalizationManager.Instance["Customer_Details"];
    public string DateText => Sale.CreatedAt.ToString("dd.MM.yyyy HH:mm");
    public string Cashier => Sale.CashierName;
    public string PaymentText => Localization.LocalizationManager.Instance[Payment.LabelKey(Sale.PaymentMethod)];
    public string TotalText => MoneyText.Format(Sale.GrossTotal);
    public string BalanceText => MoneyText.Format(Sale.Outstanding);
    public IReadOnlyList<SaleItemRow> Items { get; }
}

public sealed record SaleItemRow(string Name, decimal Quantity, string UnitPrice, string LineTotal,
    string Barcode, string UnitCost, decimal VatRate, string? Serial = null, string? Imei1 = null, string? Imei2 = null, DateTime? WarrantyUntil = null,string Unit="");
