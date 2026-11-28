using System.Reflection;
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
        for(var wait=0;!Descendants(digital).OfType<TextBox>().Any(t=>t.Name=="DigitalAmount")&&wait<200;wait++){await Task.Delay(25);Pump();}
        Button Mode(string key)=>Descendants(digital).OfType<Button>().First(b=>Equals(b.Content,LocalizationManager.Instance[key]));
        BusinessForm Form()=>Descendants(digital).OfType<BusinessForm>().Single();
        TextBox Field(string property)=>(TextBox)Form().Fields[property];
        var db=sp.GetRequiredService<PosDbContext>();var products=await db.Products.CountAsync();var before=await db.ServiceTransactions.CountAsync();
        Assert(!Descendants(digital).OfType<DataGrid>().Any()&&!Descendants(digital).OfType<Button>().Any(b=>b.Name=="AddDigitalLine"),"Normal digital flow has no cart table or Add-to-cart step");
        Assert(!Descendants(digital).OfType<CheckBox>().Any(),"No provider verification checkbox");
        var radio=Descendants(digital).OfType<RadioButton>().ToArray();Assert(radio.Length==2&&radio.Count(r=>r.IsChecked==true)==1,"Mobile Load has exclusive Prepaid/Postpaid choices");radio[1].IsChecked=true;Pump();
        Field("AmountText").Text="100";Field("Recipient").Text="03001234";Field("FeeText").Text="5";
        await digital.OpenCashCommand.ExecuteAsync(null);Pump();Assert(!InAppDialog.IsOpen&&digital.Cart.Count==0,"Invalid phone is blocked on the same form before payment");
        Field("Recipient").Text="03001234567";Assert(Field("CustomerName").Text=="","Customer name is optional and starts empty");
        Assert(InvokeDigitalShortcut(window,shell,Key.S,ModifierKeys.Control),"Ctrl+S saves direct transaction draft");
        var saved=digital.Cart.Single().ConfirmationId;
        digital.ClearCartCommand.Execute(null);Assert(InvokeDigitalShortcut(window,shell,Key.O,ModifierKeys.Control|ModifierKeys.Shift),"Ctrl+Shift+O resumes direct transaction draft");
        for(var wait=0;digital.Cart.Count==0&&wait<100;wait++){await Task.Delay(25);Pump();}
        Assert(digital.Cart.Count==1,"Draft resumes: pending="+digital.HasDraft+", available="+digital.ResumeDraftCommand.CanExecute(null)+", hint="+shell.ShortcutHint+", status="+string.Join(" / ",Descendants(digital).OfType<TextBlock>().Select(t=>t.Text).Where(t=>!string.IsNullOrEmpty(t))));
        Assert(digital.Cart.Single().ConfirmationId==saved&&digital.Cart.Single().AmountText=="100","Draft retains transaction identity and amount");
        var store=sp.GetRequiredService<SupermarketPos.Infrastructure.Commercial.ProtectedLocalStore>();var draftName="digital-cart-"+sp.GetRequiredService<SupermarketPos.Application.Abstractions.IAuthService>().CurrentUser!.Id;
        var legacy=System.Text.Json.Nodes.JsonNode.Parse(store.Read(draftName)!)!;var lines=legacy["Lines"]!.AsArray();lines[0]!.AsObject().Remove("CustomerName");var extra=lines[0]!.DeepClone();extra["Key"]=Guid.NewGuid();extra["Amount"]="200";lines.Add(extra);store.Write(draftName,legacy.ToJsonString());
        digital.ClearCartCommand.Execute(null);await digital.ResumeDraftCommand.ExecuteAsync(null);Pump();
        Assert(digital.Cart.Count==2&&digital.Cart[0].ConfirmationId==saved&&digital.Cart[1].AmountText=="200","Old multi-entry drafts remain recoverable without losing keys or amounts");
        Assert(!Descendants(digital).OfType<DataGrid>().Any(),"Legacy draft review does not restore the normal cart UI");
        digital.ClearCartCommand.Execute(null);Pump();Field("AmountText").Text="100";Field("Recipient").Text="03001234567";Field("FeeText").Text="5";
        Assert(InvokeDigitalShortcut(window,shell,Key.F1,ModifierKeys.None),"F1 opens payment directly from form");
        Pump();Assert(InAppDialog.IsOpen,"Direct payment reuses the shared in-app dialog");
        var overlay=((Grid)window.Content).Children.OfType<Grid>().Last();
        Descendants(overlay).OfType<Button>().Single(b=>Equals(b.Content,LocalizationManager.Instance["Common_Cancel"])).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for(var wait=0;InAppDialog.IsOpen&&wait<100;wait++){await Task.Delay(25);Pump();}
        Pump();Assert(Field("Recipient").Text=="03001234567","Cancel keeps the original transaction fields");
        Assert(InvokeDigitalShortcut(window,shell,Key.F1,ModifierKeys.None),"F1 reopens the same transaction");Pump();
        overlay=((Grid)window.Content).Children.OfType<Grid>().Last();Descendants(overlay).OfType<TextBox>().Single(t=>t.Name=="DigitalTendered").Text="110";
        Descendants(overlay).OfType<Button>().Single(b=>b.Name=="CompleteDigitalPayment").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for(var wait=0;!Descendants(window).OfType<ReceiptPreviewWindow>().Any()&&wait<400;wait++){await Task.Delay(25);Pump();}
        var receipt=Descendants(window).OfType<ReceiptPreviewWindow>().Single();
        var invoice=await db.ServiceCheckouts.AsNoTracking().Include(c=>c.Lines).SingleAsync();
        Assert(invoice.Total==105&&invoice.Change==5&&invoice.Lines.Count==1,"One direct transaction creates one invoice and correct change");
        Assert(await db.ServiceTransactions.CountAsync()==before+1&&await db.Products.CountAsync()==products,"Digital checkout never creates physical inventory");
        Assert(invoice.Lines.Single().AccountType=="Postpaid"&&invoice.Lines.Single().CustomerName=="","Load account type persists without requiring customer profile");
        Capture(window,System.IO.Path.Combine(screens,"direct-digital-invoice.png"));
        Descendants(receipt).OfType<Button>().Single(b=>b.Name=="CloseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for(var wait=0;InAppDialog.IsOpen&&wait<100;wait++){await Task.Delay(25);Pump();}
        Assert(!digital.HasDraft&&digital.Cart.Count==0,"Completed transaction clears its pending state");
        Mode("Service_PACKAGE").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        for(var wait=0;!Descendants(digital).OfType<ListBox>().Any(l=>l.Name=="PackageChoices"&&l.Items.Count>0)&&wait<100;wait++){await Task.Delay(25);Pump();}
        Assert(!Descendants(digital).OfType<RadioButton>().Any(),"Packages have no Prepaid/Postpaid switch");
        var list=Descendants(digital).OfType<ListBox>().Single(l=>l.Name=="PackageChoices");
        Descendants(list).OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        Assert(digital.Cart.Count==1&&!InAppDialog.IsOpen&&!Descendants(digital).OfType<DataGrid>().Any(),"Package selection displays recipient/payment form without a cart or popup");
        Field("AmountText").Text="250";Field("Recipient").Text="03001234567";
        foreach(var lang in new[]{"en","ur"})foreach(var size in new[]{100,200})
        {
            LocalizationManager.Instance.SetLanguage(lang);sp.GetRequiredService<DesktopTextSize>().Save(size);await digital.OpenSalesAsync();Pump();
            for(var wait=0;!Descendants(digital).OfType<ListBox>().Any(l=>l.Name=="PackageChoices"&&l.Items.Count>0)&&wait<100;wait++){await Task.Delay(25);Pump();}
            var choices=Descendants(digital).OfType<ListBox>().Single(l=>l.Name=="PackageChoices");Assert(choices.ActualHeight>=100,$"Package viewport stays usable at {lang}/{size}");
            var phone=Field("Recipient");var phoneBounds=phone.TransformToAncestor(window).TransformBounds(new Rect(0,0,phone.ActualWidth,phone.ActualHeight));
            Assert(phoneBounds.Top>=0&&phoneBounds.Bottom<=window.ActualHeight,"Selected package recipient is scrolled into the visible workspace at "+lang+"/"+size);
            CheckWindow(window,$"direct-digital-{lang}-{size}");Capture(window,System.IO.Path.Combine(screens,$"direct-digital-{lang}-{size}.png"));
        }
        LocalizationManager.Instance.SetLanguage("en");sp.GetRequiredService<DesktopTextSize>().Save(100);Pump();
        Assert(InvokeDigitalShortcut(window,shell,Key.F,ModifierKeys.Control),"Ctrl+F focuses digital search");Pump();
        Assert(Descendants(digital).OfType<TextBox>().Single(t=>t.Name=="PackageSearch").IsKeyboardFocused,"Package search receives keyboard focus");
        digital.ClearCartCommand.Execute(null);Pump();Mode("Service_TRANSFER").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        radio=Descendants(digital).OfType<RadioButton>().ToArray();radio.Single(r=>Equals(r.Content,LocalizationManager.Instance["Service_CASHOUT"])).IsChecked=true;Pump();
        Field("AmountText").Text="1000";Field("Recipient").Text="03001234567";Field("FeeText").Text="20";
        Assert(!digital.OpenCardCommand.CanExecute(null),"Cash out permits cash settlement only");
        Capture(window,System.IO.Path.Combine(screens,"direct-digital-cashout.png"));
        Assert(InvokeDigitalShortcut(window,shell,Key.F4,ModifierKeys.None),"F4 clears a direct transaction");Pump();Assert(!digital.HasDraft,"Clear resets pending form");
        await DigitalDefaultChargeChecks(digital,window);
        await shell.ShowCustomersCommand.ExecuteAsync(null);Pump();
        var customers=(CustomersViewModel)shell.CurrentViewModel;customers.CustomerTab=1;Pump();
        Assert(customers.DigitalAvailable&&customers.DigitalCustomers.Single().Identifier=="923001234567","Customers has a separate digital list keyed by recipient phone");
        for(var wait=0;customers.DigitalHistory.Count==0&&wait<100;wait++){await Task.Delay(25);Pump();}
        Assert(customers.DigitalHistory.Single().Number==invoice.ReceiptNumber,"Digital customer history opens the saved invoice");
        customers.DigitalSearch="923001234567";Assert(customers.DigitalCustomers.Count==1,"Digital customers can be found by phone");
        var editName=customers.EditDigitalNameCommand.ExecuteAsync(null);Pump();
        var nameForm=Descendants(window).OfType<BusinessForm>().Single(f=>f.Fields.ContainsKey("Name"));((TextBox)nameForm.Fields["Name"]).Text="Digital visitor";
        Descendants(nameForm).OfType<Button>().Single(b=>Equals(b.Content,LocalizationManager.Instance["Profile_Save"])).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await editName;Pump();
        Assert(customers.SelectedDigitalCustomer!.Name=="Digital visitor","Optional customer name saves separately from physical accounts");
        Capture(window,System.IO.Path.Combine(screens,"digital-customers.png"));
        foreach(var lang in new[]{"en","ur"})
        {
            LocalizationManager.Instance.SetLanguage(lang);sp.GetRequiredService<DesktopTextSize>().Save(200);Pump();
            foreach(var grid in Descendants(window).OfType<DataGrid>())
            {
                var row=Descendants(grid).OfType<DataGridRow>().FirstOrDefault();
                Assert(row is not null&&row.TransformToAncestor(grid).TransformBounds(new Rect(0,0,row.ActualWidth,row.ActualHeight)).Bottom<=grid.ActualHeight,"Digital customer table keeps space for its data row at "+lang+"/200");
            }
            Capture(window,System.IO.Path.Combine(screens,$"digital-customers-{lang}-200.png"));CheckWindow(window,$"digital-customers-{lang}-200");
        }
        LocalizationManager.Instance.SetLanguage("en");sp.GetRequiredService<DesktopTextSize>().Save(100);Pump();
        Assert(InvokeDigitalShortcut(window,shell,Key.F,ModifierKeys.Control),"Ctrl+F reaches digital customer search");Pump();
        Assert(Descendants(window).OfType<TextBox>().Single(t=>t.Name=="DigitalCustomerSearch").IsKeyboardFocused,"Digital customer search receives focus");
        var reopen=customers.OpenDigitalReceiptCommand.ExecuteAsync(null);
        for(var wait=0;!Descendants(window).OfType<ReceiptPreviewWindow>().Any()&&wait<400;wait++){await Task.Delay(25);Pump();}
        var customerReceipt=Descendants(window).OfType<ReceiptPreviewWindow>().Single();
        Descendants(customerReceipt).OfType<Button>().Single(b=>b.Name=="CloseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await reopen;Pump();
        Assert(!InAppDialog.IsOpen,"Digital customer receipt reopens and closes in the same window");
        customers.DigitalSearch="no match";Assert(customers.DigitalCustomers.Count==0,"Digital customer search filters results");customers.DigitalSearch="";
        await shell.ShowReportsCommand.ExecuteAsync(null);Pump();var reports=Descendants(window).OfType<ReportsView>().Single();
        Descendants(reports).OfType<TabControl>().First().Items.OfType<TabItem>().Single(t=>t.Name=="DigitalHistoryTab").IsSelected=true;Pump();
        for(var wait=0;!Descendants(reports).OfType<TextBox>().Any(t=>t.Name=="DigitalHistorySearch")&&wait<100;wait++){await Task.Delay(25);Pump();}
        var historySearch=Descendants(reports).OfType<TextBox>().Single(t=>t.Name=="DigitalHistorySearch");historySearch.Text="no match";Pump();
        Assert(!Descendants(reports).OfType<DataGrid>().Single().Items.OfType<ServiceHistoryRow>().Any(),"Digital reports filter by recipient/name");
        historySearch.Text="Digital visitor";Pump();Assert(Descendants(reports).OfType<DataGrid>().Single().Items.OfType<ServiceHistoryRow>().Count()==1,"Report search includes saved customer aliases");
        historySearch.Text="03001234567";Pump();Assert(Descendants(reports).OfType<DataGrid>().Single().Items.OfType<ServiceHistoryRow>().Single().Number==invoice.ReceiptNumber,"Digital reports keep saved receipt and phone");
        var dates=Descendants(reports).OfType<DatePicker>().ToArray();dates[0].SelectedDate=DateTime.Today.AddDays(1);Pump();
        Assert(!Descendants(reports).OfType<DataGrid>().Single().Items.OfType<ServiceHistoryRow>().Any(),"Digital history date filter excludes out-of-range transactions");
        dates[0].SelectedDate=DateTime.Today;dates[1].SelectedDate=DateTime.Today;Pump();
        Assert(Descendants(reports).OfType<DataGrid>().Single().Items.OfType<ServiceHistoryRow>().Count()==1,"Digital history date range includes both selected endpoints");
        var native=typeof(MainWindow).Assembly.GetType("SupermarketPos.Wpf.Common.WindowsNavigation")!;
        var hook=Activator.CreateInstance(native,BindingFlags.Instance|BindingFlags.NonPublic,null,[window],null)!;
        var message=native.GetMethod("Message",BindingFlags.Instance|BindingFlags.NonPublic)!;
        foreach(var code in new[]{0x20,0x0312}){object[] args=[IntPtr.Zero,code,new IntPtr(long.MaxValue),IntPtr.Zero,false];Assert((IntPtr)message.Invoke(hook,args)! == IntPtr.Zero&&!(bool)args[4],"Large native message parameter never overflows the shortcut hook");}
        ((IDisposable)hook).Dispose();
    }
    private static bool InvokeDigitalShortcut(Window window,MainViewModel shell,Key key,ModifierKeys modifiers)
    {
        var type=typeof(MainWindow).Assembly.GetType("SupermarketPos.Wpf.Common.Shortcuts")!;return(bool)type.GetMethod("TryRun",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[window,shell,key,modifiers])!;
    }
    private static async Task DigitalDefaultChargeChecks(DigitalServicesView digital,Window window)
    {
        Assert(!Descendants(digital).OfType<Button>().Any(b=>Equals(b.Content,LocalizationManager.Instance["Common_More"])),"Direct transaction has no More button");
        Descendants(digital).OfType<Button>().Single(b=>Equals(b.Content,LocalizationManager.Instance["Digital_DefaultCharges"])).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        var charges=Descendants(window).OfType<BusinessForm>().Single(f=>f.Fields.ContainsKey("Mode"));((ComboBox)charges.Fields["Mode"]).SelectedValue="PERCENT";((TextBox)charges.Fields["Value"]).Text="2";
        var popup=(StackPanel)charges.Parent;Descendants(popup).OfType<Button>().Single(b=>Equals(b.Content,LocalizationManager.Instance["Profile_Save"])).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for(var i=0;InAppDialog.IsOpen&&i<100;i++){await Task.Delay(25);Pump();}
        var form=Descendants(digital).OfType<BusinessForm>().Single();var amount=(TextBox)form.Fields["AmountText"];var fee=(TextBox)form.Fields["FeeText"];
        amount.Text="1000";Assert(decimal.Parse(fee.Text,System.Globalization.CultureInfo.InvariantCulture)==20,"Saved 2% calculates 20 on 1000");
        fee.Text="25";amount.Text="2000";Assert(fee.Text=="25","One-time fee override stays local");
        var pay=Descendants(digital).OfType<Button>().Single(b=>b.Name=="DigitalCash");
        Assert(pay.IsDefault&&((FrameworkElement)pay.Parent).HorizontalAlignment==HorizontalAlignment.Left,"Payment CTA stays bottom-left and supports Enter");
        digital.ClearCartCommand.Execute(null);Pump();
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
