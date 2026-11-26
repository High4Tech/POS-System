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
        var candidates = db.Sales.AsNoTracking();
        var recorded = await candidates.Where(s => s.CustomerAccountKey == key)
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id).Take(500)
            .Select(s => new { s.Id, s.CreatedAt }).ToListAsync(ct);
        var legacy = await candidates.Where(s => s.CustomerAccountKey == null && s.CustomerName != null &&
            (s.CustomerPhone != null || s.CustomerEmail != null))
            .Select(s => new { s.Id, s.CreatedAt, s.CustomerName, s.CustomerPhone, s.CustomerEmail }).ToListAsync(ct);
        var ids = recorded.Concat(legacy.Where(s => Sale.CustomerIdentity(
                SaleCustomer.Create(s.CustomerName, s.CustomerPhone, s.CustomerEmail, null)) == key)
            .Select(s => new { s.Id, s.CreatedAt })).OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
            .Take(500).Select(s => s.Id).ToArray();
        if(ids.Length==0)return [];
        return await db.Sales.AsNoTracking().Include(s => s.Lines).Where(s => ids.Contains(s.Id))
            .OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id).ToListAsync(ct);
    }
}
