namespace SupermarketPos.Domain.Entities;

public sealed class SaleReturn
{
    public int Id { get; set; }
    public Guid ConfirmationId { get; set; }
    public int SaleId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PostedAt { get; set; }
    public string Operator { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "";
    public string Method { get; set; } = "";
    public string ExternalReference { get; set; } = "";
    public decimal Gross { get; set; }
    public decimal Tax { get; set; }
    public decimal CashRefund { get; set; }
    public decimal CardRefund { get; set; }
    public decimal ReceivableReduction { get; set; }
    public decimal CustomerCredit { get; set; }
    public string ItemsJson { get; set; } = "";
}

public sealed record ReturnSelection(int SaleLineId, decimal Quantity, bool Restock, string? Identity = null);
public sealed record ReturnedLine(int SaleLineId, int ProductId, string Name, string Unit, decimal Quantity,
    long BaseQuantity, decimal Gross, decimal Tax, bool Restock, int? BatchId, string? Lot, DateOnly? Expiry, string? Serial, string? Location);
