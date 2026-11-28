using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Infrastructure;
using SupermarketPos.Infrastructure.Commercial;
using SupermarketPos.Infrastructure.Persistence;
using SupermarketPos.Wpf;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Wpf.ViewModels;
using SupermarketPos.Wpf.Views;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

internal static partial class Program
{
    private static int _checks;
    [STAThread] public static int Main(string[] args)
    {
        try
        {
            var root=Path.GetFullPath(args.Length>0?args[0]:"engineering/qa-data/"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            AppContext.SetData("SupermarketPos.DataDirectory",root);AppContext.SetData("SupermarketPos.IsolatedDataDirectory",true);
            var app=new System.Windows.Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            _=AppBrand.Name;
            Console.WriteLine($"WPF rendering tier: {System.Windows.Media.RenderCapability.Tier >> 16}; process rendering: {System.Windows.Media.RenderOptions.ProcessRenderMode}");
            app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/SupermarketPos;component/Resources/Theme.xaml",UriKind.Relative)});
            app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/SupermarketPos;component/Resources/DataEntry.xaml",UriKind.Relative)});
            app.Resources["BoolToVis"]=new BooleanToVisibilityConverter();
            app.Resources["StrToVis"]=new StringToVisibilityConverter();app.Resources["InverseBoolToVis"]=new InverseBooleanToVisibilityConverter();
            app.Resources["CatColor"]=new CategoryColorConverter();app.Resources["PathToImage"]=new PathToImageConverter();app.Resources["HasImageToVis"]=new HasImageToVisibilityConverter();app.Resources["Initials"]=new InitialsConverter();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var services=new ServiceCollection();var storage=new PosStorage();services.AddSingleton(storage);
            var protectedStore=new ProtectedLocalStore(Path.Combine(root,"Protected"));services.AddSingleton(protectedStore);
            services.AddSingleton(new Diagnostics(Path.Combine(root,"Diagnostics")));
            using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);var options=new PublisherOptions("http://127.0.0.1:8787",key.ExportSubjectPublicKeyInfoPem(),false,"testing");
            services.AddSingleton(options);services.AddSingleton(new LicenseClient(options,protectedStore,ProtectedLocalStore.DeviceHash()));services.AddSingleton<SoftwareUpdates>();
            services.AddPos(storage.DatabasePath);
            PanelHeights.Load(storage);
            typeof(App).GetMethod("RegisterViewModels",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[services]);
            var provider=services.BuildServiceProvider();var scope=provider.CreateAsyncScope();var sp=scope.ServiceProvider;
            Wait(DependencyInjection.InitializeDatabaseAsync(sp,createDefaultUser:false));
            var setup=sp.GetRequiredService<IBusinessSetup>();var auth=sp.GetRequiredService<IAuthService>();
            Assert(!Wait(setup.LoadAsync()).HasUsers,"Fresh startup never creates a default admin account");
            Wait(SeedUiCatalogFixture(sp));
            // Check actual WPF two-way bindings, including init-only profile properties.
            var profile=new SupermarketPos.Domain.Business.BusinessProfile();var form=new BusinessForm();var input=form.Text("Settings_StoreName",profile,nameof(profile.StoreName));input.Text="Bound shop";input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();Assert(profile.StoreName=="Bound shop","Business fields bind to the saved profile");
            var screenshots=Directory.CreateDirectory(Path.Combine(root,"screens")).FullName;
            if(args.Contains("--signed-retail-package"))
            {
                Wait(SignedRetailPackageChecks(args,sp.GetRequiredService<SoftwareUpdates>()));
                Wait(scope.DisposeAsync().AsTask());Wait(provider.DisposeAsync().AsTask());
                Console.WriteLine($"PASS: {_checks} signed Retail package/feed assertions.");app.Shutdown();return 0;
            }
            if(args.Contains("--completion-checks"))
            {
                Wait(CompletionUpdateChecks(root,sp.GetRequiredService<SoftwareUpdates>()));
                Wait(CompletionBusinessChecks(sp,screenshots),TimeSpan.FromMinutes(3));
                Wait(scope.DisposeAsync().AsTask());Wait(provider.DisposeAsync().AsTask());
                Console.WriteLine($"PASS: {_checks} completion/update assertions.");app.Shutdown();return 0;
            }
            if(args.Contains("--trial-setup"))
            {
                if(args.Contains("--standalone"))Assert(RetailTestEdition.Standalone&&RetailTestEdition.Enabled&&RetailTestEdition.DefaultDataDirectory.EndsWith(Path.Combine("WijganduRetail","Data"),StringComparison.OrdinalIgnoreCase),"Published standalone edition has its own local data folder");
                Wait(RetailTestSetupChecks(sp,screenshots));Wait(scope.DisposeAsync().AsTask());Wait(provider.DisposeAsync().AsTask());
                Console.WriteLine($"PASS: {_checks} isolated setup assertions.");app.Shutdown();return 0;
            }
            if(args.Contains("--retail-flow")||args.Contains("--digital-cart"))
            {
                var retailProfile=new SupermarketPos.Domain.Business.BusinessProfile{StoreName="Tech Retail verification",BusinessType="electronics"};retailProfile.ApplyBusinessDefaults();
                Wait(setup.CompleteAsync(retailProfile,"retail-owner","IsolatedRetail123!"));
                Wait(auth.LoginAsync("retail-owner","IsolatedRetail123!"));
                Wait(RetailKeyboardFlow(sp,screenshots,args.Contains("--digital-cart")),TimeSpan.FromMinutes(8));
                Wait(setup.SaveDraftAsync(retailProfile,Wait(setup.LoadAsync()).Step));
                Wait(InvoiceActionChecks(sp,storage,root,screenshots));
                Wait(scope.DisposeAsync().AsTask());Wait(provider.DisposeAsync().AsTask());
                Console.WriteLine($"PASS: {_checks} retail keyboard assertions.");app.Shutdown();return 0;
            }
            if(args.Contains("--keyboard-smoke"))
            {
                Wait(KeyboardSmokeAsync(sp));
                Dispatcher.Run();
                Wait(scope.DisposeAsync().AsTask());
                Wait(provider.DisposeAsync().AsTask());
                return 0;
            }
            if(args.Contains("--audit-probes"))
            {
                Wait(setup.CompleteAsync(new(){StoreName="Audit shop"},"audit-owner","IsolatedAudit123!"));
                Wait(auth.LoginAsync("audit-owner","IsolatedAudit123!"));
                Wait(ShortcutReferenceChecks(sp,screenshots),TimeSpan.FromMinutes(3));
                Wait(FeatureAuditChecks(sp,root),TimeSpan.FromMinutes(3));
                Wait(scope.DisposeAsync().AsTask());Wait(provider.DisposeAsync().AsTask());
                Console.WriteLine($"PASS: {_checks} focused audit assertions.");app.Shutdown();return 0;
            }
            foreach(var lang in new[]{"en","ur"})foreach(var size in new[]{100,200})
            {
                LocalizationManager.Instance.SetLanguage(lang);sp.GetRequiredService<DesktopTextSize>().Save(size);
                if(lang=="ur")
                {
                    var face=new Typeface(LocalizationManager.Instance.FontFamily,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);
                    Assert(face.TryGetGlyphTypeface(out var glyph)&&glyph.FamilyNames.Values.Any(n=>n.Contains("Noto Nastaliq Urdu")),"Supplied Urdu font resolves from bundled resources, without OS installation");
                }
                var vm=sp.GetRequiredService<OnboardingViewModel>();Wait(vm.LoadAsync());
                var window=new OnboardingWindow(vm,setup,auth,sp.GetRequiredService<LicenseClient>(),storage);window.Show();Pump();
                Assert(window.WindowStyle==WindowStyle.SingleBorderWindow&&WindowCaption.GetNative(window),"Window uses native system caption controls");Assert(window.FlowDirection==FlowDirection.LeftToRight&&((FrameworkElement)window.Content).FlowDirection==LocalizationManager.Instance.FlowDirection,"Native chrome stays LTR while English/Urdu content keeps its direction");
                Assert(vm.Step==0,"Language is the first setup page");
                vm.Profile=vm.Profile with{Registered=true,Language=lang};
                foreach(var step in Enumerable.Range(0,9))
                {
                    vm.Step=step;typeof(OnboardingWindow).GetMethod("Draw",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,null);Pump();
                    CheckWindow(window,$"onboarding-{lang}-{size}-{step}");
                    if(step is 0 or 5 or 7)Capture(window,Path.Combine(screenshots,$"onboarding-{lang}-{size}-{step}.png"));
                }
                window.Close();Pump();
            }
            OnboardingInteractionChecks(sp,setup,auth,storage,screenshots);
            Wait(setup.CompleteAsync(new(){StoreName="QA Electronics",LegalName="QA Registered Shop",Registered=true,Address="Test road",City="Lahore",Ntn="1234567",Language="ur"},"owner","IsolatedTest123!"));
            Wait(auth.LoginAsync("owner","IsolatedTest123!"));
            Wait(ShortcutReferenceChecks(sp,screenshots),TimeSpan.FromMinutes(3));
            if(args.Contains("--shortcuts-only"))
            {
                Wait(scope.DisposeAsync().AsTask());Wait(provider.DisposeAsync().AsTask());Console.WriteLine($"PASS: {_checks} checks including the shortcut popup.");app.Shutdown();return 0;
            }
            if(args.Contains("--restoration-only"))
            {
                var current=Wait(setup.LoadAsync());current.Profile.BusinessType="pharmacy";current.Profile.MedicineTemplate="pakistan-drap";Wait(setup.SaveDraftAsync(current.Profile,current.Step));Wait(sp.GetRequiredService<SupermarketPos.Infrastructure.Business.MedicineCatalogService>().EnsureSelectedAsync());Wait(RestorationInteractionChecks(sp,screenshots));Wait(scope.DisposeAsync().AsTask());Wait(provider.DisposeAsync().AsTask());Console.WriteLine($"PASS: {_checks} focused restoration checks.");app.Shutdown();return 0;
            }
            foreach(var lang in new[]{"en","ur"})foreach(var size in new[]{100,200})
            {
                LocalizationManager.Instance.SetLanguage(lang);sp.GetRequiredService<DesktopTextSize>().Save(size);
                var window=sp.GetRequiredService<BusinessSettingsWindow>();window.Show();Pump();
                var tabs=(TabControl)window.FindName("Tabs");Assert(tabs.Items.Count==9,"Registered settings expose business features and all local/commercial sections");
                var positions=tabs.Items.Cast<TabItem>().Select(t=>t.TransformToAncestor(window).Transform(new Point(0,0))).ToArray();
                for(int i=0;i<tabs.Items.Count;i++){tabs.SelectedIndex=i;Pump();Assert(tabs.Items.Cast<TabItem>().Select((t,n)=>(t.TransformToAncestor(window).Transform(new Point(0,0))-positions[n]).Length).All(d=>d<.1),"Settings tabs retain their positions when selected");CheckWindow(window,$"settings-{lang}-{size}-{i}");if(i is 0 or 1 or 4)Capture(window,Path.Combine(screenshots,$"settings-{lang}-{size}-{i}.png"));}
                window.Close();Pump();
                var recovery=new OwnerRecoveryWindow(setup);recovery.Show();Pump();CheckWindow(recovery,$"recovery-{lang}-{size}");recovery.Close();Pump();
            }
            if(args.Contains("--performance")){Wait(PerformanceChecks(sp,root));Console.WriteLine("PASS: isolated performance measurements saved.");app.Shutdown();return 0;}
            Wait(InvoiceActionChecks(sp,storage,root,screenshots));
            Wait(RetailScreenChecks(sp,screenshots));
            Wait(PharmacyScreenChecks(sp,screenshots), TimeSpan.FromMinutes(3));
            var update=sp.GetRequiredService<SoftwareUpdates>();Wait(UpdateFeedChecks(root,update));
            var workerFixture=Path.GetFullPath("engineering/qa-data/worker-signed-fixture.json");
            if(File.Exists(workerFixture))
            {
                using var fixture=JsonDocument.Parse(File.ReadAllText(workerFixture));var f=fixture.RootElement;
                var client=new LicenseClient(new PublisherOptions("http://127.0.0.1:8787",f.GetProperty("publicKeyPem").GetString()!,false),new ProtectedLocalStore(Path.Combine(root,"CrossRuntimeActivation")),f.GetProperty("device").GetString()!);
                client.Import(f.GetProperty("certificate").GetRawText());Assert(client.Current?.Edition=="retail","Actual Worker/WebCrypto certificate verifies in Windows/.NET");
            }
            Wait(sp.GetRequiredService<ScannerController>().StopAsync());
            sp.GetRequiredService<PosDbContext>().Database.CloseConnection();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Wait(scope.DisposeAsync().AsTask());Wait(provider.DisposeAsync().AsTask());
            Console.WriteLine($"PASS: {_checks} desktop checks; isolated data and screenshots: {root}");app.Shutdown();return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    private static async Task UpdateFeedChecks(string root,SoftwareUpdates updates)
    {
        var folder=Directory.CreateDirectory(Path.Combine(root,"local-feed")).FullName;
        var release=new ReleaseClaims(1,"supermarketpos","retail","testing","1.0.1","1.0.0",1,1,"bugfix",DateTimeOffset.UtcNow,"SupermarketPos-1.0.1-full.nupkg",7,new('a',64),new('b',40),"Local test notes");
        var package=Path.Combine(folder,release.FileName);
        using(var zip=ZipFile.Open(package,ZipArchiveMode.Create))
        {
            using(var writer=new StreamWriter(zip.CreateEntry("SupermarketPos.nuspec").Open()))writer.Write("<package><metadata><id>SupermarketPos</id><version>1.0.1</version><authors>Local test</authors><description>Unsigned contract test only</description></metadata></package>");
            using(var writer=new StreamWriter(zip.CreateEntry("lib/net8.0/SupermarketPos.exe").Open()))writer.Write("Unsigned inert test bytes");
        }
        release=release with{Size=new FileInfo(package).Length,Sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(package)))};
        File.WriteAllText(Path.Combine(folder,"releases.testing.json"),SoftwareUpdates.LocalFeed(release));
        var locator=new TestVelopackLocator("SupermarketPos","1.0.0",root,null!);
        var manager=new UpdateManager(new SimpleFileSource(new DirectoryInfo(folder)),new UpdateOptions{ExplicitChannel="testing"},locator);
        var result=await manager.CheckForUpdatesAsync();Assert(result?.TargetFullRelease.Version.ToString()=="1.0.1","Velopack reads the authenticated local feed format");
        await manager.DownloadUpdatesAsync(result!);Assert(Directory.EnumerateFiles(root,release.FileName,SearchOption.AllDirectories).Count()>=2,"Velopack stages the local full package without installation");
        var bad=Path.Combine(folder,release.FileName);File.WriteAllText(bad,"changed");
        try{await SoftwareUpdates.VerifyPackageAsync(bad,release);throw new Exception("Tampered package accepted");}catch(CommercialException){Assert(true,"Modified update package rejected");}
        try{await updates.InstallAsync(()=>true,()=>Task.CompletedTask);throw new Exception("Active draft allowed");}catch(CommercialException e){Assert(e.Message=="Updates_FinishDrafts","Active sale/purchase prevents install");}
        try{WindowsPublisherTrust.Verify(typeof(App).Assembly.Location,new('b',40));throw new Exception("Unsigned package accepted");}catch(CommercialException){Assert(true,"Unsigned publisher rejected by Windows trust");}
    }
    private static void OnboardingInteractionChecks(IServiceProvider sp,IBusinessSetup setup,IAuthService auth,PosStorage storage,string screenshots)
    {
        LocalizationManager.Instance.SetLanguage("en");sp.GetRequiredService<DesktopTextSize>().Save(100);
        Wait(setup.SaveDraftAsync(new(){StoreName="Test",Logo="legacy-store-logo"},4));
        var vm=sp.GetRequiredService<OnboardingViewModel>();
        var window=new OnboardingWindow(vm,setup,auth,sp.GetRequiredService<LicenseClient>(),storage);window.Show();Pump();
        Assert(vm.Profile.Logo is null,"An unfinished onboarding session does not inherit another store's saved logo");
        void Draw(int step){vm.Step=step;typeof(OnboardingWindow).GetMethod("Draw",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);Pump();}
        void ClickNext(){((Button)window.FindName("NextButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();}
        BusinessForm Form()=>(BusinessForm)((ContentControl)window.FindName("Page")).Content;
        vm.Profile=new(){Registered=false};Draw(3);
        var radios=Descendants(window).OfType<RadioButton>().ToArray();
        radios[1].IsChecked=true;Pump();Assert(radios.Count(r=>r.IsChecked==true)==1&&vm.Profile.Registered,"Registered choice is exclusive and updates saved value");
        radios[0].IsChecked=true;Pump();Assert(radios.Count(r=>r.IsChecked==true)==1&&!vm.Profile.Registered,"Unregistered choice deselects registered");
        vm.Profile=new(){Registered=false,SalesTaxRegistered=true,Strn="1234567890123"};Draw(3);ClickNext();
        Assert(!((Button)window.FindName("NextButton")).IsEnabled,"Changing registration never silently discards declared tax obligations");
        vm.Profile=new(){Registered=true};Draw(4);ClickNext();
        Assert(vm.Step==4&&!((Button)window.FindName("NextButton")).IsEnabled,"Empty required fields stay on their own page and disable Next");
        Assert(Descendants(Form()).OfType<TextBlock>().Count(t=>t.IsVisible&&t.Text=="This field is required.")>=3,"Required messages appear beside every missing field");
        var form=Form();((TextBox)form.Fields["StoreName"]).Text="Test store";((TextBox)form.Fields["Address"]).Text="Test road";((TextBox)form.Fields["City"]).Text="Lahore";
        ((TextBox)form.Fields["Email"]).Text="bad-email";Pump();Assert(!((Button)window.FindName("NextButton")).IsEnabled,"Invalid optional email also prevents continuing");
        ((TextBox)form.Fields["Email"]).Text="owner@example.com";Pump();Assert(((Button)window.FindName("NextButton")).IsEnabled,"Correcting fields re-enables Next immediately");
        Assert(!Descendants(Form()).OfType<Image>().Any(),"No inherited logo appears in sample invoice");
        Assert(Descendants(Form()).OfType<TextBlock>().Any(t=>t.Text=="Your")&&Descendants(Form()).OfType<TextBlock>().Any(t=>t.Text=="PKR 1,000.00"),"Logo placeholder and complete sample receipt render");
        Capture(window,Path.Combine(screenshots,"onboarding-preview-validation.png"));
        Draw(5);ClickNext();Assert(vm.Step==5&&!((Button)window.FindName("NextButton")).IsEnabled,"Missing legal name is caught on tax identity page");
        ((TextBox)Form().Fields["LegalName"]).Text="Test legal";((TextBox)Form().Fields["Ntn"]).Text="123";Pump();
        Assert(!((Button)window.FindName("NextButton")).IsEnabled,"Short NTN cannot advance");
        ((TextBox)Form().Fields["Ntn"]).Text="1234567";((CheckBox)Form().Fields["SalesTaxRegistered"]).IsChecked=true;Pump();
        Assert(!((Button)window.FindName("NextButton")).IsEnabled,"Sales-tax selection requires STRN immediately");
        ((TextBox)Form().Fields["Strn"]).Text="1234567890123";Pump();Assert(((Button)window.FindName("NextButton")).IsEnabled,"Correct dummy tax identifiers accepted locally");
        Draw(1);((TextBox)Form().Fields["LicenseKey"]).Text="dummy";ClickNext();Assert(vm.Step==1&&!((Button)window.FindName("NextButton")).IsEnabled,"Dummy license is never accepted as a valid key");
        ((TextBox)Form().Fields["LicenseKey"]).Text="";Pump();Assert(((Button)window.FindName("NextButton")).IsEnabled,"Development build may continue honestly without activation");
        window.WindowState=WindowState.Maximized;Pump();Assert(window.ActualWidth>=SystemParameters.WorkArea.Width-4&&window.ActualHeight>=SystemParameters.WorkArea.Height-4,"Setup maximizes to the entire desktop work area");
        CheckWindow(window,"onboarding-maximized");window.Close();Pump();
    }
    private static async Task InvoiceActionChecks(IServiceProvider sp,PosStorage storage,string root,string screens)
    {
        var db=sp.GetRequiredService<PosDbContext>();var cat=new SupermarketPos.Domain.Entities.Category("Invoice test");db.Categories.Add(cat);await db.SaveChangesAsync();
        var product=new SupermarketPos.Domain.Entities.Product("INVOICE-TEST","Saved invoice test",2450,0,SupermarketPos.Domain.Common.Unit.Piece,2,cat.Id);db.Products.Add(product);await db.SaveChangesAsync();
        var cart=new SupermarketPos.Domain.Pricing.Cart();cart.AddOrIncrement(product,1);
        var sale=await sp.GetRequiredService<ISaleService>().CheckoutAsync(cart,new SupermarketPos.Domain.Pricing.CashPayment(2450,2450),sp.GetRequiredService<IAuthService>().CurrentUser!,customer:new("Test customer","03001234567",null,null));
        var preview=await sp.GetRequiredService<IReceiptService>().RenderPreviewAsync(sale);var before=await db.Sales.CountAsync();
        var customer=new CustomerFieldsViewModel();customer.SetExisting(await sp.GetRequiredService<ISaleRepository>().GetCustomerSalesAsync());customer.SelectedCustomer=customer.ExistingCustomers.First();Assert(customer.IncludeDetails&&customer.Build()?.Phone==sale.CustomerPhone,"Customer dropdown fills the saved customer details for Udhar checkout");customer.Load(null);Assert(customer.SelectedCustomer is null,"A new sale clears the previous customer selection");
        var info=new InvoiceActionInfo(sale.ReceiptNo,sale.CreatedAt,sale.GrossTotal,"Test shop",sale.CustomerName,sale.CustomerPhone,"en","80","TEST-QUEUE","","");
        var actions=new SavedInvoiceActions(preview,info,storage);var path=await actions.EnsurePdfAsync();File.SetLastWriteTimeUtc(path,new DateTime(2000,1,1));await actions.EnsurePdfAsync();Assert(File.GetLastWriteTimeUtc(path).Year==2000,"An existing verified invoice PDF is reused");
        var download=Path.Combine(root,"DownloadedInvoice.pdf");await actions.DownloadAsync(download);Assert(File.ReadAllBytes(download).SequenceEqual(preview.Pdf),"Download copies the same saved invoice PDF");
        await File.AppendAllTextAsync(path,"changed");await actions.EnsurePdfAsync();Assert(File.ReadAllBytes(path).SequenceEqual(preview.Pdf),"A damaged cached PDF is repaired from the saved invoice preview");
        Assert(actions.Message().Contains("2,450.00"),"WhatsApp text uses the saved invoice amount");
        foreach(var lang in new[]{"en","ur"})foreach(var size in new[]{100,200})
        {
            LocalizationManager.Instance.SetLanguage(lang);sp.GetRequiredService<DesktopTextSize>().Save(size);var printer=new FakePrinter();var handoff=new FakeHandoff();
            var receipt=new ReceiptPreviewWindow(preview,sale.ReceiptNo,storage,info,printer:printer,whatsApp:new(handoff,()=>true));var behind=new Button{Content="Underlying screen"};var host=new Grid();host.Children.Add(behind);var window=new Window{Width=940,Height=900,Content=host};window.Show();var overlay=InAppDialog.ShowAsync(receipt,close=>receipt.RequestClose=close,window);Pump();
            var closeButton=(Button)receipt.FindName("CloseButton");Assert(Equals(closeButton.Content,LocalizationManager.Instance["Common_Close"])&&closeButton.IsVisible,"Every receipt has an explicit localized Close button");Assert(closeButton.TransformToAncestor(window).Transform(new Point()).Y<180,"Receipt Close stays in the header at 100% and 200%");
            var primary=Descendants(receipt).OfType<Button>().Where(b=>ReferenceEquals(b.Style,receipt.FindResource("PrimaryButton"))).ToArray();Assert(primary.Select(b=>b.Name).SequenceEqual(new[]{"PrintButton","WhatsAppButton","SaveButton"}),"Exactly Print Receipt, WhatsApp, Download appear in that order");
            ((Button)receipt.FindName("PrintButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Assert(printer.Jobs==1&&printer.Name=="TEST-QUEUE","Print uses the configured printer and saved receipt pages");
            printer.Fail=true;((Button)receipt.FindName("PrintButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Assert(((Button)receipt.FindName("RetryPrintButton")).IsVisible,"Unavailable printer exposes retry without rerunning checkout");
            var share=(Task)typeof(ReceiptPreviewWindow).GetMethod("ShareAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(receipt,[false])!;await share;Assert(handoff.Opened.Count==1&&handoff.Opened[0].Scheme=="whatsapp"&&handoff.Opened[0].Query.Contains("923001234567"),"Desktop handoff targets saved customer once");
            CheckWindow(window,$"invoice-actions-{lang}-{size}");Capture(window,Path.Combine(screens,$"invoice-actions-{lang}-{size}.png"));closeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await overlay;Assert(behind.IsEnabled&&host.Children.Count==1,"Close button dismisses the popup and restores the existing screen");
            overlay=InAppDialog.ShowAsync(receipt,close=>receipt.RequestClose=close,window);Pump();var layer=host.Children.OfType<Grid>().Single();
            ((Image)receipt.FindName("PagePreview")).RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,System.Windows.Input.MouseButton.Left){RoutedEvent=System.Windows.Input.Mouse.MouseDownEvent});Pump();Assert(!overlay.IsCompleted&&behind.IsEnabled&&!behind.IsHitTestVisible,"Clicks inside the receipt do not dismiss it or reach the screen underneath");
            var outside=new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,System.Windows.Input.MouseButton.Left){RoutedEvent=System.Windows.Input.Mouse.MouseDownEvent};layer.RaiseEvent(outside);await overlay;Assert(outside.Handled&&behind.IsEnabled&&host.Children.Count==1,"Clicking the backdrop dismisses the popup without passing the click through");
            overlay=InAppDialog.ShowAsync(receipt,close=>receipt.RequestClose=close,window);Pump();layer=host.Children.OfType<Grid>().Single();layer.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),Environment.TickCount,System.Windows.Input.Key.Escape){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent});await overlay;Assert(behind.IsEnabled&&host.Children.Count==1,"Escape dismisses the shared popup");window.Close();Pump();
        }
        var missing=new FakeHandoff{Desktop=false};Assert(new WhatsAppShareService(missing,()=>true).Open("03001234567","Invoice 2,450") =="Invoice_BrowserOpened"&&missing.Opened.Single().Host=="web.whatsapp.com","Missing desktop handler opens only browser fallback");
        var failed=new FakeHandoff{FailDesktop=true};new WhatsAppShareService(failed,()=>true).Open("03001234567","Test");Assert(failed.Opened.Count==2&&failed.Opened[1].Scheme=="https","Failed desktop handoff falls back to browser");
        try{new WhatsAppShareService(new FakeHandoff(),()=>false).Open("03001234567","Test");throw new Exception("Offline handoff accepted");}catch(SupermarketPos.Domain.Common.DomainException){Assert(true,"Offline share preserves the invoice and reports unavailable");}
        Assert(await db.Sales.CountAsync()==before&&product.StockQuantity==1,"Repeated print/share/download never creates another sale or deducts stock");
        var missingNumber=new ReceiptPreviewWindow(preview,sale.ReceiptNo,storage,info with{Phone=null},whatsApp:new(new FakeHandoff(),()=>true));var owner=new Window{Width=940,Height=900,Content=new Grid()};owner.Show();var modal=InAppDialog.ShowAsync(missingNumber,close=>missingNumber.RequestClose=close,owner);Pump();var windows=System.Windows.Application.Current.Windows.Count;
        var waiting=(Task)typeof(ReceiptPreviewWindow).GetMethod("ShareAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(missingNumber,[false])!;Pump();
        Assert(((StackPanel)missingNumber.FindName("PromptPanel")).IsVisible&&System.Windows.Application.Current.Windows.Count==windows,"Missing WhatsApp recipient opens one inline input without a new window");missingNumber.CancelPrompt();await waiting;missingNumber.RequestClose!();await modal;owner.Close();Pump();
    }
    private sealed class FakePrinter:IReceiptPrinter
    {
        public int Jobs;public string Name="";public bool Fail;
        public Task<string[]> PrintersAsync()=>Task.FromResult(new[]{"TEST-QUEUE"});
        public Task PrintAsync(IReadOnlyList<byte[]> pages,string printer,string paper,string jobName){Jobs++;Name=printer;if(Fail)throw new SupermarketPos.Domain.Common.DomainException("Invoice_PrinterUnavailable");return Task.CompletedTask;}
    }
    private sealed class FakeHandoff:IInvoiceHandoff
    {
        public bool Desktop=true,FailDesktop;public List<Uri> Opened=[];public bool HasDesktopHandler()=>Desktop;
        public bool Open(Uri uri){Opened.Add(uri);return !(FailDesktop&&uri.Scheme=="whatsapp");}
    }
    private static async Task RetailScreenChecks(IServiceProvider sp,string screens)
    {
        var setup=sp.GetRequiredService<IBusinessSetup>();var state=await setup.LoadAsync();state.Profile.EnableLooseGoods=true;state.Profile.EnablePackagedUnits=true;state.Profile.EnableDigitalWalletServices=true;state.Profile.EnableMobileRecharge=true;await setup.SaveDraftAsync(state.Profile,8);
        var ledger=sp.GetRequiredService<SupermarketPos.Infrastructure.Business.DigitalServiceLedger>();await ledger.SaveProviderAsync(new(){Name="UI test provider",OpeningFloat=5000});
        var inventory=sp.GetRequiredService<ProductsViewModel>();await inventory.RefreshAsync();
        var db=sp.GetRequiredService<PosDbContext>();var category=await db.Categories.FirstAsync();var sugar=new SupermarketPos.Domain.Entities.Product("","Sugar",160,0,SupermarketPos.Domain.Common.Unit.Kilogram,50,category.Id);
        foreach(var lang in new[]{"en","ur"})foreach(var size in new[]{100,200})
        {
            LocalizationManager.Instance.SetLanguage(lang);sp.GetRequiredService<DesktopTextSize>().Save(size);
            inventory.Editor.LoadNew(category.Id,0);inventory.Editor.SellingMethod="pack";inventory.Editor.UnitsPerPackage="100";
            var editor=new Window{Width=1100,Height=850,DataContext=inventory,Content=new Viewbox{Child=new Border{Width=1020,Child=new ProductEditorView()},StretchDirection=StretchDirection.DownOnly},FlowDirection=LocalizationManager.Instance.FlowDirection};editor.Show();Pump();CheckWindow(editor,$"pack-editor-{lang}-{size}");Capture(editor,Path.Combine(screens,$"pack-editor-{lang}-{size}.png"));editor.Close();Pump();
            var measure=new MeasuredSaleWindow(new[]{sugar}){FlowDirection=LocalizationManager.Instance.FlowDirection};measure.Show();Pump();CheckWindow(measure,$"measured-sale-{lang}-{size}");Capture(measure,Path.Combine(screens,$"measured-sale-{lang}-{size}.png"));measure.Close();Pump();
            var digital=new DigitalServicesView(ledger,setup,sp.GetRequiredService<ReceiptPreviewDialog>());var window=new Window{Width=1240,Height=900,Content=digital,FlowDirection=LocalizationManager.Instance.FlowDirection};window.Show();Pump();
            await (Task)typeof(DigitalServicesView).GetMethod("LoadAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(digital,null)!;
            foreach(var page in new[]{"Record","History","Providers","Packages","Reports"})
            {
                await (Task)typeof(DigitalServicesView).GetMethod("ShowAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(digital,[page])!;Pump();
                Assert(!Descendants(digital).OfType<TextBlock>().Any(t=>t.IsVisible&&(t.Text.StartsWith("Service_")||t.Text.StartsWith("Measure_")||t.Text.StartsWith("Invoice_"))),$"Digital {page} resolves localized text");
                Capture(window,Path.Combine(screens,$"digital-{page}-{lang}-{size}.png"));
            }
            window.Close();Pump();
        }
        LocalizationManager.Instance.SetLanguage("en");sp.GetRequiredService<DesktopTextSize>().Save(100);var form=new BusinessForm();form.Text("Setup_Ntn",new SupermarketPos.Domain.Business.BusinessProfile(),"Ntn");var host=new Window{Width=650,Height=300,Content=form};host.Show();Pump();
        var info=Descendants(form).OfType<Border>().Single(b=>b.ToolTip is ToolTip);Assert(!Descendants(form).OfType<Button>().Any(b=>b.ToolTip is ToolTip),"Help is a non-clickable hover target");
        Assert(((ToolTip)info.ToolTip).StaysOpen&&ToolTipService.GetInitialShowDelay(info)==150,"Native tooltip service owns opening and dismissal, without mouse capture");host.Close();Pump();
        await RegisterCorrectionChecks(sp,screens);

    }
    private static bool HasScrollAncestor(DependencyObject item){for(var p=VisualTreeHelper.GetParent(item);p is not null;p=VisualTreeHelper.GetParent(p))if(p is ScrollViewer)return true;return false;}
    private static async Task RegisterCorrectionChecks(IServiceProvider sp,string screens)
    {
        var db=sp.GetRequiredService<PosDbContext>();var cat=await db.Categories.FirstAsync();
        var sugar=new SupermarketPos.Domain.Entities.Product("LOOSE-TEST","Test sugar",160,0,SupermarketPos.Domain.Common.Unit.Kilogram,50,cat.Id);
        var piece=new SupermarketPos.Domain.Entities.Product("PIECE-TEST","Test candy",10,0,SupermarketPos.Domain.Common.Unit.Piece,100,cat.Id);
        db.Products.AddRange(sugar,piece);await db.SaveChangesAsync();
        var committed=new List<(decimal Quantity,bool Package)>();var goods=new LooseGoodsPanel([sugar,piece],true,true,(p,q,pack)=>{committed.Add((q,pack));return Task.FromResult(true);});
        var window=new Window{Width=1100,Height=800,Content=goods};window.Show();Pump();
        var selectors=Descendants(goods).OfType<ComboBox>().ToArray();selectors[0].SelectedItem=sugar;Pump();selectors[1].SelectedValue="amount";
        var value=(TextBox)typeof(LooseGoodsPanel).GetField("_value",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(goods)!;value.Text="100";
        await (Task)typeof(LooseGoodsPanel).GetMethod("AddAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(goods,null)!;
        Assert(committed.Single().Quantity==.625m,"Rs 100 sugar at Rs 160/kg adds exactly 625 grams through inline flow");
        selectors[0].SelectedItem=piece;Pump();Assert(selectors[1].Items.Count==1,"Piece product never exposes weight or amount modes");value.Text="2";
        await (Task)typeof(LooseGoodsPanel).GetMethod("AddAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(goods,null)!;Assert(committed.Last().Quantity==2,"Piece flow adds exact count");
        Capture(window,Path.Combine(screens,"inline-loose-goods.png"));window.Close();Pump();
        var source=new[]{new CustomerChoice(new("Alice","03001234567",null,null)),new CustomerChoice(new("Bob","03007654321",null,null))};
        var first=new ComboBox{ItemsSource=source,DisplayMemberPath="Label",IsEditable=true};var second=new ComboBox{ItemsSource=source,DisplayMemberPath="Label",IsEditable=true};
        var root=new StackPanel();root.Children.Add(first);root.Children.Add(second);var comboWindow=new Window{Content=root,Width=700,Height=300};comboWindow.Show();Pump();first.IsDropDownOpen=true;Pump();
        var editable=(TextBox)first.Template.FindName("PART_EditableTextBox",first);comboWindow.Activate();System.Windows.Input.Keyboard.Focus(editable);editable.Text="Ali";Pump();
        Assert(first.Items.Count==1&&first.Items.Cast<CustomerChoice>().Single().Customer.Name=="Alice",$"Typing filters the editable dropdown (items={first.Items.Count}, focus={editable.IsKeyboardFocusWithin}, selected={first.SelectedItem}, source={first.ItemsSource.GetType().Name}, text={editable.Text})");
        Assert(second.Items.Count==2,"Filtering one dropdown preserves another field's options");first.IsDropDownOpen=false;Pump();Assert(first.Items.Count==2,"Closing restores the complete list");
        var optionalCustomer=new CustomerFieldsViewModel();first.SetBinding(ComboBox.SelectedItemProperty,new Binding(nameof(optionalCustomer.SelectedCustomer)){Source=optionalCustomer,Mode=BindingMode.TwoWay});first.SelectedItem=source[0];optionalCustomer.IncludeDetails=false;
        comboWindow.Content=null;Pump();Assert(!optionalCustomer.IncludeDetails,"Unloading a searched customer field never re-enables optional customer details");comboWindow.Content=root;Pump();first.SelectedItem=null;first.IsDropDownOpen=true;Pump();System.Windows.Input.Keyboard.Focus(editable);editable.Text="Bob";Pump();Assert(first.Items.Count==1&&first.Items.Cast<CustomerChoice>().Single().Customer.Name=="Bob","Search still filters after returning to the same control");comboWindow.Close();Pump();
        var main=sp.GetRequiredService<MainViewModel>();await main.ShowPosCommand.ExecuteAsync(null);
        var pos=(PosViewModel)main.CurrentViewModel;
        Assert(!pos.CatalogKinds.Any(k=>k.Code=="digital"),"Physical register exposes only physical product kinds");
        await main.ShowDigitalServicesCommand.ExecuteAsync(null);Pump();
        Assert(main.CurrentViewModel is DigitalServicesView&&main.ActiveScreen=="digital","Digital sales has its own destination");
        await main.ShowPosCommand.ExecuteAsync(null);
    }
    private static async Task PharmacyScreenChecks(IServiceProvider sp,string screens)
    {
        var setup=sp.GetRequiredService<IBusinessSetup>();var state=await setup.LoadAsync();state.Profile.BusinessType="pharmacy";state.Profile.MedicineTemplate="pakistan-drap";await setup.SaveDraftAsync(state.Profile,state.Step);
        var catalog=sp.GetRequiredService<SupermarketPos.Infrastructure.Business.MedicineCatalogService>();await catalog.EnsureSelectedAsync();Assert((await catalog.StatusAsync())?.RowCount==10068,"Supplied catalog imports in an isolated real file database");
        Console.WriteLine("PHARMACY catalog ready");
        var medicine=(await catalog.SearchAsync("moxiflox",1)).Single();Assert(medicine.Price is null,"Catalog never invents a price");
        var main=sp.GetRequiredService<MainViewModel>();var scanner=sp.GetRequiredService<ScannerController>();
        foreach(var lang in new[]{"en","ur"})foreach(var size in new[]{100,200})
        {
            LocalizationManager.Instance.SetLanguage(lang);sp.GetRequiredService<DesktopTextSize>().Save(size);await main.ShowPosCommand.ExecuteAsync(null);
            var pos=(PosViewModel)typeof(MainViewModel).GetField("_pos",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(main)!;var view=new PosView{DataContext=pos};var host=new Window{DataContext=main,Width=1600,Height=1000,Content=new Grid{Children={view}},FlowDirection=LocalizationManager.Instance.FlowDirection};host.SetResourceReference(Control.FontSizeProperty,"TextSize14");host.SetResourceReference(Control.FontFamilyProperty,"UiFont");System.Windows.Application.Current.MainWindow=host;host.Show();Pump();
            Assert(pos.IsPharmacy&&!pos.EnableDigitalServices&&!pos.EnableLooseGoods,"Pharmacy cannot expose grocery or telecom modules");Assert(((ScrollViewer)view.FindName("CategoryStrip")).IsVisible&&pos.ShowRegisterCards,"Pharmacy restores original categories and cards");Assert(pos.PharmacyWorkspace is not null,"Pharmacy inline workspace is present");
            await pos.SelectMedicineCommand.ExecuteAsync(medicine);Assert(pos.Lines.Single().Line.Product.Id==0&&!pos.PharmacyWorkspace!.HasPendingEntry,"Catalog selection enters the cart without a product form");Pump();var cart=(DataGrid)view.FindName("CartTable");Assert(cart.IsVisible&&cart.ActualHeight>25,"Cart stays visible during medicine entry");Assert(((ScrollViewer)view.FindName("ProductResults")).ActualHeight>100,"Cards retain a usable viewport beside the cart at every tested language and scale");CheckWindow(host,$"pharmacy-register-{lang}-{size}");Capture(host,Path.Combine(screens,$"pharmacy-register-{lang}-{size}.png"));host.Close();Pump();foreach(var line in pos.Lines.ToArray())pos.RemoveLineCommand.Execute(line);
            var inventory=sp.GetRequiredService<ProductsViewModel>();await inventory.RefreshAsync();Assert(!inventory.ShowProductImages&&inventory.Editor.IsPharmacy,"Pharmacy product forms exclude images and electronics");inventory.Editor.LoadNew(inventory.Editor.Categories.First().Id,0);
            var editor=new Window{Width=1200,Height=900,DataContext=inventory,FlowDirection=LocalizationManager.Instance.FlowDirection,Content=new Viewbox{Child=new Border{Width=1100,Child=new ProductEditorView()},StretchDirection=StretchDirection.DownOnly}};editor.SetResourceReference(Control.FontFamilyProperty,"UiFont");editor.SetResourceReference(Control.FontSizeProperty,"TextSize14");editor.Show();Pump();CheckWindow(editor,$"pharmacy-editor-{lang}-{size}");Capture(editor,Path.Combine(screens,$"pharmacy-editor-{lang}-{size}.png"));editor.Close();Pump();
        }
        await PharmacyInteractionChecks(sp,main,scanner,screens);
        await RestorationInteractionChecks(sp,screens);
        state.Profile.BusinessType="other";state.Profile.MedicineTemplate="none";await setup.SaveDraftAsync(state.Profile,state.Step);
    }
    private static async Task PharmacyInteractionChecks(IServiceProvider sp,MainViewModel main,ScannerController scanner,string screens)
    {
        LocalizationManager.Instance.SetLanguage("en");sp.GetRequiredService<DesktopTextSize>().Save(100);
        var pharmacy=sp.GetRequiredService<SupermarketPos.Infrastructure.Business.PharmacyInventory>();
        var medicine=await pharmacy.QuickAddAsync(new(null,"QA pharmacy bottle","QA-PHARM-1","bottle",80,0,true));
        medicine.SetMedicineDetails("QA ingredient","10 mg","Bottle","QA company");
        var locations=sp.GetRequiredService<SupermarketPos.Infrastructure.Business.StockLocationService>();var rack=await locations.SaveLocationAsync("QA rack");var shelf=await locations.SaveLocationAsync("QA shelf",rack.Id);medicine=await sp.GetRequiredService<PosDbContext>().Products.SingleAsync(p=>p.Id==medicine.Id);medicine.SetMedicineDetails("QA ingredient","10 mg","Bottle","QA company");medicine.SetDefaultLocation(shelf.Id);await sp.GetRequiredService<PosDbContext>().SaveChangesAsync();
        await main.ShowPosCommand.ExecuteAsync(null);var pos=(PosViewModel)typeof(MainViewModel).GetField("_pos",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(main)!;pos.PharmacyWorkspace!.CancelEntry();
        var view=new PosView{DataContext=pos};var root=new Grid{Children={view}};var host=new Window{DataContext=main,Width=1600,Height=1000,Content=root};host.SetResourceReference(Control.FontFamilyProperty,"UiFont");host.SetResourceReference(Control.FontSizeProperty,"TextSize14");System.Windows.Application.Current.MainWindow=host;host.Show();Pump();
        Assert((await scanner.DispatchPhysicalAsync("QA-PHARM-1")).Outcome=="added","Physical scan works without pairing a phone");Assert((await scanner.DispatchPhysicalAsync("QA-PHARM-1")).Outcome=="added"&&pos.Lines.Single().Line.Quantity==2,"Second scan adds the same real product");
        pos.OpenCashCommand.Execute(null);Assert(pos.IsPaymentOpen&&pos.IsCash,"Cash shortcut opens existing checkout");pos.OpenCreditCommand.Execute(null);Assert(pos.IsCash&&!pos.IsCredit,"Second payment shortcut cannot replace active checkout");Assert((await scanner.DispatchPhysicalAsync("QA-PHARM-1")).Outcome=="paused","Checkout pauses background scans");pos.CancelPaymentCommand.Execute(null);
        pos.OpenCardCommand.Execute(null);Assert(pos.IsPaymentOpen&&!pos.IsCash&&!pos.IsCredit,"Card shortcut opens card checkout");pos.CancelPaymentCommand.Execute(null);pos.OpenCreditCommand.Execute(null);Assert(pos.IsCredit&&pos.CreditPaidNow=="0","Udhar starts with zero collected cash");pos.CancelPaymentCommand.Execute(null);
        var clear=pos.ClearCartCommand.ExecuteAsync(null);Pump();Assert(InAppDialog.IsOpen&&pos.Lines.Count==1,"F4 waits for confirmation");var cancel=Descendants(root).OfType<Button>().Single(b=>b.IsEnabled&&b.IsVisible&&Equals(b.Content,LocalizationManager.Instance["Common_Cancel"]));cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await clear;Assert(pos.Lines.Single().Line.Quantity==2,"Cancelling F4 preserves cart");
        clear=pos.ClearCartCommand.ExecuteAsync(null);Pump();var confirm=Descendants(root).OfType<Button>().Single(b=>b.IsEnabled&&b.IsVisible&&Equals(b.Content,LocalizationManager.Instance["Pos_Clear"]));confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await clear;Assert(pos.Lines.Count==0&&!pos.OpenCashCommand.CanExecute(null),"Confirmed clear empties cart and blocks empty checkout");

        await main.ShowProductsCommand.ExecuteAsync(null);var inventory=(ProductsViewModel)main.CurrentViewModel;await inventory.BeginScannerEntryAsync(null);var editor=new ProductEditorView{DataContext=inventory};Action close=()=>{};var dialog=InAppDialog.ShowAsync(editor,c=>close=c,contentWidth:1100);Pump();var field=(TextBox)editor.FindName("MedicineBarcodeField");field.Focus();Pump();
        Assert((await scanner.DispatchPhysicalAsync("QA-MODAL-CODE")).Outcome=="captured"&&inventory.Editor.Barcode=="QA-MODAL-CODE","Focused product barcode overrides page and modal scan pause");inventory.Editor.SellingMethod="pack";Pump();var packField=(TextBox)editor.FindName("MedicinePackBarcodeField");packField.Focus();Pump();Assert((await scanner.DispatchPhysicalAsync("QA-PACK-CODE")).Outcome=="captured"&&inventory.Editor.PackageBarcode=="QA-PACK-CODE"&&inventory.Editor.Barcode=="QA-MODAL-CODE","Focused outer-pack barcode updates only that field");Assert(pos.Lines.Count==0,"Focused product scan never adds to cart");((MedicineSearchBox)inventory.MedicineNameSearch!).Input.Focus();Pump();Assert((await scanner.DispatchPhysicalAsync("QA-PHARM-1")).Outcome=="paused","Other modal fields keep background scanning paused");close();await dialog;inventory.CancelEditCommand.Execute(null);
        var searchBox=new MedicineSearchBox(sp.GetRequiredService<SupermarketPos.Infrastructure.Business.MedicineCatalogService>());var selection=new TaskCompletionSource<SupermarketPos.Domain.Entities.MedicineMatch>();var barcodeValue="";searchBox.Selected+=m=>selection.TrySetResult(m);searchBox.BarcodeSubmitted+=s=>barcodeValue=s;root.Children.Add(searchBox);searchBox.Input.Focus();searchBox.Input.Text="Moxifloxacin";searchBox.Input.Text="QA pharmacy";searchBox.Input.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(host),0,System.Windows.Input.Key.Enter){RoutedEvent=UIElement.PreviewKeyDownEvent});Assert((await selection.Task.WaitAsync(TimeSpan.FromSeconds(5))).ProductId==medicine.Id,"Fast Enter selects current query rather than stale medicine results");searchBox.Input.Text="0400000000695";searchBox.Input.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(host),0,System.Windows.Input.Key.Enter){RoutedEvent=UIElement.PreviewKeyDownEvent});Assert(barcodeValue=="0400000000695","Numeric barcode Enter retains scan routing");root.Children.Remove(searchBox);
        var noBarcode=await pharmacy.QuickAddAsync(new(null,"QA barcode optional","","bottle",20,0,true));inventory.Editor.Load(noBarcode);Assert(inventory.Editor.Barcode==""&&inventory.Editor.BarcodeForSave==noBarcode.Barcode&&new ProductRowViewModel(noBarcode,5).Barcode=="","Optional barcode stays blank while its private identifier remains stable");
        await pharmacy.VerifyStockAsync(medicine.Id,20,"QA physical count");medicine=await sp.GetRequiredService<PosDbContext>().Products.SingleAsync(p=>p.Id==medicine.Id);medicine.SetPurchaseCost(40);await sp.GetRequiredService<PosDbContext>().SaveChangesAsync();
        await main.ShowProductsCommand.ExecuteAsync(null);inventory.ClearPharmacyFiltersCommand.Execute(null);await inventory.RefreshAsync();inventory.ReadyOnly=true;inventory.ManufacturerFilter="QA company";inventory.FormFilter="Bottle";inventory.BarcodeState="has";Assert(inventory.VisibleProducts.Cast<ProductRowViewModel>().Select(r=>r.Id).SequenceEqual(new[]{medicine.Id}),"Combined filters: "+string.Join(" | ",inventory.Products.Where(r=>r.Id==medicine.Id).Select(r=>$"{r.SetupDetails}; company={r.Manufacturer}; form={r.Formulation}; filter={inventory.Filter}; low={inventory.LowStockOnly}")));
        Assert((await scanner.DispatchPhysicalAsync(shelf.Barcode)).Outcome=="location_selected","Rack scan selects inventory filter");Assert(inventory.SelectedLocationFilter==shelf.Id&&inventory.ManufacturerFilter=="QA company"&&inventory.VisibleProducts.Cast<ProductRowViewModel>().Single().Id==medicine.Id,"Location scanning preserves other filters: selected="+inventory.SelectedLocationFilter+" expected="+shelf.Id+" manufacturer="+inventory.ManufacturerFilter+" visible="+string.Join(",",inventory.VisibleProducts.Cast<ProductRowViewModel>().Select(r=>r.Id)));
        inventory.BarcodeState="missing";Assert(inventory.VisibleProducts.IsEmpty,"Contradictory barcode filter returns no results");inventory.ClearPharmacyFiltersCommand.Execute(null);Assert(inventory.SelectedLocationFilter==0&&inventory.StockState=="all"&&inventory.ManufacturerFilter==""&&!inventory.ReadyOnly,"Clear resets every pharmacy filter");
        foreach(var size in new[]{100,200})
        {
            sp.GetRequiredService<DesktopTextSize>().Save(size);root.Children.Clear();root.Children.Add(new ProductsView{DataContext=inventory});Pump();CheckWindow(host,"pharmacy-inventory-"+size);var table=Descendants(root).OfType<DataGrid>().First(g=>g.IsVisible);Assert(table.ActualHeight>90,"Inventory filters leave a usable table at "+size+" percent");Capture(host,Path.Combine(screens,"pharmacy-inventory-"+size+".png"));
        }
        await main.ShowPurchasesCommand.ExecuteAsync(null);var purchases=(PurchasesViewModel)main.CurrentViewModel;root.Children.Clear();root.Children.Add(new PurchasesView{DataContext=purchases});Pump();if(purchases.Suppliers.Count==0){await sp.GetRequiredService<IPurchaseService>().AddSupplierAsync("QA supplier");await purchases.RefreshAsync();}purchases.SelectedSupplier=purchases.Suppliers.First();var before=purchases.Draft.Count;Assert((await scanner.DispatchPhysicalAsync(shelf.Barcode)).Outcome=="location_selected"&&purchases.DestinationLocationId==shelf.Id&&purchases.Draft.Count==before,"Receiving rack scan selects destination without adding or moving stock");
        host.Close();Pump();
    }
    private static void CheckWindow(Window window,string name)
    {
        window.UpdateLayout();var bounds=new Rect(0,0,window.ActualWidth,window.ActualHeight);
        Assert(bounds.Width>100&&bounds.Height>100,name+" rendered");
        foreach(var b in Descendants(window).OfType<Button>().Where(b=>b.IsVisible&&b.ActualWidth>0&&b.ActualHeight>0&&!HasScrollAncestor(b)))
        {
            var r=b.TransformToAncestor(window).TransformBounds(new Rect(0,0,b.ActualWidth,b.ActualHeight));
            Assert(r.Left>=-2&&r.Top>=-2&&r.Right<=bounds.Right+2&&r.Bottom<=bounds.Bottom+2,name+" actions stay within window");
        }
        Assert(!Descendants(window).OfType<TextBlock>().Any(t=>t.IsVisible&&(t.Text.StartsWith("Setup_")||t.Text.StartsWith("License_")||t.Text.StartsWith("Tax_")||t.Text.StartsWith("Pharmacy_"))),name+" resource text resolves");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var c in Descendants(child))yield return c;}}
    private static void Capture(Window window,string file)
    {
        var image=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(window);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var output=File.Create(file);png.Save(output);
    }
    private static void Assert(bool valid,string message){if(!valid)throw new Exception(message);_checks++;}
    private static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=>frame.Continue=false);Dispatcher.PushFrame(frame);}
    private static void Wait(Task task, TimeSpan? timeout=null){var until=DateTime.UtcNow+(timeout??TimeSpan.FromSeconds(60));while(!task.IsCompleted){if(DateTime.UtcNow>until)throw new TimeoutException();Pump();Thread.Sleep(5);}task.GetAwaiter().GetResult();}
    private static T Wait<T>(Task<T> task){Wait((Task)task);return task.GetAwaiter().GetResult();}
}
