using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Infrastructure.Persistence;
using SupermarketPos.Wpf;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Wpf.ViewModels;
using SupermarketPos.Wpf.Views;

internal static partial class Program
{
    private static async Task FeatureAuditChecks(IServiceProvider sp, string folder)
    {
        var db = sp.GetRequiredService<PosDbContext>();
        var auth = sp.GetRequiredService<IAuthService>();
        var owner = auth.CurrentUser!;
        var product = await db.Products.OrderBy(p => p.Id).FirstAsync();
        product.SetPrice(100);
        product.SetVatRate(0);
        product.SetPurchaseCost(40);
        await db.SaveChangesAsync();

        var customer = SaleCustomer.Create("Isolated audit customer","03000000001",null,null)!;
        var cart = new Cart();
        cart.AddOrIncrement(product, 3);
        var sale = await sp.GetRequiredService<ISaleService>().CheckoutAsync(cart, new CreditPayment(300,50), owner,customer:customer);
        var ledger = sp.GetRequiredService<ITradeLedger>();
        var confirmation = Guid.NewGuid();
        await ledger.RecordPaymentAsync(sale.Id,false,60,"CASH",DateTime.Today,null,owner,confirmation);
        await ledger.RecordPaymentAsync(sale.Id,false,60,"CASH",DateTime.Today,null,owner,confirmation);
        Assert(sale.Outstanding==190 && (await ledger.PaymentsAsync(sale.Id,false)).Count==1,"Partial repayment posts once and reduces actual outstanding");

        var timings = new Dictionary<string,double>();
        var clock = Stopwatch.StartNew();
        var returns = sp.GetRequiredService<ReturnsService>();
        var refund = await returns.RecordAsync(sale.Id,[new(sale.Lines.Single().Id,1,false)],"Audit return",false,Guid.NewGuid());
        timings["returnConfirmationMs"] = clock.Elapsed.TotalMilliseconds;
        Assert(refund.ReceivableReduction==100 && sale.Outstanding==90 && sale.GrossTotal==300 && sale.AmountPaid==50,
            "Return reduces the actual receivable while preserving the original invoice and payment");

        product.SetPrice(125);
        product.SetPurchaseCost(65);
        await db.SaveChangesAsync();
        Assert(sale.Lines.Single().UnitPrice==100 && sale.Lines.Single().UnitCost==40 && sale.GrossProfit==180,
            "Invoice price, cost and gross margin remain on the original basis after a product edit");

        var shell = sp.GetRequiredService<MainViewModel>();
        shell.IsLoggedIn = true;
        var window = new MainWindow(sp.GetRequiredService<PosStorage>()){DataContext=shell,Width=1366,Height=768};
        foreach (var (model, view) in new[] { (typeof(PosViewModel),typeof(PosView)), (typeof(DashboardViewModel),typeof(DashboardView)),
            (typeof(ReportsViewModel),typeof(ReportsView)) })
        {
            var template = new DataTemplate(model){VisualTree=new FrameworkElementFactory(view)};
            window.Resources.Add(template.DataTemplateKey,template);
        }
        System.Windows.Application.Current.MainWindow=window;
        clock.Restart();
        window.Show();
        Pump();
        timings["shellFirstRenderMs"]=clock.Elapsed.TotalMilliseconds;
        await Time("registerDataLoadMs",()=>shell.ShowPosCommand.ExecuteAsync(null));
        var register = (PosViewModel)shell.CurrentViewModel;
        var salesCount = await db.Sales.CountAsync();
        var moves = await db.StockMovements.CountAsync();
        await Time("repeatSaleMs",()=>shell.RepeatSaleCommand.ExecuteAsync(sale));
        Assert(register.Lines.Count==1 && register.Lines[0].Line.GrossUnitPrice==125 && register.Lines[0].Line.Quantity==3,
            "Repeat loads current prices and original quantities into an uncommitted cart. " + register.Status);
        var confirmRepeat = shell.RepeatSaleCommand.ExecuteAsync(sale);
        Pump();
        var cancelRepeat = Descendants(window).OfType<Button>().Single(b=>b.IsVisible&&b.IsEnabled&&Equals(b.Content,LocalizationManager.Instance["Common_Cancel"]));
        cancelRepeat.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await confirmRepeat;
        Assert(register.Lines.Single().Line.Quantity==3 && await db.Sales.CountAsync()==salesCount && await db.StockMovements.CountAsync()==moves,
            "Repeat preserves an existing cart and posts neither sale nor inventory movement");
        register.Lines.Single().PriceText = "120";
        await ChooseRepeat("Trade_RepeatAdd");
        Assert(register.Lines.Count==2 && register.Lines.Select(line=>line.Line.GrossUnitPrice).SequenceEqual(new decimal[]{120,125}),
            "Adding a repeat keeps an overridden existing line separate from current-price units");
        await ChooseRepeat("Trade_RepeatReplace");
        Assert(register.Lines.Single().Line.GrossUnitPrice==125 && register.Lines.Single().Line.Quantity==3 && await db.StockMovements.CountAsync()==moves,
            "Replacing a repeat stages a valid cart and records no stock movements");
        register.RemoveLineCommand.Execute(register.Lines.Single());
        await register.AddFromResultsCommand.ExecuteAsync(product);
        register.SaveDraftCommand.Execute(null);
        Assert(register.Status==LocalizationManager.Instance["Trade_DraftSaved"],"The owner can save an eligible draft");
        register.RemoveLineCommand.Execute(register.Lines.Single());
        await register.ResumeDraftCommand.ExecuteAsync(null);
        Assert(register.Lines.Count==1 && await db.Sales.CountAsync()==salesCount && await db.StockMovements.CountAsync()==moves,
            "Draft reopens without posting revenue or moving stock");
        register.RemoveLineCommand.Execute(register.Lines.Single());
        register.Customer.Load(customer);
        await TimeDialog("frequentlyPurchasedOpenMs",()=>register.PreviousPurchasesCommand.ExecuteAsync(true));
        await TimeDialog("previousInvoicesOpenMs",()=>register.PreviousPurchasesCommand.ExecuteAsync(false));
        await Time("inventoryDataLoadMs",()=>shell.ShowProductsCommand.ExecuteAsync(null));
        var inventory = (ProductsViewModel)shell.CurrentViewModel;
        await inventory.OpenProductAsync(product.Id);
        await TimeDialog("itemHistoryOpenMs",()=>inventory.ProductSalesCommand.ExecuteAsync(product.Id));
        inventory.CancelEditCommand.Execute(null);
        await Time("invoiceHistoryLoadMs",()=>shell.ShowReportsCommand.ExecuteAsync(null));
        var reports = (ReportsViewModel)shell.CurrentViewModel;
        clock.Restart();
        reports.InvoiceSearch=sale.ReceiptNo;
        await reports.LoadHistoryCommand.ExecuteAsync(null);
        timings["invoiceSearchFilterMs"]=clock.Elapsed.TotalMilliseconds;
        Assert(reports.History.Single().Sale.Id==sale.Id,"Posted invoice is found by receipt number");
        reports.SelectedSale=reports.History.Single();
        Pump();
        var reportsView=Descendants(window).OfType<ReportsView>().Single();
        var heading=Descendants(reportsView).OfType<TextBlock>().Single(text=>text.Text==LocalizationManager.Instance["Dash_InvoiceItems"]);
        var margin=Descendants(reportsView).OfType<TextBlock>().Single(text=>text.IsVisible&&Equals(text.ToolTip,LocalizationManager.Instance["Dash_Profit"]));
        Assert(heading.ActualWidth>0&&margin.ActualWidth>0&&!Bounds(heading).IntersectsWith(Bounds(margin)),
            "Invoice item heading and owner margin have separate layout space");
        await TimeDialog("returnDialogOpenMs",()=>reports.ReturnItemsCommand.ExecuteAsync(null),close=>
        {
            var submit=Descendants(window).OfType<Button>().Single(button=>button.IsVisible&&
                Equals(button.Content,LocalizationManager.Instance["Trade_ReturnItems"])&&button.Command is null);
            var closeBounds=Bounds(close);
            var submitBounds=Bounds(submit);
            Assert(Math.Abs(closeBounds.Top-submitBounds.Top)<1&&closeBounds.Right<=submitBounds.Left,
                "Return dialog places Close and Return Items on the same row without overlap");
            CheckWindow(window,"return-dialog");
        });
        reports.InvoiceSearch=customer.Phone!;
        reports.InvoicePayment="CREDIT";
        reports.InvoiceStatus="returned";
        await reports.LoadHistoryCommand.ExecuteAsync(null);
        Assert(reports.History.Single().Sale.Id==sale.Id,"Customer phone, credit and posted-return filters can be combined");
        await shell.ShowCustomersCommand.ExecuteAsync(null);
        var customers = (CustomersViewModel)shell.CurrentViewModel;
        customers.Selected=customers.Customers.Single(c=>c.Key==sale.CustomerAccountKey);
        await TimeDialog("customerLedgerOpenMs",()=>customers.ViewLedgerCommand.ExecuteAsync(null));

        var hasher=sp.GetRequiredService<IPasswordHasher>();
        db.Users.Add(new User("audit-cashier",hasher.Hash("IsolatedCashier123!"),"Audit Cashier",UserRole.Cashier));
        await db.SaveChangesAsync();
        await auth.LoginAsync("audit-cashier","IsolatedCashier123!");
        await shell.ShowDashboardCommand.ExecuteAsync(null);
        var dashboard=(DashboardViewModel)shell.CurrentViewModel;
        Pump();
        Assert(!reports.ShowMargin,"Invoice margin visibility checks the cashier role in Sales & Reports");
        Assert(!auth.CurrentUser!.IsAdmin && !dashboard.ShowProfit && dashboard.ProfitText=="—" &&
            dashboard.History.SelectMany(row=>row.Items).All(item=>item.UnitCost=="—"),
            "Cashier Dashboard cannot expose invoice profit or purchase cost");
        try
        {
            await sp.GetRequiredService<SupermarketPos.Application.Services.InvoiceMarginService>().ForSaleAsync(sale.Id);
            throw new InvalidOperationException("Cashier called owner-only margin service");
        }
        catch (AuthException) { Assert(true,"Direct margin access requires an owner"); }
        try
        {
            await returns.RecordAsync(sale.Id,[new(sale.Lines.Single().Id,1,false)],"Unauthorized test",false,Guid.NewGuid());
            throw new InvalidOperationException("A cashier recorded an unauthorized return");
        }
        catch(DomainException){Assert(await db.SaleReturns.CountAsync()==1,"Cashier return is blocked before a ledger record is created");}

        File.WriteAllText(Path.Combine(folder,"feature-audit-timings.json"),JsonSerializer.Serialize(timings,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("AUDIT_TIMINGS "+JsonSerializer.Serialize(timings));
        window.Close();
        Pump();

        async Task Time(string label, Func<Task> action)
        {
            clock.Restart();
            await action();
            timings[label]=clock.Elapsed.TotalMilliseconds;
        }
        async Task ChooseRepeat(string label)
        {
            var task=shell.RepeatSaleCommand.ExecuteAsync(sale);
            Pump();
            Descendants(window).OfType<Button>().Single(b=>b.IsVisible&&b.IsEnabled&&Equals(b.Content,LocalizationManager.Instance[label]))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await task;
        }
        Rect Bounds(FrameworkElement element) =>
            element.TransformToAncestor(window).TransformBounds(new Rect(0,0,element.ActualWidth,element.ActualHeight));

        async Task TimeDialog(string label, Func<Task> action, Action<Button>? verifyLayout=null)
        {
            clock.Restart();
            var task=action();
            while(!InAppDialog.IsOpen&&!task.IsCompleted&&clock.Elapsed<TimeSpan.FromSeconds(8)) { Pump(); await Task.Delay(10); }
            Pump();
            timings[label]=clock.Elapsed.TotalMilliseconds;
            Assert(InAppDialog.IsOpen,label+" opened a dialog");
            var close=Descendants(window).OfType<Button>().Single(b=>b.IsVisible&&Equals(b.Content,LocalizationManager.Instance["Common_Close"]));
            verifyLayout?.Invoke(close);
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await task;
        }
    }
}
