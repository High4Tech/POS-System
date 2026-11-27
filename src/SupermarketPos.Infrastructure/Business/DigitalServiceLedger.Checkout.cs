using Microsoft.EntityFrameworkCore;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;

namespace SupermarketPos.Infrastructure.Business;

public sealed partial class DigitalServiceLedger
{
    public Task<ServiceCheckout?> CheckoutByIdAsync(int id)=>db.ServiceCheckouts.AsNoTracking().Include(c=>c.Lines.OrderBy(t=>t.Id)).SingleOrDefaultAsync(c=>c.Id==id);

    public Task<ServiceCheckout> CheckoutAsync(Guid confirmationId,IReadOnlyList<ServiceTransaction> lines,string paymentMethod,decimal tendered)=>work.ExecuteInTransactionAsync(async ct=>
    {
        var user=auth.CurrentUser??throw new DomainException("Service_SignIn");
        if(confirmationId==Guid.Empty||lines.Count is 0 or >500||lines.Select(t=>t.ConfirmationId).Distinct().Count()!=lines.Count||lines.Any(t=>t.ReversesId.HasValue||t.Status!="Confirmed"))throw new DomainException("Service_Invalid");
        var total=lines.Sum(t=>t.SettlementAmount);
        if(paymentMethod is not ("CASH" or "CARD")||paymentMethod=="CARD"&&lines.Any(t=>t.Type=="CASHOUT"))throw new DomainException("Digital_InvalidPayment");
        if(tendered<0||tendered!=Money.Round2(tendered)||paymentMethod=="CASH"&&total>0&&tendered<total||paymentMethod=="CASH"&&total<=0&&tendered!=0||paymentMethod=="CARD"&&tendered!=total)throw new DomainException("Digital_InvalidTendered");
        var checkout=await db.ServiceCheckouts.AsNoTracking().Include(c=>c.Lines).SingleOrDefaultAsync(c=>c.ConfirmationId==confirmationId,ct);
        if(checkout is not null)
        {
            if(checkout.Total!=total||checkout.PaymentMethod!=paymentMethod||checkout.Tendered!=tendered||!checkout.Lines.OrderBy(t=>t.Id).Select(t=>t.ConfirmationId).SequenceEqual(lines.Select(t=>t.ConfirmationId)))throw new DomainException("Service_ConfirmationConflict");
        }
        else
        {
            var business=(await setup.LoadAsync(ct)).Profile;
            checkout=new(){ConfirmationId=confirmationId,CreatedAt=DateTime.Now,Total=total,PaymentMethod=paymentMethod,Tendered=tendered,StoreName=business.StoreName,Operator=user.DisplayName,InvoiceProfileJson=System.Text.Json.JsonSerializer.Serialize(business)};
            db.ServiceCheckouts.Add(checkout);await db.SaveChangesAsync(ct);
        }
        foreach(var line in lines)
        {
            line.CheckoutId=checkout.Id;line.PaymentMethod=paymentMethod;line.FloatChange=0;
            var saved=await SaveCoreAsync(line,false,true);
            if(!checkout.Lines.Any(t=>t.Id==saved.Id))checkout.Lines.Add(saved);
        }
        await db.SaveChangesAsync(ct);return checkout;
    });
}
