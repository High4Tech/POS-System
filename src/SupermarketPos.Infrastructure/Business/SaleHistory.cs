using Microsoft.EntityFrameworkCore;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Persistence;

namespace SupermarketPos.Infrastructure.Business;

public sealed class SaleHistory(PosDbContext db)
{
    public const int InvoicePageSize = 100;

    public async Task<IReadOnlyList<Sale>> SearchAsync(DateTime from, DateTime to, string text, string payment, string status,
        decimal minimum, decimal maximum, int page, CancellationToken ct = default)
    {
        var term = text.Trim().ToLowerInvariant();
        return await db.Sales.FromSqlInterpolated($"""
            SELECT * FROM Sales s
            WHERE s.CreatedAt >= {from} AND s.CreatedAt < {to}
                AND ({term} = '' OR instr(lower(s.ReceiptNo), {term}) > 0
                     OR instr(lower(coalesce(s.SourceReceiptNo,'')), {term}) > 0
                     OR instr(lower(coalesce(s.CustomerName,'')), {term}) > 0
                     OR instr(lower(coalesce(s.CustomerPhone,'')), {term}) > 0
                     OR instr(lower(s.CashierName), {term}) > 0)
                AND ({payment} = '' OR s.PaymentMethod = {payment})
                AND cast(s.GrossTotal AS NUMERIC) BETWEEN cast({minimum} AS NUMERIC) AND cast({maximum} AS NUMERIC)
                AND ({status} = ''
                     OR {status} = 'returned' AND EXISTS(SELECT 1 FROM SaleReturns r WHERE r.SaleId = s.Id AND r.Status = 'Posted')
                     OR {status} = 'open' AND round(s.GrossTotal - s.AmountPaid - s.BalancePaid - s.ReturnCreditApplied,2) > 0
                     OR {status} = 'paid' AND round(s.GrossTotal - s.AmountPaid - s.BalancePaid - s.ReturnCreditApplied,2) <= 0)
            """).AsNoTracking().OrderByDescending(s=>s.CreatedAt).ThenByDescending(s=>s.Id)
            .Skip(page * InvoicePageSize).Take(InvoicePageSize + 1).Include(s=>s.Lines).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Sale>> ForProductAsync(int id, CancellationToken ct = default) =>
        await db.Sales.AsNoTracking().Include(s => s.Lines).Where(s => s.Lines.Any(l => l.ProductId == id))
            .OrderByDescending(s => s.CreatedAt).Take(500).ToListAsync(ct);

    public async Task<IReadOnlyList<Sale>> ForCustomerAsync(SaleCustomer customer, CancellationToken ct = default)
    {
        var key = Sale.CustomerIdentity(customer);
        if (key is null) return [];
        var candidates = await db.Sales.AsNoTracking().Where(s => s.CustomerAccountKey == key ||
            (s.CustomerAccountKey == null && (s.CustomerPhone != null || s.CustomerEmail != null)))
            .OrderByDescending(s => s.CreatedAt).ToListAsync(ct);
        var ids = candidates.Where(s => (s.CustomerAccountKey ?? Sale.CustomerIdentity(s.Customer)) == key).Take(500).Select(s => s.Id).ToArray();
        return await db.Sales.AsNoTracking().Include(s => s.Lines).Where(s => ids.Contains(s.Id))
            .OrderByDescending(s => s.CreatedAt).ToListAsync(ct);
    }
}
