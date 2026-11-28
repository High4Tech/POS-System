using SupermarketPos.Domain.Entities;

namespace SupermarketPos.Application.Abstractions;

public interface ITradeLedger
{
    Task<IReadOnlyList<AccountPayment>> PaymentsAsync(int invoiceId, bool purchase, CancellationToken ct = default);
    Task RecordPaymentAsync(int invoiceId, bool purchase, decimal amount, string method, DateTime paidOn,
        string? reference, User recordedBy, Guid confirmationId, CancellationToken ct = default);
    Task<IReadOnlyList<SerialRecord>> SerialsAsync(int? productId = null, CancellationToken ct = default);
    Task RegisterExistingAsync(int productId, IReadOnlyList<UnitIdentifiers> units, CancellationToken ct = default);
    Task ReceiveUnitsAsync(Product product, IReadOnlyList<UnitIdentifiers> units, int purchaseId, DateTime receivedOn, CancellationToken ct,decimal? purchaseCost=null);
    Task<SerializedUnit> AvailableUnitAsync(int productId, string serial, CancellationToken ct);
    Task<(Product Product, string Serial)?> FindUnitAsync(string identifier, CancellationToken ct = default);
    Task ValidateProductTrackingAsync(Product product, CancellationToken ct = default);
}

public sealed record SerialRecord(SerializedUnit Unit, string ProductName, string? ReceiptNo, string? CustomerName,
    string? CustomerPhone, string? CustomerEmail, string Brand="", string Model="");
