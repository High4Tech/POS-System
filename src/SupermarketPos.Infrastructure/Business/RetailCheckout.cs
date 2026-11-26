using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
namespace SupermarketPos.Infrastructure.Business;

public sealed record RetailServicePayment(ServiceTransaction Transaction,bool ConfirmedExternally);
public sealed record RetailCheckoutResult(Sale? Sale,IReadOnlyList<ServiceTransaction> Services);
public sealed class RetailCheckout(IUnitOfWork work,ISaleService sales,DigitalServiceLedger digital,RetailInventory inventory)
{
    public Task<RetailCheckoutResult> CheckoutAsync(Cart cart,Payment? payment,decimal tendered,User cashier,SaleCustomer? customer,Guid checkoutId,IReadOnlyList<RetailServicePayment> services)=>work.ExecuteInTransactionAsync(async ct=>
    {
        var goods=cart.GoodsOnly();var goodsTotal=PricingService.GrossTotal(goods);
        var financial=cart.Lines.Where(l=>l.Product.IsFinancialService).ToArray();
        if(financial.Length!=services.Count||financial.Zip(services).Any(pair=>pair.First.Quantity!=1||pair.First.LineDiscount is not null||pair.First.GrossUnitPrice!=pair.Second.Transaction.Principal||pair.First.Product.ServiceType!=pair.Second.Transaction.Type||pair.First.Product.ServiceProviderId!=pair.Second.Transaction.ProviderId||pair.First.Product.ServicePackageId!=pair.Second.Transaction.PackageId))throw new DomainException("Retail_ServiceCheckoutRequired");
        if(services.Count>0&&payment is not null&&payment.Method!="CASH")throw new DomainException("Retail_ServiceCashOnly");
        var net=goodsTotal+services.Where(s=>s.Transaction.Status=="Confirmed").Sum(s=>s.Transaction.CashChange);
        if(services.Count>0&&tendered<Math.Max(0,net))throw new DomainException("Insufficient cash tendered.");
        foreach(var line in cart.Lines.Where(l=>l.Product.QuickEntry))await inventory.RememberAsync(line.Product,line.GrossUnitPrice,ct);
        Sale? sale=null;
        if(!goods.IsEmpty)
        {
            var allocated=services.Count==0?payment: new CashPayment(goodsTotal,goodsTotal);
            sale=await sales.CheckoutAsync(goods,allocated??throw new DomainException("A payment is required."),cashier,ct,customer,checkoutId);
        }
        var saved=new List<ServiceTransaction>();
        foreach(var service in services)saved.Add(await digital.SaveAsync(service.Transaction,service.ConfirmedExternally));
        if(goods.IsEmpty&&saved.Count==0)throw new DomainException("Cannot check out an empty cart.");
        return new RetailCheckoutResult(sale,saved);
    });
}
