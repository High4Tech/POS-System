namespace SupermarketPos.Domain.Entities;

public sealed class CustomerCreditMovement
{
    public int Id { get; set; }
    public Guid ConfirmationId { get; set; }
    public int SaleReturnId { get; set; }
    public int? TargetSaleId { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Operator { get; set; } = "";
    public DateTime RecordedAt { get; set; }
}
