using SupermarketPos.Domain.Common;

namespace SupermarketPos.Domain.Pricing;

/// <summary>
/// Payment strategy (polymorphism #2). Each concrete payment knows how to settle a sale total
/// and reports its method. Cash additionally computes change from the tendered amount.
/// </summary>
public abstract class Payment
{
    public decimal Amount { get; protected set; } // amount applied to the sale
    public abstract string Method { get; }

    public static IReadOnlyList<string> RecordedMethods { get; } =
        Array.AsReadOnly(new[] { "CASH", "CARD", "BANK_TRANSFER", "WALLET", "OTHER" });

    public static string LabelKey(string method) => method switch
    {
        "CASH" => "Payment_Cash",
        "CARD" => "Payment_Card",
        "BANK_TRANSFER" => "Payment_Transfer",
        "WALLET" => "Payment_Wallet",
        "CREDIT" => "Payment_Credit",
        _ => "Payment_Other"
    };

    public static Payment Record(string method, decimal total, decimal? cashTendered)
    {
        if (!RecordedMethods.Contains(method) || (method != "CASH" && cashTendered.HasValue))
            throw new DomainException("Select a valid payment method and enter cash tendered only for cash sales.");
        return method == "CASH" ? new CashPayment(total, cashTendered ?? total) : new RecordedPayment(method, total);
    }

    private sealed class RecordedPayment : Payment
    {
        public RecordedPayment(string method, decimal total)
        {
            if (total < 0) throw new DomainException("Total cannot be negative.");
            Method = method;
            Amount = Money.Round2(total);
        }

        public override string Method { get; }
    }
}

public sealed class CreditPayment : Payment
{
    public decimal Total { get; }
    public DateTime? DueDate{get;}
    public string Note{get;}

    public CreditPayment(decimal total, decimal paid,DateTime? dueDate=null,string? note=null)
    {
        if (paid < 0 || paid >= total || paid != Money.Round2(paid))
            throw new DomainException("A credit sale needs an amount paid of zero or more, below the total.");
        Amount = paid;
        Total = Money.Round2(total);
        Note=note?.Trim()??"";if(Note.Length>500||Note.Any(c=>char.IsControl(c)&&c is not ('\r' or '\n')))throw new DomainException("Validation_Text");DueDate=dueDate?.Date;
    }

    public override string Method => "CREDIT";
}

public sealed class CashPayment : Payment
{
    public decimal Tendered { get; }
    public decimal Change { get; }

    public CashPayment(decimal total, decimal tendered)
    {
        if (total < 0) throw new DomainException("Total cannot be negative.");
        if (tendered < total) throw new DomainException("Insufficient cash tendered.");
        Amount = Money.Round2(total);
        Tendered = Money.Round2(tendered);
        Change = Money.Round2(tendered - total);
    }

    public override string Method => "CASH";
}

public sealed class CardPayment : Payment
{
    public CardPayment(decimal total)
    {
        if (total < 0) throw new DomainException("Total cannot be negative.");
        Amount = Money.Round2(total);
        // Simulated approval — no real terminal integration.
    }

    public override string Method => "CARD";
}
