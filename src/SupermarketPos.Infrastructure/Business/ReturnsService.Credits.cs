using Microsoft.EntityFrameworkCore;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;

namespace SupermarketPos.Infrastructure.Business;

public sealed record AvailableCustomerCredit(SaleReturn Return, decimal Available, string Receipt);

public sealed partial class ReturnsService
{
    public async Task<IReadOnlyList<AvailableCustomerCredit>> AvailableCreditsAsync(int[] saleIds, CancellationToken ct=default)
    {
        var credits=await db.SaleReturns.AsNoTracking().Where(r=>saleIds.Contains(r.SaleId)&&r.Status=="Posted").ToArrayAsync(ct);
        var ids=credits.Select(r=>r.Id).ToArray();
        var uses=await db.CustomerCreditMovements.AsNoTracking().Where(m=>ids.Contains(m.SaleReturnId)).ToArrayAsync(ct);
        var spent=uses.GroupBy(m=>m.SaleReturnId).ToDictionary(g=>g.Key,g=>g.Sum(m=>m.Amount));
        var receipts=await db.Sales.AsNoTracking().Where(s=>saleIds.Contains(s.Id)).Select(s=>new{s.Id,s.ReceiptNo}).ToDictionaryAsync(s=>s.Id,s=>s.ReceiptNo,ct);
        return credits.Where(r=>r.CustomerCredit>0).Select(r=>new AvailableCustomerCredit(r,r.CustomerCredit-spent.GetValueOrDefault(r.Id),receipts[r.SaleId])).Where(c=>c.Available>0).ToArray();
    }

    public async Task<IReadOnlyList<CustomerCreditMovement>> CreditMovementsAsync(int[] saleIds,CancellationToken ct=default)=>
        await db.CustomerCreditMovements.AsNoTracking().Where(m=>db.SaleReturns.Any(r=>r.Id==m.SaleReturnId&&saleIds.Contains(r.SaleId))).OrderBy(m=>m.RecordedAt).ThenBy(m=>m.Id).ToArrayAsync(ct);

    public async Task<IReadOnlyList<CustomerCreditMovement>> CreditRefundsBetweenAsync(DateTime from,DateTime to,CancellationToken ct=default)=>
        await db.CustomerCreditMovements.AsNoTracking().Where(m=>m.TargetSaleId==null&&m.RecordedAt>=from&&m.RecordedAt<to).ToArrayAsync(ct);

    public Task<CustomerCreditMovement> UseCreditAsync(int returnId,int? targetSaleId,decimal amount,string method,string reference,
        Guid confirmationId,bool refundConfirmed=false,CancellationToken ct=default)=>work.ExecuteInTransactionAsync(async token=>
    {
        auth.RequireAdmin();reference=reference.Trim();
        if(confirmationId==Guid.Empty||amount<=0||amount!=Money.Round2(amount)||reference.Length>150||reference.Any(char.IsControl)||
            (targetSaleId.HasValue?method!="APPLY":method is not ("CASH" or "CARD")))throw new DomainException("Credit_Invalid");
        if(await db.CustomerCreditMovements.SingleOrDefaultAsync(m=>m.ConfirmationId==confirmationId,token) is{} saved)
        {
            if(saved.SaleReturnId!=returnId||saved.TargetSaleId!=targetSaleId||saved.Amount!=amount||saved.Method!=method||saved.Reference!=reference)throw new DomainException("Credit_Conflict");
            return saved;
        }
        var credit=await db.SaleReturns.AsNoTracking().SingleOrDefaultAsync(r=>r.Id==returnId&&r.Status=="Posted",token)??throw new DomainException("Credit_Invalid");
        var used=await db.CustomerCreditMovements.AsNoTracking().Where(m=>m.SaleReturnId==returnId).Select(m=>m.Amount).ToArrayAsync(token);
        if(amount>credit.CustomerCredit-used.Sum())throw new DomainException("Credit_Insufficient");
        if(targetSaleId is{} saleId)
        {
            var source=await LoadSaleAsync(credit.SaleId,token);var target=await LoadSaleAsync(saleId,token);
            await db.Entry(target).ReloadAsync(token);
            if(source.CustomerAccountKey is null||source.CustomerAccountKey!=target.CustomerAccountKey||target.PaymentMethod!="CREDIT"||source.Id==target.Id)throw new DomainException("Credit_CustomerMismatch");
            target.ApplyReturnCredit(amount);
        }
        else if(!refundConfirmed)throw new DomainException("Credit_ConfirmRefund");
        var movement=new CustomerCreditMovement{ConfirmationId=confirmationId,SaleReturnId=returnId,TargetSaleId=targetSaleId,Amount=amount,Method=method,Reference=reference,Operator=auth.CurrentUser!.DisplayName,RecordedAt=DateTime.Now};
        db.CustomerCreditMovements.Add(movement);await db.SaveChangesAsync(token);return movement;
    },ct);
}
