using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Business;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Infrastructure.Persistence;

namespace SupermarketPos.Infrastructure.Business;

// A sandbox mapper, never a production fiscalization provider. No network traffic or invented FBR IDs.
public sealed class MockFbrAdapter(string route) : IFbrAdapter
{
    public string Route => route;
    public bool IsProductionAuthorized => false;
    public Task<FiscalValidation> ValidateAsync(BusinessProfile profile, string invoiceJson, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (Route is not ("pos" or "digital") || profile.FbrEnvironment != "sandbox" || profile.FbrProvider != "mock" || profile.FbrRoute != Route) errors.Add("Setup_ProductionPending");
        if (!profile.SalesTaxRegistered || profile.Strn.Length != 13) errors.Add("Setup_StrnInvalid");
        try
        {
            using var json=JsonDocument.Parse(invoiceJson);var root=json.RootElement;
            if(root.GetProperty("schema").GetInt32()!=1||string.IsNullOrWhiteSpace(root.GetProperty("localInvoice").GetString())||root.GetProperty("route").GetString()!=Route||root.GetProperty("environment").GetString()!="sandbox")throw new JsonException();
            var lines=root.GetProperty("lines");if(lines.ValueKind!=JsonValueKind.Array||lines.GetArrayLength()==0)throw new JsonException();
            decimal gross=0;
            foreach(var line in lines.EnumerateArray())
            {
                var quantity=line.GetProperty("Quantity").GetDecimal();var rate=line.GetProperty("VatRate").GetDecimal();var amount=line.GetProperty("LineGross").GetDecimal();
                if(quantity<=0||rate<0||rate>1||amount<0||line.GetProperty("NetAmount").GetDecimal()+line.GetProperty("VatAmount").GetDecimal()!=amount)throw new JsonException();
                gross+=amount;
            }
            if(gross!=root.GetProperty("total").GetDecimal()||root.GetProperty("net").GetDecimal()+root.GetProperty("tax").GetDecimal()!=gross)throw new JsonException();
        }
        catch (Exception e) when(e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { errors.Add("Tax_MappingInvalid"); }
        return Task.FromResult(new FiscalValidation(errors.Count == 0, errors.Count == 0 ? "sandbox-validated" : "attention", errors));
    }
}

public sealed class FbrCompliance(PosDbContext db) : IBusinessCompliance
{
    public async Task ValidateCheckoutAsync(Cart cart, CancellationToken ct)
    {
        var row = await db.BusinessConfigurations.AsNoTracking().SingleOrDefaultAsync(ct);
        if (row is null) return; // legacy database; setup never modifies existing checkout data
        var profile = JsonSerializer.Deserialize<BusinessProfile>(row.ProfileJson)!;
        if(profile.IsPharmacy)
        {
            var productIds=cart.Lines.Select(l=>l.Product.Id).ToArray();
            var products=await db.Products.AsNoTracking().Where(p=>productIds.Contains(p.Id)).ToArrayAsync(ct);
            if(products.Any(p=>p.Unit!=SupermarketPos.Domain.Common.Unit.Piece))throw new BusinessValidationException("Pharmacy_PackedOnly");
            if(products.Any(p=>p.TrackSerial||p.WarrantyMonths>0))throw new BusinessValidationException("Pharmacy_TraceabilityRequired");
        }
        if (profile.FbrRequired) throw new BusinessValidationException("Setup_RequiredIntegration");
        if (!profile.Registered) return;
        var ids = cart.Lines.Select(l => l.Product.Id).ToArray();
        var date = DateTime.Today;
        var rules = await db.ProductTaxRules.AsNoTracking().Where(r => ids.Contains(r.ProductId) && r.EffectiveFrom <= date && (r.EffectiveTo == null || r.EffectiveTo >= date)).ToArrayAsync(ct);
        foreach (var line in cart.Lines)
            if (rules.SingleOrDefault(r => r.ProductId == line.Product.Id) is { } rule && rule.Rate != line.VatRate)
                throw new BusinessValidationException("Tax_RateMismatch");
    }
    public async Task CaptureAsync(Sale sale, CancellationToken ct)
    {
        foreach(var line in sale.Lines.Where(l=>l.StockUntracked))
            db.StockReconciliations.Add(new StockReconciliation{ProductId=line.ProductId,SaleLineId=line.Id,CreatedAt=sale.CreatedAt});
        var row = await db.BusinessConfigurations.AsNoTracking().SingleOrDefaultAsync(ct);
        if (row is null) return;
        var profile = JsonSerializer.Deserialize<BusinessProfile>(row.ProfileJson)!;
        sale.CaptureInvoiceProfile(JsonSerializer.Serialize(profile));
        if (!profile.Registered) return;
        var ids = sale.Lines.Select(l => l.ProductId).ToArray();
        var date = sale.CreatedAt.Date;
        var rules = await db.ProductTaxRules.AsNoTracking().Where(r => ids.Contains(r.ProductId) && r.EffectiveFrom <= date && (r.EffectiveTo == null || r.EffectiveTo >= date)).ToArrayAsync(ct);
        var submission=new FiscalSubmission { SaleId = sale.Id, Route = profile.FbrRoute, Environment = profile.FbrEnvironment,
            Status = profile.FbrStatus == "sandbox" ? "sandbox-pending" : "not-configured", InvoiceJson = Map(sale, profile, rules) };
        if(profile.FbrStatus=="sandbox"&&profile.FbrProvider=="mock")
        {
            var validation=await new MockFbrAdapter(profile.FbrRoute).ValidateAsync(profile,submission.InvoiceJson,ct);
            submission.Status=validation.Status;submission.ValidationCodes=JsonSerializer.Serialize(validation.Errors);
        }
        db.FiscalSubmissions.Add(submission);
    }
    public static string Map(Sale sale, BusinessProfile profile, IReadOnlyList<ProductTaxRule> rules) => JsonSerializer.Serialize(new
    {
        schema = 1, localInvoice = sale.ReceiptNo, route = profile.FbrRoute, environment = profile.FbrEnvironment, businessProfile=profile,
        legalName = profile.LegalName, profile.StoreName, profile.Address, profile.Ntn, profile.Strn, profile.Terminal,
        date = sale.CreatedAt, total = sale.GrossTotal, net = sale.NetTotal, tax = sale.VatTotal,
        lines = sale.Lines.Select(l => new { l.ProductId, l.Name, l.Quantity, l.UnitPrice, l.VatRate, l.LineGross, l.NetAmount, l.VatAmount,
            taxRule = rules.SingleOrDefault(r => r.ProductId == l.ProductId) })
    });
}
