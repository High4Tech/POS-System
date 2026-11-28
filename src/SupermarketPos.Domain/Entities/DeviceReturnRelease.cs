namespace SupermarketPos.Domain.Entities;

public sealed class DeviceReturnRelease
{
    public int Id { get; set; }
    public Guid ConfirmationId { get; set; }
    public int SaleReturnId { get; set; }
    public int SaleLineId { get; set; }
    public int SerializedUnitId { get; set; }
    public string Inspection { get; set; } = "";
    public string Operator { get; set; } = "";
    public DateTime ReleasedAt { get; set; }
}
