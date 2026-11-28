using Microsoft.EntityFrameworkCore;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;

namespace SupermarketPos.Infrastructure.Business;

public sealed partial class ReturnsService
{
    public async Task<bool> DeviceReturnsAvailableAsync(CancellationToken ct=default)=>(await ReadProfileAsync(ct)).DeviceTrackingAvailable;

    public async Task<IReadOnlyList<DeviceReturnRelease>> DeviceReleasesAsync(int saleId,CancellationToken ct=default)=>
        await db.DeviceReturnReleases.AsNoTracking().Where(r=>db.SaleLines.Any(l=>l.Id==r.SaleLineId&&l.SaleId==saleId)).ToArrayAsync(ct);

    public Task<DeviceReturnRelease> ReleaseDeviceAsync(int returnId,int saleLineId,string identity,string inspection,Guid confirmationId,CancellationToken ct=default)=>
        work.ExecuteInTransactionAsync(async token=>
        {
            auth.RequireAdmin();identity=identity.Trim();inspection=inspection.Trim();
            if(confirmationId==Guid.Empty||identity.Length is <1 or >64||inspection.Length is <1 or >300||inspection.Any(char.IsControl))throw new DomainException("Device_ReturnInvalid");
            var existing=await db.DeviceReturnReleases.SingleOrDefaultAsync(r=>r.ConfirmationId==confirmationId,token);
            if(existing is not null)
            {
                var original=await db.SaleLines.AsNoTracking().SingleAsync(l=>l.Id==existing.SaleLineId,token);
                if(existing.SaleReturnId!=returnId||existing.SaleLineId!=saleLineId||existing.Inspection!=inspection||!new[]{original.SerialNumber,original.Imei1,original.Imei2}.Any(s=>string.Equals(s,identity,StringComparison.OrdinalIgnoreCase)))throw new DomainException("Device_ReturnInvalid");
                return existing;
            }
            var profile=await ReadProfileAsync(token);
            if(profile.IsPharmacy||!profile.DeviceTrackingAvailable)throw new DomainException("Trade_DeviceUnavailable");
            var record=await db.SaleReturns.AsNoTracking().SingleOrDefaultAsync(r=>r.Id==returnId&&r.Status=="Posted",token)??throw new DomainException("Device_ReturnInvalid");
            var sale=await LoadSaleAsync(record.SaleId,token);var line=sale.Lines.SingleOrDefault(l=>l.Id==saleLineId)??throw new DomainException("Device_ReturnInvalid");
            if(line.SerialNumber is null||line.BatchId.HasValue||sale.SourceReceiptNo is not null||
                !Items(record).Any(l=>l.SaleLineId==saleLineId)||!new[]{line.SerialNumber,line.Imei1,line.Imei2}.Any(s=>string.Equals(s,identity,StringComparison.OrdinalIgnoreCase))||
                (await HistoryAsync(sale.Id,token)).Where(r=>r.Status=="Posted").SelectMany(Items).Where(l=>l.SaleLineId==saleLineId).Sum(l=>l.Quantity)!=line.Quantity||
                await db.DeviceReturnReleases.AnyAsync(r=>r.SaleLineId==saleLineId,token))throw new DomainException("Device_ReturnInvalid");
            var product=await db.Products.SingleAsync(p=>p.Id==line.ProductId,token);
            if(!product.IsActive||!product.StockTracked||!product.TrackSerial||product.TrackExpiry)throw new DomainException("Device_ReturnInvalid");
            var unit=await db.SerializedUnits.SingleAsync(u=>u.ProductId==line.ProductId&&u.Serial==line.SerialNumber,token);
            unit.ReleaseReturned(sale.Id);product.Restock(1);
            var release=new DeviceReturnRelease{ConfirmationId=confirmationId,SaleReturnId=returnId,SaleLineId=saleLineId,SerializedUnitId=unit.Id,Inspection=inspection,Operator=auth.CurrentUser!.DisplayName,ReleasedAt=DateTime.Now};
            db.DeviceReturnReleases.Add(release);db.StockMovements.Add(new(product.Id,product.Name,1,StockMovementKind.SaleReturn,release.ReleasedAt,sale.ReceiptNo));
            await db.SaveChangesAsync(token);return release;
        },ct);
}
