using Microsoft.EntityFrameworkCore;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Business;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Persistence;
using System.Text.Json;

namespace SupermarketPos.Infrastructure.Business;

public sealed record PharmacyQuickProduct(int? ReferenceId,string Name,string Barcode,string Unit,decimal Price,decimal VatRate,bool RulesReviewed,bool Prescription=false,bool DispensingReference=false);

public sealed class PharmacyInventory(PosDbContext db,IUnitOfWork uow,IAuthService auth)
{
    public async Task<Product> QuickAddAsync(PharmacyQuickProduct input,CancellationToken ct=default)
    {
        auth.RequireAdmin();
        return await uow.ExecuteInTransactionAsync(async token=>
        {
            var configuration=await db.BusinessConfigurations.AsNoTracking().SingleOrDefaultAsync(token);
            if(configuration is null||JsonSerializer.Deserialize<BusinessProfile>(configuration.ProfileJson)?.IsPharmacy!=true)throw new DomainException("Pharmacy_Only");
            var name=input.Name.Trim();var barcode=input.Barcode.Trim();
            if(name.Length is <1 or >200||input.Price<0||input.Price!=Money.Round2(input.Price))throw new DomainException("Pharmacy_InvalidProduct");
            if(input.ReferenceId is{} reference)
            {
                var medicine=await db.MedicineReferences.SingleOrDefaultAsync(m=>m.Id==reference,token)??throw new DomainException("Pharmacy_InvalidProduct");
                name=medicine.Name;
                if(await db.Products.SingleOrDefaultAsync(p=>p.MedicineReferenceId==reference,token) is{} saved)return saved;
            }
            else if(await db.Products.FirstOrDefaultAsync(p=>p.Name.ToLower()==name.ToLower(),token) is{} named)return named;
            if(barcode.Length>0&&await db.Products.AnyAsync(p=>p.Barcode==barcode||p.PackageBarcode==barcode,token))throw new DomainException("Scanner_DuplicateBarcode");
            var category=await db.Categories.FirstOrDefaultAsync(c=>c.Name=="Medicines",token);
            if(category is null){category=new Category("Medicines");db.Categories.Add(category);await db.SaveChangesAsync(token);}
            var product=new Product(barcode,name,input.Price,input.VatRate,Unit.Piece,0,category.Id);
            product.ConfigureMedicineUnits(input.Unit);product.MarkStockUntracked();
            if(input.ReferenceId is{} id)product.LinkMedicine(id);
            if(input.RulesReviewed)product.ReviewDispensingRules(input.Prescription,input.DispensingReference);
            db.Products.Add(product);await db.SaveChangesAsync(token);return product;
        },ct);
    }
    public Task VerifyStockAsync(int productId,decimal physicalQuantity,string note,CancellationToken ct=default)
    {
        auth.RequireAdmin();
        return uow.ExecuteInTransactionAsync(async token=>
        {
            if(string.IsNullOrWhiteSpace(note)||note.Length>1000)throw new DomainException("Pharmacy_ResolutionRequired");
            var p=await db.Products.SingleAsync(p=>p.Id==productId,token);var delta=physicalQuantity-p.StockQuantity;p.VerifyOpeningStock(physicalQuantity);
            db.StockMovements.Add(new StockMovement(p.Id,p.Name,delta,StockMovementKind.ManualAdjustment,DateTime.Now,note));
            foreach(var task in await db.StockReconciliations.Where(r=>r.ProductId==productId&&r.ResolvedAt==null).ToArrayAsync(token))task.Resolve(note,auth.CurrentUser!.DisplayName);
            await db.SaveChangesAsync(token);return true;
        },ct);
    }
    public Task RememberAsync(int productId,string unit,decimal price,bool reviewed,bool prescription,bool reference,CancellationToken ct=default)
    {
        auth.RequireAdmin();return uow.ExecuteInTransactionAsync(async token=>
        {
            var p=await db.Products.SingleAsync(p=>p.Id==productId,token);p.SetSellingUnitPrice(unit,price);
            if(!p.DispensingRulesReviewed&&reviewed)p.ReviewDispensingRules(prescription,reference);
            await db.SaveChangesAsync(token);return true;
        },ct);
    }
    public Task SetBatchHoldAsync(int batchId,string reason,bool hold,CancellationToken ct=default)
    {
        auth.RequireAdmin();return uow.ExecuteInTransactionAsync(async token=>
        {
            reason=reason.Trim();if(reason.Length is <1 or >300||reason.Any(char.IsControl))throw new DomainException("Pharmacy_ResolutionRequired");
            var batch=await db.InventoryBatches.SingleAsync(b=>b.Id==batchId,token);var product=await db.Products.SingleAsync(p=>p.Id==batch.ProductId,token);batch.SetHold(hold?reason:"");
            db.StockMovements.Add(new StockMovement(product.Id,product.Name,0,StockMovementKind.BatchHold,DateTime.Now,(hold?"Hold: ":"Release: ")+reason,batch));
            db.BusinessAudits.Add(new(){Actor=auth.CurrentUser!.DisplayName,Action=hold?"BatchHold":"BatchRelease",Summary=$"ProductId={product.Id}; BatchId={batch.Id}"});await db.SaveChangesAsync(token);return true;
        },ct);
    }
}
