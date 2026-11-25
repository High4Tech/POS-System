using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Pricing;

namespace SupermarketPos.Domain.Entities;

public sealed class AccountPayment
{
    public int Id { get; private set; }
    public Guid ConfirmationId { get; private set; }
    public int? SaleId { get; private set; }
    public int? PurchaseId { get; private set; }
    public decimal Amount { get; private set; }
    public string Method { get; private set; } = "";
    public DateTime PaidOn { get; private set; }
    public DateTime RecordedAt { get; private set; }
    public string Reference { get; private set; } = "";
    public string Operator { get; private set; } = "";

    private AccountPayment() { }

    public AccountPayment(Guid confirmationId, int? saleId, int? purchaseId, decimal amount, string method,
        DateTime paidOn, string? reference, string recordedBy)
    {
        if (confirmationId == Guid.Empty || saleId.HasValue == purchaseId.HasValue || amount <= 0 || amount != Money.Round2(amount))
            throw new DomainException("Enter a positive payment with up to two decimals and select an invoice.");
        if (!Payment.RecordedMethods.Contains(method)) throw new DomainException("Select a payment method.");
        if (paidOn.Date > DateTime.Today) throw new DomainException("A payment date cannot be in the future.");
        ConfirmationId = confirmationId;
        SaleId = saleId;
        PurchaseId = purchaseId;
        Amount = amount;
        Method = method;
        PaidOn = paidOn.Date;
        RecordedAt = DateTime.Now;
        Reference = reference?.Trim() ?? "";
        if (Reference.Length > 150) throw new DomainException("A payment reference can contain up to 150 characters.");
        Operator = recordedBy;
    }
}

public sealed record UnitIdentifiers(string Serial, string? Imei1 = null, string? Imei2 = null)
{
    public static UnitIdentifiers Create(string? serial, string? imei1 = null, string? imei2 = null)
    {
        string? Imei(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var cleaned = value.Trim();
            if (cleaned.Length != 15 || !cleaned.All(char.IsAsciiDigit)) throw new DomainException("Each IMEI must contain 15 digits.");
            return cleaned;
        }
        var first = Imei(imei1);
        var second = Imei(imei2);
        var identifier = (string.IsNullOrWhiteSpace(serial) ? first : serial)?.Trim().ToUpperInvariant();
        if (identifier is null || identifier.Length > 64 || identifier.Any(char.IsControl))
            throw new DomainException("Enter a serial number of up to 64 characters or an IMEI.");
        if (first is not null && second == first) throw new DomainException("The two IMEIs must be different.");
        return new UnitIdentifiers(identifier, first, second);
    }

    public IEnumerable<string> All => new[] { Serial, Imei1, Imei2 }.OfType<string>().Distinct(StringComparer.Ordinal);
}

public sealed class SerializedUnit
{
    public int Id { get; private set; }
    public int ProductId { get; private set; }
    public string Serial { get; private set; } = "";
    public string? Imei1 { get; private set; }
    public string? Imei2 { get; private set; }
    public int? PurchaseId { get; private set; }
    public int? SaleId { get; private set; }
    public DateTime ReceivedOn { get; private set; }
    public DateTime? SoldOn { get; private set; }
    public DateTime? WarrantyUntil { get; private set; }

    private SerializedUnit() { }

    public SerializedUnit(int productId, UnitIdentifiers identifiers, int? purchaseId, DateTime receivedOn)
    {
        var checkedIds = UnitIdentifiers.Create(identifiers.Serial, identifiers.Imei1, identifiers.Imei2);
        ProductId = productId;
        Serial = checkedIds.Serial;
        Imei1 = checkedIds.Imei1;
        Imei2 = checkedIds.Imei2;
        PurchaseId = purchaseId;
        ReceivedOn = receivedOn;
    }

    public void Sell(int saleId, DateTime soldOn, int warrantyMonths)
    {
        if (SaleId.HasValue) throw new DomainException("This serial or IMEI has already been sold.");
        SaleId = saleId;
        SoldOn = soldOn;
        WarrantyUntil = warrantyMonths > 0 ? soldOn.Date.AddMonths(warrantyMonths) : null;
    }
}
