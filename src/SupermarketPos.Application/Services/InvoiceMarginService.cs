using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Pricing;

namespace SupermarketPos.Application.Services;

public sealed record InvoiceMargin(decimal NetSales, decimal? Cost, int MissingCosts)
{
    public decimal? Profit => Cost is { } cost ? Money.Round2(NetSales - cost) : null;
    public decimal? Percent => Profit is { } profit && NetSales > 0 ? Money.Round2(profit / NetSales * 100) : null;
}

public sealed class InvoiceMarginService(IAuthService auth, ISaleRepository sales)
{
    public async Task<InvoiceMargin> ForSaleAsync(int id, CancellationToken ct = default)
    {
        auth.RequireAdmin();
        var sale = await sales.GetByIdAsync(id, ct) ?? throw new DomainException("Reports_LoadError");
        return Calculate(sale.NetTotal, sale.Lines.Select(line => line.Quantity * line.UnitCost));
    }

    public InvoiceMargin ForCart(Cart cart)
    {
        auth.RequireAdmin();
        var summary = PricingService.Summarize(cart);
        return Calculate(summary.NetTotal, cart.Lines.Select(line =>
            line.Product.StockTracked ? line.Quantity * line.Product.PurchaseCost : null));
    }

    private static InvoiceMargin Calculate(decimal net, IEnumerable<decimal?> lineCosts)
    {
        var costs = lineCosts.ToArray();
        var missing = costs.Count(cost => cost is null);
        return new(net, missing > 0 ? null : Money.Round2(costs.Sum() ?? 0), missing);
    }
}
