using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Pricing;

namespace SupermarketPos.Domain.Entities;

/// <summary>
/// A completed, persisted sale. Built from a resolved <see cref="SaleSummary"/> plus a
/// <see cref="Payment"/>, it stores immutable line snapshots, the headline totals, and the
/// payment outcome. The VAT breakdown is recomputed from the line snapshots on demand.
/// </summary>
public sealed class Sale
{
    private readonly List<SaleLine> _lines = new();

    public int Id { get; private set; }
    public string ReceiptNo { get; private set; } = string.Empty;  // e.g. 2026-000123
    public string? SourceReceiptNo { get; private set; }
    public string? InvoiceProfileJson{get;private set;}
    public void CaptureInvoiceProfile(string json){if(InvoiceProfileJson is not null)throw new DomainException("Invoice snapshot is already recorded.");InvoiceProfileJson=json;}
    public DateTime CreatedAt { get; private set; }
    public int CashierId { get; private set; }
    public string CashierName { get; private set; } = string.Empty;

    public IReadOnlyList<SaleLine> Lines => _lines;

    public decimal GrossTotal { get; private set; }
    public decimal NetTotal { get; private set; }
    public decimal VatTotal { get; private set; }

    public string PaymentMethod { get; private set; } = string.Empty; // CASH | CARD
    public decimal AmountPaid { get; private set; }
    public decimal BalancePaid { get; private set; }
    public int BalanceRevision { get; private set; }
    public decimal ReturnCreditApplied { get; private set; }
    public decimal Outstanding => Money.Round2(GrossTotal - AmountPaid - BalancePaid - ReturnCreditApplied);

    public void ApplyReturnCredit(decimal amount)
    {
        if (amount < 0 || amount > Outstanding || amount != Money.Round2(amount))
            throw new DomainException("Trade_ReturnInvalid");
        ReturnCreditApplied += amount;
        BalanceRevision++;
    }
    public string? CustomerAccountKey { get; private set; }
    public DateTime? CreditDueDate{get;private set;}
    public string CreditNote{get;private set;}="";
    public decimal? CashTendered { get; private set; }
    public decimal? ChangeGiven { get; private set; }
    public string? CustomerName { get; private set; }
    public string? CustomerPhone { get; private set; }
    public string? CustomerEmail { get; private set; }
    public string? CustomerAddress { get; private set; }
    public SaleCustomer? Customer => SaleCustomer.Create(CustomerName, CustomerPhone, CustomerEmail, CustomerAddress);
    public decimal? GrossProfit => _lines.Count > 0 && _lines.All(line => line.UnitCost.HasValue)
        ? Money.Round2(NetTotal - _lines.Sum(line => line.Quantity * line.UnitCost!.Value))
        : null;

    private Sale() { } // EF

    public Sale(string receiptNo, int cashierId, string cashierName,
                IEnumerable<SaleLine> lines, decimal grossTotal, decimal netTotal, decimal vatTotal,
                Payment payment, DateTime createdAt, SaleCustomer? customer = null, string? sourceReceiptNo = null)
    {
        if (string.IsNullOrWhiteSpace(receiptNo))
            throw new DomainException("Receipt number is required.");
        ReceiptNo = receiptNo;
        SourceReceiptNo = sourceReceiptNo;
        CashierId = cashierId;
        CashierName = cashierName;
        _lines.AddRange(lines);
        if (_lines.Count == 0) throw new DomainException("A sale must have at least one line.");
        GrossTotal = Money.Round2(grossTotal);
        NetTotal = Money.Round2(netTotal);
        VatTotal = Money.Round2(vatTotal);
        CreatedAt = createdAt;
        SetCustomer(customer);

        PaymentMethod = payment.Method;
        AmountPaid = payment.Amount;
        if (payment is CreditPayment credit)
        {
            CreditDueDate=credit.DueDate;CreditNote=credit.Note;
            CustomerAccountKey = CustomerIdentity(customer);
            if (CustomerAccountKey is null) throw new DomainException("Credit sales require a customer name and phone number or email.");
        }
        if (payment is CashPayment cash)
        {
            CashTendered = cash.Tendered;
            ChangeGiven = cash.Change;
        }
    }

    public void SetCustomer(SaleCustomer? details)
    {
        var customer = SaleCustomer.Create(details?.Name, details?.Phone, details?.Email, details?.Address);
        CustomerName = customer?.Name;
        CustomerPhone = customer?.Phone;
        CustomerEmail = customer?.Email;
        CustomerAddress = customer?.Address;
    }

    public static string? CustomerIdentity(SaleCustomer? customer)
    {
        if (string.IsNullOrWhiteSpace(customer?.Name)) return null;
        if (!string.IsNullOrWhiteSpace(customer.Email)) return "email:" + customer.Email.Trim().ToUpperInvariant();
        var digits = new string(customer.Phone?.Where(char.IsAsciiDigit).ToArray() ?? []);
        return digits.Length >= 7 ? "phone:" + digits : null;
    }

    public void RecordBalancePayment(decimal amount)
    {
        if (amount <= 0 || amount != Money.Round2(amount) || amount > Outstanding)
            throw new DomainException("The payment must be positive and cannot exceed the outstanding balance.");
        BalancePaid += amount;
        BalanceRevision++;
    }

    /// <summary>Per-rate VAT breakdown, recomputed from the persisted line snapshots.</summary>
    public IReadOnlyList<VatGroup> VatBreakdown =>
        PricingService.ExtractGroups(_lines.Select(l => (l.VatRate, l.LineGross)));
}
