using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuestPDF.Fluent;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Business;
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
    private static async Task CompletionBusinessChecks(IServiceProvider sp,string screens)
    {
        var setup=sp.GetRequiredService<IBusinessSetup>();var auth=sp.GetRequiredService<IAuthService>();
        var profile=new BusinessProfile{StoreName="Completion QA",BusinessType="electronics"};profile.ApplyBusinessDefaults();
        await setup.CompleteAsync(profile,"qa-owner","IsolatedComplete123!");await auth.LoginAsync("qa-owner","IsolatedComplete123!");
        var db=sp.GetRequiredService<PosDbContext>();var sales=sp.GetRequiredService<ISaleService>();var returns=sp.GetRequiredService<ReturnsService>();
        var p=new Product("CREDIT-QA","Credit QA",100,0,Unit.Piece,20,(await db.Categories.FirstAsync()).Id);db.Products.Add(p);await db.SaveChangesAsync();
        Cart One(){var c=new Cart();c.AddOrIncrement(p,1);return c;}
        var customer=new SaleCustomer("Credit visitor","03001234567",null,null);
        var original=await sales.CheckoutAsync(One(),new CreditPayment(100,10),auth.CurrentUser!,customer:customer);
        await returns.RecordAsync(original.Id,[new(original.Lines.Single().Id,1,false)],"QA return",false,Guid.NewGuid());
        var next=await sales.CheckoutAsync(One(),new CreditPayment(100,0),auth.CurrentUser!,customer:customer);
        var shell=sp.GetRequiredService<MainViewModel>();shell.IsLoggedIn=true;
        var window=new MainWindow(sp.GetRequiredService<PosStorage>()){DataContext=shell,Width=1366,Height=768};
        var template=new DataTemplate(typeof(CustomersViewModel)){VisualTree=new FrameworkElementFactory(typeof(CustomersView))};window.Resources.Add(template.DataTemplateKey,template);
        Application.Current.MainWindow=window;window.Show();await shell.ShowCustomersCommand.ExecuteAsync(null);Pump();
        var customers=(CustomersViewModel)shell.CurrentViewModel;
        Assert(customers.Selected!.CreditAmount==10,"Customers exposes returned credit");
        foreach(var lang in new[]{"en","ur"})foreach(var size in new[]{100,200})
        {
            LocalizationManager.Instance.SetLanguage(lang);sp.GetRequiredService<DesktopTextSize>().Save(size);Pump();
            var pending=customers.ManageCreditCommand.ExecuteAsync(null);Pump();
            var form=Descendants(window).OfType<BusinessForm>().Single(f=>f.Fields.ContainsKey("credit"));
            Assert(form.Fields.ContainsKey("target")&&form.Fields.ContainsKey("amount"),"Credit actions include target invoice and amount");
            Assert(Descendants(window).OfType<ScrollViewer>().Any(v=>ReferenceEquals(v.Content,form.Parent)),"Credit popup scrolls at enlarged display sizes");
            var close=Descendants(window).OfType<Button>().Single(b=>Equals(b.Content,LocalizationManager.Instance["Common_Close"]));
            var bounds=close.TransformToAncestor(window).TransformBounds(new Rect(0,0,close.ActualWidth,close.ActualHeight));Assert(bounds.Top>=0&&bounds.Bottom<window.ActualHeight,"Credit popup keeps Close visible without scrolling");
            Capture(window,System.IO.Path.Combine(screens,$"credit-{lang}-{size}.png"));
            Descendants(window).OfType<Button>().Single(b=>Equals(b.Content,LocalizationManager.Instance["Common_Close"])).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await pending;
        }
        LocalizationManager.Instance.SetLanguage("en");sp.GetRequiredService<DesktopTextSize>().Save(100);
        var use=customers.ManageCreditCommand.ExecuteAsync(null);Pump();var credit=Descendants(window).OfType<BusinessForm>().Single(f=>f.Fields.ContainsKey("credit"));
        ((TextBox)credit.Fields["amount"]).Text="6";
        Descendants(window).OfType<Button>().Single(b=>b.IsDefault).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await use;
        Assert(next.Outstanding==94&&customers.Selected!.CreditAmount==4,"Credit popup applies credit and refreshes remaining balance");
        var refund=customers.ManageCreditCommand.ExecuteAsync(null);Pump();credit=Descendants(window).OfType<BusinessForm>().Single(f=>f.Fields.ContainsKey("credit"));
        ((ComboBox)credit.Fields["action"]).SelectedValue="CASH";((TextBox)credit.Fields["amount"]).Text="4";
        Descendants(window).OfType<Button>().Single(b=>b.IsDefault).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await refund;
        Assert(customers.Selected!.CreditAmount==0&&!InAppDialog.IsOpen,"Completed credit refund closes and clears available credit");
        var report=await sp.GetRequiredService<IReportService>().GetXReportAsync(DateTime.Today);
        Assert(report.CreditCashRefunds==4,"Exported report includes actual credit refunds separately from original sales");
        QuestPDF.Settings.License=QuestPDF.Infrastructure.LicenseType.Community;
        foreach(var lang in new[]{"en","ur"})new SupermarketPos.Infrastructure.Documents.ReportDocument(report,"Completion QA",lang).GeneratePdf(System.IO.Path.Combine(screens,"credit-report-"+lang+".pdf"));
        await shell.ShowDigitalServicesCommand.ExecuteAsync(null);Pump();var digital=Descendants(window).OfType<DigitalServicesView>().Single();
        foreach(var status in new[]{"Pending","Failed"})
        {
            var form=Descendants(digital).OfType<BusinessForm>().Single();((TextBox)form.Fields["AmountText"]).Text="100";((TextBox)form.Fields["Recipient"]).Text="03001234568";
            ((ComboBox)form.Fields["Outcome"]).SelectedValue=status;Pump();
            Assert(!digital.OpenCardCommand.CanExecute(null),"Unsettled result never opens card settlement");
            await digital.OpenCashCommand.ExecuteAsync(null);Pump();
            Assert(!InAppDialog.IsOpen&&(await db.ServiceTransactions.AsNoTracking().OrderBy(t=>t.Id).LastAsync()).Status==status,"Digital form directly records "+status+" without payment or receipt");
        }
        Assert(!await db.ServicePostings.AnyAsync(),"Pending and failed UI actions create no financial postings");
        window.Close();Pump();
    }
}
