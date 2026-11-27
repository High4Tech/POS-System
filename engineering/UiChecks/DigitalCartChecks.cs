using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Infrastructure.Persistence;
using SupermarketPos.Wpf;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Wpf.ViewModels;
using SupermarketPos.Wpf.Views;

internal static partial class Program
{
    private static async Task DigitalCartChecks(IServiceProvider sp,MainViewModel shell,MainWindow window,string screens)
    {
        await shell.ShowDigitalServicesCommand.ExecuteAsync(null);Pump();
        var digital=Descendants(window).OfType<DigitalServicesView>().Single();
        for(var wait=0;!Descendants(digital).OfType<DataGrid>().Any(g=>g.Name=="DigitalCart")&&wait<200;wait++){await Task.Delay(25);Pump();}
        var expected=new[]{"Service_LOAD","Service_PACKAGE","Service_TRANSFER"}.Select(k=>LocalizationManager.Instance[k]).ToArray();
        var tabs=Descendants(digital).OfType<WrapPanel>().First().Children.OfType<Button>().Select(b=>b.Content?.ToString()).ToArray();Assert(tabs.SequenceEqual(expected),"Digital service hierarchy is Load, Package, Bank/Wallet Transfer");
        Assert(!Descendants(digital).OfType<CheckBox>().Any(),"Cashier flow has no provider-verification checkbox");
        var entry=Descendants(digital).OfType<BusinessForm>().Single();((TextBox)entry.Fields["AmountText"]).Text="100";((TextBox)entry.Fields["Recipient"]).Text="03001234567";((TextBox)entry.Fields["FeeText"]).Text="5";
        Descendants(entry).OfType<Button>().Single(b=>b.Name=="AddDigitalLine").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        Assert(digital.Cart.Count==1&&digital.HasDraft,"Mobile load adds to the digital cart without posting a transaction");digital.Cart[0].CostText="80";
        Button Mode(string key)=>Descendants(digital).OfType<Button>().First(b=>Equals(b.Content,LocalizationManager.Instance[key]));Mode("Service_PACKAGE").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        for(var wait=0;!Descendants(digital).OfType<ListBox>().Any(l=>l.Name=="PackageChoices"&&l.Items.Count>0)&&wait<100;wait++){await Task.Delay(25);Pump();}
        var radios=Descendants(digital).OfType<RadioButton>().ToArray();Assert(radios.Length==2&&radios.Count(r=>r.IsChecked==true)==1,"Prepaid/Postpaid uses a mutually exclusive pair");
        radios[0].ApplyTemplate();Assert(radios[0].Template.FindName("Box",radios[0]) is Border,"Radio choice uses the shared checkbox-style box");
        var list=Descendants(digital).OfType<ListBox>().Single(l=>l.Name=="PackageChoices");var card=Descendants(list).OfType<Button>().First();card.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        Assert(digital.Cart.Count==2&&!InAppDialog.IsOpen,"A single package click adds a cart row instead of opening a popup");var package=digital.Cart.Last();package.AmountText="250";
        await digital.OpenCashCommand.ExecuteAsync(null);Pump();Assert(!InAppDialog.IsOpen&&package.ShowValidation&&package.Errors().ContainsKey("Recipient"),"Required phone errors stay on the cart row before payment");package.Recipient="03001234567";
        var savedKeys=digital.Cart.Select(r=>r.ConfirmationId).ToArray();Assert(InvokeDigitalShortcut(window,shell,Key.S,ModifierKeys.Control),"Ctrl+S saves the encrypted digital cart draft");digital.ClearCartCommand.Execute(null);Assert(InvokeDigitalShortcut(window,shell,Key.O,ModifierKeys.Control|ModifierKeys.Shift),"Ctrl+Shift+O resumes a digital cart draft");for(var wait=0;digital.Cart.Count==0&&wait<100;wait++){await Task.Delay(25);Pump();}Assert(digital.Cart.Select(r=>r.ConfirmationId).SequenceEqual(savedKeys)&&digital.Cart.Last().AmountText=="250","Digital draft preserves row confirmation keys and edited prices");Mode("Service_PACKAGE").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        foreach(var lang in new[]{"en","ur"})foreach(var size in new[]{100,200}){LocalizationManager.Instance.SetLanguage(lang);sp.GetRequiredService<DesktopTextSize>().Save(size);await digital.OpenSalesAsync();Pump();for(var wait=0;!Descendants(digital).OfType<ListBox>().Any(l=>l.Name=="PackageChoices"&&l.Items.Count>0)&&wait<100;wait++){await Task.Delay(25);Pump();}var choices=Descendants(digital).OfType<ListBox>().Single(l=>l.Name=="PackageChoices");Capture(window,System.IO.Path.Combine(screens,$"digital-cart-{lang}-{size}.png"));Assert(choices.ActualHeight>=100,$"Package viewport remains usable: {lang}/{size}, height {choices.ActualHeight:0}");var grid=Descendants(digital).OfType<DataGrid>().Single(g=>g.Name=="DigitalCart");Assert(grid.Columns.Take(8).All(c=>c.ActualWidth>=c.MinWidth-1),"Editable cart columns preserve readable minimum widths");CheckWindow(window,$"digital-cart-{lang}-{size}");Capture(window,System.IO.Path.Combine(screens,$"digital-cart-{lang}-{size}.png"));}
        LocalizationManager.Instance.SetLanguage("en");sp.GetRequiredService<DesktopTextSize>().Save(100);Pump();
        Assert(InvokeDigitalShortcut(window,shell,Key.F,ModifierKeys.Control),"Ctrl+F is routed on Digital Services");Pump();Assert(Descendants(digital).OfType<TextBox>().Single(t=>t.Name=="PackageSearch").IsKeyboardFocused,"Ctrl+F focuses package search");
        var db=sp.GetRequiredService<PosDbContext>();var products=await db.Products.CountAsync();var before=await db.ServiceTransactions.CountAsync();
        Assert(InvokeDigitalShortcut(window,shell,Key.F1,ModifierKeys.None),"F1 opens digital cash checkout");Pump();Assert(InAppDialog.IsOpen,"Digital checkout uses the shared in-app popup");
        var overlay=((Grid)window.Content).Children.OfType<Grid>().Last();Assert(!Descendants(overlay).OfType<CheckBox>().Any(),"Payment has no verification checkbox or mandatory provider-reference field");
        var cash=Descendants(overlay).OfType<TextBox>().Single(t=>t.Name=="DigitalTendered");cash.Text="400";Capture(window,System.IO.Path.Combine(screens,"digital-cart-payment.png"));
        Descendants(overlay).OfType<Button>().Single(b=>b.Name=="CompleteDigitalPayment").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for(var wait=0;!Descendants(window).OfType<ReceiptPreviewWindow>().Any()&&wait<400;wait++){await Task.Delay(25);Pump();}
        var receipt=Descendants(window).OfType<ReceiptPreviewWindow>().Single();Assert(digital.Cart.Count==0&&!digital.HasDraft,"Payment clears the cart after the complete invoice is saved");
        var invoice=await db.ServiceCheckouts.AsNoTracking().Include(c=>c.Lines).SingleAsync();Assert(invoice.Total==355&&invoice.Tendered==400&&invoice.Change==45&&invoice.Lines.Count==2,"One invoice contains both selected services and correct change");Assert(await db.ServiceTransactions.CountAsync()==before+2&&await db.Products.CountAsync()==products,"Digital checkout posts its service rows without physical inventory");Assert(invoice.Lines.All(t=>t.Reference==""),"Provider references are optional");
        Assert(((Grid)window.Content).Children.OfType<Grid>().Count(g=>Panel.GetZIndex(g)==int.MaxValue)==1,"Service invoice has one modal overlay");Capture(window,System.IO.Path.Combine(screens,"digital-cart-invoice.png"));Descendants(receipt).OfType<Button>().Single(b=>b.Name=="CloseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for(var wait=0;InAppDialog.IsOpen&&wait<100;wait++){await Task.Delay(25);Pump();}
        Mode("Service_TRANSFER").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();radios=Descendants(digital).OfType<RadioButton>().ToArray();radios.Single(r=>Equals(r.Content,LocalizationManager.Instance["Service_CASHOUT"])).IsChecked=true;Pump();
        radios=Descendants(digital).OfType<RadioButton>().ToArray();Assert(radios.Length==2&&radios.Count(r=>r.IsChecked==true)==1,"Online transfer and cash out share one mutually exclusive form");entry=Descendants(digital).OfType<BusinessForm>().Single();((TextBox)entry.Fields["AmountText"]).Text="1000";((TextBox)entry.Fields["FeeText"]).Text="20";Descendants(entry).OfType<Button>().Single(b=>b.Name=="AddDigitalLine").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Assert(digital.Cart.Single().Total==-980&&!digital.OpenCardCommand.CanExecute(null),"Cash out shows a net customer payout and uses cash settlement");
        Capture(window,System.IO.Path.Combine(screens,"digital-cashout-cart.png"));Assert(InvokeDigitalShortcut(window,shell,Key.F4,ModifierKeys.None),"F4 clears the digital cart");Pump();Assert(!digital.HasDraft&&digital.Cart.Count==0,"Clear resets pending digital entry and cart");
        await shell.ShowReportsCommand.ExecuteAsync(null);Pump();var reports=Descendants(window).OfType<ReportsView>().Single();var reportTabs=Descendants(reports).OfType<TabControl>().First().Items.OfType<TabItem>().ToArray();reportTabs.Single(t=>t.Name=="DigitalHistoryTab").IsSelected=true;Pump();for(var wait=0;!Descendants(reports).OfType<DataGrid>().Any(t=>t.Items.Count>0)&&wait<100;wait++){await Task.Delay(25);Pump();}Assert(Descendants(reports).OfType<DataGrid>().Any(t=>t.Items.OfType<ServiceHistoryRow>().Any(r=>r.Number==invoice.ReceiptNumber)),"Digital history retains grouped invoice numbers for reprinting");
    }
    private static bool InvokeDigitalShortcut(Window window,MainViewModel shell,Key key,ModifierKeys modifiers)
    {
        var type=typeof(MainWindow).Assembly.GetType("SupermarketPos.Wpf.Common.Shortcuts")!;return(bool)type.GetMethod("TryRun",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.Invoke(null,[window,shell,key,modifiers])!;
    }
}
