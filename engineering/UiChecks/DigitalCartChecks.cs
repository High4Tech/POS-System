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
        var loadChoices=Descendants(digital).OfType<RadioButton>().ToArray();Assert(loadChoices.Length==2&&loadChoices.Count(r=>r.IsChecked==true)==1,"Prepaid/Postpaid belongs to Mobile Load");loadChoices[1].IsChecked=true;Pump();
        var entry=Descendants(digital).OfType<BusinessForm>().Single();((TextBox)entry.Fields["AmountText"]).Text="100";((TextBox)entry.Fields["Recipient"]).Text="03001234567";((TextBox)entry.Fields["FeeText"]).Text="5";
        Descendants(digital).OfType<Button>().Single(b=>b.Name=="AddDigitalLine").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        Assert(digital.Cart.Count==1&&digital.HasDraft,"Mobile load adds to the digital cart without posting a transaction");digital.Cart[0].CostText="80";
        Button Mode(string key)=>Descendants(digital).OfType<Button>().First(b=>Equals(b.Content,LocalizationManager.Instance[key]));Mode("Service_PACKAGE").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        for(var wait=0;!Descendants(digital).OfType<ListBox>().Any(l=>l.Name=="PackageChoices"&&l.Items.Count>0)&&wait<100;wait++){await Task.Delay(25);Pump();}
        Assert(!Descendants(digital).OfType<RadioButton>().Any(),"Packages have no account-type switch");var radios=Array.Empty<RadioButton>();
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
        radios=Descendants(digital).OfType<RadioButton>().ToArray();Assert(radios.Length==2&&radios.Count(r=>r.IsChecked==true)==1,"Online transfer and cash out share one mutually exclusive form");entry=Descendants(digital).OfType<BusinessForm>().Single();((TextBox)entry.Fields["Recipient"]).Text="03001234567";((TextBox)entry.Fields["AmountText"]).Text="1000";((TextBox)entry.Fields["FeeText"]).Text="20";Descendants(digital).OfType<Button>().Single(b=>b.Name=="AddDigitalLine").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Assert(digital.Cart.Single().Total==-980&&!digital.OpenCardCommand.CanExecute(null),"Cash out shows a net customer payout and uses cash settlement");
        Capture(window,System.IO.Path.Combine(screens,"digital-cashout-cart.png"));Assert(InvokeDigitalShortcut(window,shell,Key.F4,ModifierKeys.None),"F4 clears the digital cart");Pump();Assert(!digital.HasDraft&&digital.Cart.Count==0,"Clear resets pending digital entry and cart");
        await DigitalDefaultChargeChecks(digital,window);
        await shell.ShowReportsCommand.ExecuteAsync(null);Pump();var reports=Descendants(window).OfType<ReportsView>().Single();var reportTabs=Descendants(reports).OfType<TabControl>().First().Items.OfType<TabItem>().ToArray();reportTabs.Single(t=>t.Name=="DigitalHistoryTab").IsSelected=true;Pump();for(var wait=0;!Descendants(reports).OfType<DataGrid>().Any(t=>t.Items.Count>0)&&wait<100;wait++){await Task.Delay(25);Pump();}Assert(Descendants(reports).OfType<DataGrid>().Any(t=>t.Items.OfType<ServiceHistoryRow>().Any(r=>r.Number==invoice.ReceiptNumber)),"Digital history retains grouped invoice numbers for reprinting");
    }
    private static bool InvokeDigitalShortcut(Window window,MainViewModel shell,Key key,ModifierKeys modifiers)
    {
        var type=typeof(MainWindow).Assembly.GetType("SupermarketPos.Wpf.Common.Shortcuts")!;return(bool)type.GetMethod("TryRun",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.Invoke(null,[window,shell,key,modifiers])!;
    }
    private static async Task DigitalDefaultChargeChecks(DigitalServicesView digital,Window window)
    {
        void SendKey(FrameworkElement target,Key key)=>target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(target)!,0,key){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        Assert(!Descendants(digital).OfType<Button>().Any(b=>Equals(b.Content,LocalizationManager.Instance["Common_More"])),"Cart has no More button");
        var table=Descendants(digital).OfType<DataGrid>().Single(g=>g.Name=="DigitalCart");Assert(!table.Columns.Any(c=>Equals(c.Header,LocalizationManager.Instance["Digital_ReferenceOptional"])),"Cart has no provider-reference column");
        Descendants(digital).OfType<Button>().Single(b=>Equals(b.Content,LocalizationManager.Instance["Digital_DefaultCharges"])).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        var charges=Descendants(window).OfType<BusinessForm>().Single(f=>f.Fields.ContainsKey("Mode"));((ComboBox)charges.Fields["Mode"]).SelectedValue="PERCENT";((TextBox)charges.Fields["Value"]).Text="2";
        var popup=(StackPanel)charges.Parent;var save=Descendants(popup).OfType<Button>().Single(b=>Equals(b.Content,LocalizationManager.Instance["Profile_Save"]));Assert(save.IsDefault,"Save defaults supports Enter");save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for(var i=0;InAppDialog.IsOpen&&i<100;i++){await Task.Delay(25);Pump();}Pump();
        var form=Descendants(digital).OfType<BusinessForm>().Single();var amount=(TextBox)form.Fields["AmountText"];var phone=(TextBox)form.Fields["Recipient"];var fee=(TextBox)form.Fields["FeeText"];
        amount.Text="1000";Assert(decimal.Parse(fee.Text,System.Globalization.CultureInfo.InvariantCulture)==20,"Saved 2% fee calculates 20 on 1000");fee.Text="25";amount.Text="2000";Assert(fee.Text=="25","One-time fee override survives an amount edit");
        phone.Text="03001234";SendKey(phone,Key.Enter);Pump();Assert(digital.Cart.Count==0,"Short recipient number cannot enter the cart");phone.Text="03001234567";SendKey(phone,Key.Enter);Pump();Assert(digital.Cart.Count==1&&digital.Cart[0].FeeText=="25","Enter adds a valid transaction with its one-time override");
        amount.Text="1000";phone.Text="03001234567";Assert(decimal.Parse(fee.Text,System.Globalization.CultureInfo.InvariantCulture)==20,"Next transaction keeps the saved provider percentage");SendKey(phone,Key.Enter);Pump();var automatic=digital.Cart.Last();automatic.AmountText="1500";Assert(decimal.Parse(automatic.FeeText,System.Globalization.CultureInfo.InvariantCulture)==30&&digital.Cart.First().FeeText=="25","Automatic row fees follow amounts while another row's override stays fixed");
        var add=Descendants(digital).OfType<Button>().Single(b=>b.Name=="AddDigitalLine");Assert(add.IsDefault&&((FrameworkElement)add.Parent).HorizontalAlignment==HorizontalAlignment.Left,"Add CTA stays at the bottom left and supports Enter");digital.ClearCartCommand.Execute(null);Pump();
        var shortcuts=typeof(MainWindow).Assembly.GetType("SupermarketPos.Wpf.Common.Shortcuts")!;var reference=(System.Runtime.CompilerServices.ITuple)shortcuts.GetMethod("Describe",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.Invoke(null,[window,(MainViewModel)window.DataContext])!;var bindings=((System.Collections.IEnumerable)reference[1]!).Cast<object>().Select(r=>(string)r.GetType().GetProperty("Shortcut")!.GetValue(r)!).ToArray();foreach(var key in new[]{"H","S","D","P","T","R","C","X","Z"})Assert(bindings.Contains("Win + "+key)&&bindings.Contains("Alt + "+key),"Windows and Alt navigation are listed for "+key);
        var native=typeof(MainWindow).Assembly.GetType("SupermarketPos.Wpf.Common.WindowsNavigation")!;var probe=native.GetMethod("Probe",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;var conflicts=new List<string>();foreach(var key in new[]{Key.H,Key.S,Key.D,Key.P,Key.T,Key.R,Key.C,Key.X,Key.Z})if(!(bool)probe.Invoke(null,[key])!)conflicts.Add(key.ToString());Console.WriteLine("Windows-reserved/unavailable navigation keys: "+string.Join(", ",conflicts));
        var model=(MainViewModel)window.DataContext;
        foreach(var(key,screen) in new[]{(Key.H,"dashboard"),(Key.S,"pos"),(Key.D,"digital"),(Key.P,"purchases"),(Key.T,"products"),(Key.R,"reports"),(Key.C,"customers"),(Key.Z,"settings"),(Key.D,"digital")})
        {
            Assert(InvokeDigitalShortcut(window,model,key,ModifierKeys.Alt),"Alt navigation routes "+key);
            for(var i=0;model.ActiveScreen!=screen&&i<200;i++){await Task.Delay(25);Pump();}Pump();Assert(model.ActiveScreen==screen,"Alt navigation opens "+screen);
        }
        _ = window.Dispatcher.BeginInvoke(new Action(()=>System.Windows.Application.Current.Windows.OfType<ScannerConnectionWindow>().Single().Close()),System.Windows.Threading.DispatcherPriority.Background);
        Assert(InvokeDigitalShortcut(window,model,Key.X,ModifierKeys.Alt),"Alt+X opens the existing scanner connection dialog");Pump();
    }
}
