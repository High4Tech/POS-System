using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Business;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Infrastructure.Persistence;

namespace SupermarketPos.Infrastructure.Business;

public sealed class ReturnsService(PosDbContext db, IUnitOfWork work, IAuthService auth)
{
    public async Task<IReadOnlyList<SaleReturn>> HistoryAsync(int saleId, CancellationToken ct = default) =>
        await db.SaleReturns.AsNoTracking().Where(r => r.SaleId == saleId).OrderByDescending(r => r.Id).ToListAsync(ct);

    public async Task<IReadOnlyList<SaleReturn>> PostedBetweenAsync(DateTime from,DateTime to,CancellationToken ct=default) =>
        await db.SaleReturns.AsNoTracking().Where(r=>r.Status=="Posted" && r.PostedAt>=from && r.PostedAt<to).ToListAsync(ct);

    public async Task<Dictionary<int,(decimal Gross, decimal Quantity)>> PostedTotalsAsync(int[] ids, CancellationToken ct = default)
    {
        var rows = await db.SaleReturns.AsNoTracking().Where(r=>r.Status=="Posted" && ids.Contains(r.SaleId))
            .ToListAsync(ct);
        return rows.GroupBy(r=>r.SaleId).ToDictionary(g=>g.Key,g=>(g.Sum(r=>r.Gross),g.SelectMany(Items).Sum(line=>line.Quantity)));
    }

    public static ReturnedLine[] Items(SaleReturn record) => JsonSerializer.Deserialize<ReturnedLine[]>(record.ItemsJson) ?? [];

    public Task<SaleReturn> RecordAsync(int saleId, IReadOnlyList<ReturnSelection> selection, string reason,
        bool cashHandedOver, Guid confirmationId, CancellationToken ct = default) => work.ExecuteInTransactionAsync(async token =>
    {
        auth.RequireAdmin();
        reason = reason.Trim();
        if (confirmationId == Guid.Empty || reason.Length is < 1 or > 300 || reason.Any(char.IsControl))
            throw new DomainException("Trade_ReturnInvalid");
        if (await db.SaleReturns.SingleOrDefaultAsync(r => r.ConfirmationId == confirmationId, token) is { } saved)
        {
            var savedItems = Items(saved);
            if (saved.SaleId != saleId || saved.Reason != reason ||
                !savedItems.Select(l=>new ReturnSelection(l.SaleLineId,l.Quantity,l.Restock)).SequenceEqual(selection.Select(l=>l with {Identity=null})) ||
                savedItems.Zip(selection).Any(pair => pair.First.VerifiedIdentity is { } identity &&
                    !string.Equals(identity, pair.Second.Identity?.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new DomainException("Trade_ReturnInvalid");
            return saved;
        }
        var sale = await LoadSaleAsync(saleId, token);
        var profile = await ReadProfileAsync(token);
        if (profile.FbrRequired) throw new DomainException("Setup_RequiredIntegration");
        var history = await HistoryAsync(saleId,token);
        if (history.Any(r=>r.Status=="Pending")) throw new DomainException("Trade_ReturnPending");
        if (selection.Count == 0 || selection.Select(l=>l.SaleLineId).Distinct().Count()!=selection.Count)
            throw new DomainException("Trade_ReturnInvalid");

        var prior = history.Where(r=>r.Status=="Posted").SelectMany(Items).ToArray();
        var lines = new List<ReturnedLine>();
        foreach (var selected in selection)
        {
            var original = sale.Lines.SingleOrDefault(l=>l.Id==selected.SaleLineId) ?? throw new DomainException("Trade_ReturnInvalid");
            if (original.Imei1 is not null || original.Imei2 is not null || original.SerialNumber is not null && original.BatchId is null)
            {
                var identity = selected.Identity?.Trim();
                if (string.IsNullOrEmpty(identity) || !new[]{original.SerialNumber,original.Imei1,original.Imei2}.Any(id=>string.Equals(id,identity,StringComparison.OrdinalIgnoreCase)))
                    throw new DomainException("Trade_ReturnIdentity");
            }
            else if (original.BatchLot is { } lot && !string.Equals(lot,selected.Identity?.Trim(),StringComparison.OrdinalIgnoreCase))
                throw new DomainException("Trade_ReturnIdentity");
            var already = prior.Where(l=>l.SaleLineId==selected.SaleLineId).Sum(l=>l.Quantity);
            if (selected.Quantity<=0 || selected.Quantity+already>original.Quantity ||
                selected.Quantity!=decimal.Round(selected.Quantity, original.UnitCode is "kg" or "litre" ? 3 : 0))
                throw new DomainException("Trade_ReturnInvalid");
            var baseQuantity = original.BaseQuantity * selected.Quantity / original.Quantity;
            if (baseQuantity!=decimal.Truncate(baseQuantity) || original.SerialNumber is not null && selected.Quantity != original.Quantity)
                throw new DomainException("Trade_ReturnInvalid");
            var gross = Money.Round2(original.LineGross*(already+selected.Quantity)/original.Quantity)-Money.Round2(original.LineGross*already/original.Quantity);
            var tax = Money.Round2(original.VatAmount*(already+selected.Quantity)/original.Quantity)-Money.Round2(original.VatAmount*already/original.Quantity);
            var disposition = selected.Restock ? "Restocked" : profile.IsPharmacy ? "Quarantined" : "NotRestocked";
            lines.Add(new(original.Id,original.ProductId,original.Name,original.UnitCode,selected.Quantity,(long)baseQuantity,gross,tax,
                selected.Restock,original.BatchId,original.BatchLot,original.BatchExpiry,original.SerialNumber,original.StockLocation,
                disposition, string.IsNullOrWhiteSpace(selected.Identity) ? null : selected.Identity.Trim()));
        }
        var result = new SaleReturn
        {
            ConfirmationId=confirmationId, SaleId=sale.Id, CreatedAt=DateTime.Now, Operator=auth.CurrentUser!.DisplayName,
            Reason=reason, Method=sale.PaymentMethod, Status="Pending", Gross=lines.Sum(l=>l.Gross),
            Tax=lines.Sum(l=>l.Tax), ItemsJson=JsonSerializer.Serialize(lines)
        };
        db.SaleReturns.Add(result);
        if (result.Method != "CARD")
        {
            if (result.Method=="CASH" && result.Gross>0 && !cashHandedOver) throw new DomainException("Trade_ConfirmCashRefund");
            await PostAsync(sale,result,lines,token);
        }
        else if (lines.Any(l=>l.Restock)) throw new DomainException("Trade_ReturnQuarantine");
        await db.SaveChangesAsync(token);
        return result;
    },ct);

    public Task ResolveCardAsync(int id, bool confirmed, string reference, CancellationToken ct = default) => work.ExecuteInTransactionAsync(async token =>
    {
        auth.RequireAdmin();
        reference = reference.Trim();
        if(reference.Length is <1 or >150 || reference.Any(char.IsControl)) throw new DomainException("Trade_ReturnInvalid");
        var record = await db.SaleReturns.SingleAsync(r=>r.Id==id,token);
        if(record.Method!="CARD") throw new DomainException("Trade_ReturnInvalid");
        var target = confirmed ? "Posted" : "Failed";
        if(record.Status==target && record.ExternalReference==reference) return true;
        if(record.Status!="Pending") throw new DomainException("Trade_ReturnInvalid");
        if((await ReadProfileAsync(token)).FbrRequired) throw new DomainException("Setup_RequiredIntegration");
        record.ExternalReference=reference;
        if (confirmed) await PostAsync(await LoadSaleAsync(record.SaleId,token),record,Items(record),token);
        else record.Status="Failed";
        await db.SaveChangesAsync(token);
        return true;
    },ct);

    private async Task<BusinessProfile> ReadProfileAsync(CancellationToken ct)
    {
        var profile=await db.BusinessConfigurations.AsNoTracking().Select(c=>c.ProfileJson).SingleOrDefaultAsync(ct);
        return profile is null ? new() : JsonSerializer.Deserialize<BusinessProfile>(profile) ?? new();
    }
    private async Task<Sale> LoadSaleAsync(int id,CancellationToken ct) =>
        await db.Sales.Include(s=>s.Lines).SingleOrDefaultAsync(s=>s.Id==id,ct) ?? throw new DomainException("Trade_ReturnInvalid");

    private async Task PostAsync(Sale sale, SaleReturn record, IEnumerable<ReturnedLine> lines, CancellationToken ct)
    {
        var profile=await ReadProfileAsync(ct);
        foreach(var line in lines.Where(l=>l.Restock))
        {
            var original=sale.Lines.Single(l=>l.Id==line.SaleLineId);
            var product=await db.Products.Include(p=>p.Batches).SingleOrDefaultAsync(p=>p.Id==line.ProductId,ct);
            if (product is null || !product.IsActive || !product.StockTracked || original.StockUntracked || product.TrackSerial || product.TrackExpiry ||
                original.SerialNumber is not null || original.BatchId.HasValue || line.Location is not null || line.BaseQuantity<=0 ||
                sale.SourceReceiptNo is not null || product.DefaultLocationId.HasValue ||
                profile.IsPharmacy && (product.MedicineReferenceId.HasValue || product.MedicineForm.Length>0 || product.SellingUnit!="piece") ||
                await db.StockPlacements.AnyAsync(p=>p.ProductId==product.Id && p.BaseQuantity>0,ct))
                throw new DomainException("Trade_ReturnQuarantine");
            if ((line.Unit=="kg") != (product.Unit==Unit.Kilogram) || (line.Unit=="litre") != (product.Unit==Unit.Litre))
                throw new DomainException("Trade_ReturnQuarantine");
            var quantity=MeasuredGoods.Quantity(line.BaseQuantity,product.Unit);
            product.Restock(quantity);
            db.StockMovements.Add(new(product.Id,product.Name,quantity,StockMovementKind.SaleReturn,DateTime.Now,sale.ReceiptNo));
        }
        switch(record.Method)
        {
            case "CASH": record.CashRefund=record.Gross; break;
            case "CARD": record.CardRefund=record.Gross; break;
            case "CREDIT":
                record.ReceivableReduction=Math.Min(sale.Outstanding,record.Gross);
                record.CustomerCredit=record.Gross-record.ReceivableReduction;
                sale.ApplyReturnCredit(record.ReceivableReduction);
                break;
            default: throw new DomainException("Trade_ReturnInvalid");
        }
        record.Status="Posted";
        record.PostedAt=DateTime.Now;
    }
}
