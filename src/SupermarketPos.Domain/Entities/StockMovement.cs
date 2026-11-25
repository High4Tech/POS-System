namespace SupermarketPos.Domain.Entities;

public enum StockMovementKind { OpeningStock, PurchaseReceipt, Sale, ManualAdjustment, BatchAssignment, BatchHold }

public sealed class StockMovement
{
    public int Id { get; private set; }
    public int ProductId { get; private set; }
    public string ProductName { get; private set; } = "";
    public decimal QuantityChange { get; private set; }
    public StockMovementKind Kind { get; private set; }
    public DateTime OccurredAt { get; private set; }
    public string? Reference { get; private set; }
    public int? BatchId { get; private set; }
    public string? BatchLot { get; private set; }

    private StockMovement() { }

    public StockMovement(int productId, string productName, decimal quantityChange, StockMovementKind kind,
        DateTime occurredAt, string? reference = null, InventoryBatch? batch = null)
    {
        ProductId = productId;
        ProductName = productName;
        QuantityChange = quantityChange;
        Kind = kind;
        OccurredAt = occurredAt;
        Reference = reference;
        BatchId = batch?.Id;
        BatchLot = batch?.Lot;
    }
}
