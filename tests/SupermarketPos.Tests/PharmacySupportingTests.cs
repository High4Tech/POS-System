using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SupermarketPos.Application.Services;
using SupermarketPos.Domain.Business;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Infrastructure.Persistence;
namespace SupermarketPos.Tests;
public sealed class PharmacySupportingTests
{
    private static async Task<(PharmacyInventory Inventory,User User,SaleService Sales)> Setup(TestDb db)
    {
        var hash=new BCryptPasswordHasher();var user=new User("owner",hash.Hash("test-owner-123"),"Owner",UserRole.Admin);db.Context.Users.Add(user);db.Context.BusinessConfigurations.Add(new(){Completed=true,ProfileJson=JsonSerializer.Serialize(new BusinessProfile{BusinessType="pharmacy",StoreName="Original pharmacy",Phone="03001234567",Phone2="03121234567",ReceiptNotes=[new(){Text="Original note",Enabled=true}]})});await db.Context.SaveChangesAsync();var auth=new AuthService(new UserRepository(db.Context),hash);await auth.LoginAsync("owner","test-owner-123");var work=new UnitOfWork(db.Context);
        return(new(db.Context,work,auth),user,new(new ProductRepository(db.Context),new SaleRepository(db.Context),new ReceiptNumberGenerator(db.Context),work,compliance:new FbrCompliance(db.Context)));
    }
    [Fact]public async Task QuickCreationCreditSaleAndOwnerReconciliationPreserveUnknownHistory()
    {
        using var db=new TestDb();var (inventory,user,sales)=await Setup(db);var p=await inventory.QuickAddAsync(new(null,"Locally verified medicine","","tablet",10,0,true));var again=await inventory.QuickAddAsync(new(null,"Locally verified medicine","","tablet",15,0,true));Assert.Equal(p.Id,again.Id);Assert.Single(db.Context.Products);Assert.False(p.StockTracked);Assert.Empty(db.Context.StockMovements);Assert.Throws<DomainException>(()=>p.SetStock(99));
        var due=DateTime.Today.AddDays(7);var cart=new Cart();cart.AddOrIncrement(p,2,medicineUnit:"tablet");var sale=await sales.CheckoutAsync(cart,new CreditPayment(20,5,due,"Contact before collection"),user,customer:SaleCustomer.Create("Customer","03009999999",null,null));Assert.Equal(15,sale.Outstanding);Assert.Equal(5,sale.AmountPaid);Assert.Null(sale.CashTendered);Assert.Null(sale.ChangeGiven);Assert.Equal(due,sale.CreditDueDate);Assert.Equal("Contact before collection",sale.CreditNote);Assert.Single(db.Context.StockReconciliations);
        var snapshot=JsonSerializer.Deserialize<BusinessProfile>(sale.InvoiceProfileJson!)!;Assert.Equal("Original pharmacy",snapshot.StoreName);Assert.Equal("03121234567",snapshot.Phone2);Assert.Equal("Original note",snapshot.ReceiptNotes.Single().Text);
        db.Context.BusinessConfigurations.Single().ProfileJson=JsonSerializer.Serialize(snapshot with{StoreName="Changed pharmacy",Phone2="03229999999"});await db.Context.SaveChangesAsync();await inventory.VerifyStockAsync(p.Id,15,"Counted 15 actual tablets; earlier sale reconciled");Assert.True(p.StockTracked);Assert.Equal(15,p.StockQuantity);Assert.NotNull(db.Context.StockReconciliations.Single().ResolvedAt);Assert.True(sale.Lines.Single().StockUntracked);Assert.Null(sale.Lines.Single().UnitCost);Assert.Null(sale.GrossProfit);Assert.Equal("Original pharmacy",JsonSerializer.Deserialize<BusinessProfile>(sale.InvoiceProfileJson!)!.StoreName);
    }
    [Fact]public async Task HoldingAndReleasingBatchChangesSellableQuantityAndCreatesZeroQuantityAuditOnly()
    {
        using var db=new TestDb();var (inventory,_,_)=await Setup(db);var category=new Category("Medicine");db.Context.Categories.Add(category);await db.Context.SaveChangesAsync();var p=new Product("","Batch medicine",10,0,Unit.Piece,0,category.Id);p.SetExpiryTracking(true);var b=p.RecordBatch("LOT",DateOnly.FromDateTime(DateTime.Today.AddMonths(3)),30,null,DateTime.Now);db.Context.Products.Add(p);await db.Context.SaveChangesAsync();await inventory.SetBatchHoldAsync(b.Id,"Supplier recall under investigation",true);Assert.Equal(30,p.StockQuantity);Assert.Equal(0,p.AvailableForSale(DateOnly.FromDateTime(DateTime.Today)));Assert.Throws<DomainException>(()=>p.SellBatch(b.Id,b.ExpiryDate,1,DateTime.Now));await inventory.SetBatchHoldAsync(b.Id,"Authorized owner reviewed release",false);Assert.Equal(30,p.AvailableForSale(DateOnly.FromDateTime(DateTime.Today)));Assert.Equal(2,db.Context.BusinessAudits.Count());Assert.All(db.Context.StockMovements,m=>{Assert.Equal(0,m.QuantityChange);Assert.Equal(StockMovementKind.BatchHold,m.Kind);});
    }
    [Fact]public async Task SerializedMedicinePackKeepsOneSerialAndThirtyBaseUnits()
    {
        using var db=new TestDb();var (_,user,sales)=await Setup(db);var category=new Category("Medicine");db.Context.Categories.Add(category);await db.Context.SaveChangesAsync();var p=new Product("","Pack medicine",10,0,Unit.Piece,0,category.Id);p.ConfigurePackaging("pack",30,null,180,250,null);p.ConfigureMedicineUnits("tablet",10,90);p.ReviewDispensingRules(false,false);p.SetExpiryTracking(true);var batch=p.RecordBatch("LOT",DateOnly.FromDateTime(DateTime.Today.AddMonths(3)),30,null,DateTime.Now);db.Context.Products.Add(p);await db.Context.SaveChangesAsync();
        var cart=new Cart();cart.AddOrIncrement(p,30,batch,serial:"MED-SERIAL",medicineUnit:"pack");Assert.Throws<DomainException>(()=>new CartLine(p,60,batch,serial:"MED-SERIAL",medicineUnit:"pack"));var sale=await sales.CheckoutAsync(cart,new CashPayment(250,250),user);Assert.Equal(0,p.StockQuantity);Assert.Equal(1,sale.Lines.Single().Quantity);Assert.Equal(30,sale.Lines.Single().BaseQuantity);Assert.Equal("MED-SERIAL",sale.Lines.Single().SerialNumber);Assert.Equal(180,sale.Lines.Single().UnitCost);Assert.Null(sale.Lines.Single().Imei1);
    }
}
