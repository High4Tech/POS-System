using Microsoft.EntityFrameworkCore;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Persistence;

namespace SupermarketPos.Infrastructure.Business;

public sealed class SaleHistory(PosDbContext db)
{
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
