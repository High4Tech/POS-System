using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Infrastructure.Commercial;
using SupermarketPos.Infrastructure.Persistence;
using SupermarketPos.Wpf;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Wpf.ViewModels;
using SupermarketPos.Wpf.Views;

internal static partial class Program
{
    private static async Task ShortcutReferenceChecks(IServiceProvider sp, string screenshots)
    {
        var text = LocalizationManager.Instance;
        text.SetLanguage("en");
        var scale = sp.GetRequiredService<DesktopTextSize>();
        scale.Save(100);
        var shell = sp.GetRequiredService<MainViewModel>();
        shell.IsLoggedIn = true;
        var window = new MainWindow(sp.GetRequiredService<PosStorage>()) { DataContext = shell, Width = 1366, Height = 768 };
        System.Windows.Application.Current.MainWindow = window;
        window.Show();
        Pump();
        var content = Descendants(window).OfType<ContentControl>().Single(c =>
            c.GetBindingExpression(ContentControl.ContentProperty)?.ParentBinding.Path.Path == nameof(MainViewModel.CurrentViewModel));
        var more = (Button)window.FindName("MoreButton");
        var open = typeof(MainWindow).GetMethod("ShowShortcutsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var nav = new (string Name, Func<Task> Open, Func<UserControl> View, string Key)[]
        {
            ("Nav_Pos", () => shell.ShowPosCommand.ExecuteAsync(null), () => new PosView(), "F1"),
            ("Nav_Customers", () => shell.ShowCustomersCommand.ExecuteAsync(null), () => new CustomersView(), "Alt + F1"),
            ("Nav_Products", () => shell.ShowProductsCommand.ExecuteAsync(null), () => new ProductsView(), "Ctrl + H"),
            ("Nav_Purchases", () => shell.ShowPurchasesCommand.ExecuteAsync(null), () => new PurchasesView(), "Ctrl + G"),
            ("Nav_Reports", () => shell.ShowReportsCommand.ExecuteAsync(null), () => new ReportsView(), "Ctrl + P"),
            ("Nav_Settings", () => shell.ShowSettingsCommand.ExecuteAsync(null), () => new SettingsView(), "Ctrl + G")
        };
        foreach (var page in nav)
        {
            await NavigateAsync(page);
            var clock = Stopwatch.StartNew();
            var task = Open();
            clock.Stop();
            var popup = Popup();
            var current = Rows(popup, "CurrentTable");
            var other = Rows(popup, "OtherTable");
            Console.WriteLine($"SHORTCUT_AUDIT {page.Name} current={current.Length} other={other.Length} shown={current.Length+other.Length} open_ms={clock.Elapsed.TotalMilliseconds:F1}");
            Assert(((TextBlock)popup.FindName("PageSubtitle")).Text == text.Format("Shortcut_Subtitle", text[page.Name]), "Shortcut reference names the launching page");
            Assert(current.Any(row => row.Key == page.Key), "Current shortcut matches " + page.Name);
            Assert(!current.Select(r => (r.Key, r.Action)).Intersect(other.Select(r => (r.Key, r.Action))).Any(), "A binding is never repeated across tables");
            Assert(other.Select(r => (r.Key, r.Action)).Distinct().Count() == other.Length && other.All(r => !string.IsNullOrWhiteSpace(r.Page)), "Other pages aggregate identical bindings");
            if (page.Name == "Nav_Pos")
            {
                Assert(current.Single(r => r.Key == "F1").Availability == text["Shortcut_CartRequired"], "An empty cart keeps its unavailable shortcuts in the reference");
                Assert(other.Any(r => r.Key == "Ctrl + Alt + R"), "Register previous purchases and Reports repeat sale share a key without suppressing either");
            }
            if (page.Name == "Nav_Products")
                Assert(current.Single(r => r.Key == "Ctrl + H").Availability == text["Shortcut_ProductRequired"], "Inventory history appears with its product requirement");
            if (page.Name == "Nav_Purchases")
                Assert(current.Any(r => r.Key == "Ctrl + F" && r.Availability is null), "General retail Purchases lists its working product search shortcut");
            Assert(current.All(r => !r.Key.StartsWith("Ctrl + Alt + A")), "Only registered shortcuts appear");
            KeyDown((Button)popup.FindName("HeaderClose"), Key.Escape);
            await task;
            Assert(!InAppDialog.IsOpen && ReferenceEquals(Keyboard.FocusedElement, more), "Escape closes the popup and returns focus to More");
        }

        var setup = sp.GetRequiredService<IBusinessSetup>();
        var saved = await setup.LoadAsync();
        await setup.SaveDraftAsync(saved.Profile with { BusinessType = "pharmacy" }, saved.Step);
        await shell.RefreshFeaturesAsync();
        await NavigateAsync(nav[3]);
        var pharmacy = Open();
        Assert(Rows(Popup(), "CurrentTable").Any(r => r.Key == "Ctrl + F"), "Pharmacy Purchases includes its actual lookup binding");
        Dismiss(Popup());
        await pharmacy;
        await setup.SaveDraftAsync(saved.Profile, saved.Step);
        await shell.RefreshFeaturesAsync();

        await NavigateAsync(nav[0]);
        var register = (PosViewModel)shell.CurrentViewModel;
        var product = await sp.GetRequiredService<PosDbContext>().Products.AsNoTracking().FirstAsync();
        await register.AddFromResultsCommand.ExecuteAsync(product);
        Assert(register.Lines.Count == 1, "A checkout test has one isolated cart line");
        foreach (var (width, height, language) in new[] { (1920, 1080, "en"), (1366, 768, "en"), (1280, 720, "ur") })
        {
            text.SetLanguage(language);
            window.Width = width;
            window.Height = height;
            Pump();
            var task = Open();
            var popup = Popup();
            var root = (FrameworkElement)window.Content;
            var card = (Border)popup.Parent;
            var rect = card.TransformToAncestor(root).TransformBounds(new Rect(0, 0, card.ActualWidth, card.ActualHeight));
            Assert(Math.Abs(rect.Left + rect.Right - root.ActualWidth) < 2 && Math.Abs(rect.Top + rect.Bottom - root.ActualHeight) < 2 &&
                card.ActualWidth <= 620 && card.ActualHeight <= root.ActualHeight * .78 + 1, $"Shortcut card is centered and bounded at {width}×{height}");
            Assert(Rows(popup, "CurrentTable").Single(r => r.Key == "F1").Availability is null, "A ready cart enables its checkout shortcut");
            var scroll = (ScrollViewer)popup.FindName("ReferenceScroll");
            if (width == 1280)
            {
                Assert(scroll.ScrollableHeight > 0 && Descendants(popup).OfType<TextBlock>().Any(t => t.Text == "F1" && t.FlowDirection == FlowDirection.LeftToRight), "Urdu keycaps stay LTR and small windows scroll internally");
                var header = (Button)popup.FindName("HeaderClose");
                var top = header.TransformToAncestor(root).Transform(new Point()).Y;
                scroll.ScrollToEnd();
                Pump();
                Assert(scroll.VerticalOffset > 0 && Math.Abs(header.TransformToAncestor(root).Transform(new Point()).Y - top) < 1,
                    "Scrolling keeps the header in place");
                scroll.ScrollToTop();
                Pump();
            }
            KeyDown((Button)popup.FindName("HeaderClose"), Key.F1);
            Assert(!register.IsPaymentOpen, "Shortcut help blocks checkout commands behind it");
            Capture(window, Path.Combine(screenshots, $"shortcut-reference-{language}-{width}.png"));
            Dismiss(popup);
            await task;
        }
        KeyDown(more, Key.F1);
        Assert(register.IsPaymentOpen && register.IsCash, "F1 still opens Cash");
        register.CancelPaymentCommand.Execute(null);
        KeyDown(more, Key.F2);
        Assert(register.IsPaymentOpen && register.ShowCardInstructions, "F2 still opens Card");
        register.CancelPaymentCommand.Execute(null);
        KeyDown(more, Key.F3);
        Assert(register.IsPaymentOpen && register.IsCredit, "F3 still opens Udhar");
        register.CancelPaymentCommand.Execute(null);
        KeyDown(more, Key.F4);
        Pump();
        Assert(InAppDialog.IsOpen && register.Lines.Count == 1, "F4 retains the Clear Cart confirmation");
        KeyDown((FrameworkElement)Keyboard.FocusedElement!, Key.Escape);
        await register.ClearCartCommand.ExecutionTask!;
        register.RemoveLineCommand.Execute(register.Lines.Single());

        var readOnlyLicense = new LicenseClient(sp.GetRequiredService<PublisherOptions>() with { Commercial = true },
            sp.GetRequiredService<ProtectedLocalStore>(), ProtectedLocalStore.DeviceHash());
        var readOnly = new MainViewModel(sp, sp.GetRequiredService<IAuthService>(), shell.Login, shell.StoreProfile,
            shell.Scanner, shell.StockAlerts, readOnlyLicense, sp.GetRequiredService<SoftwareUpdates>())
        {
            IsLoggedIn = true, CurrentViewModel = register, ActiveScreen = "pos"
        };
        Assert(readOnly.IsReadOnly, "The license fixture is restricted to reading records");
        window.DataContext = readOnly;
        Pump();
        var restricted = Open();
        Assert(!Rows(Popup(), "CurrentTable").Any(r => r.Key is "F1" or "F2" or "F3") &&
            !Rows(Popup(), "OtherTable").Any(r => r.Key is "Ctrl + S" or "Ctrl + Alt + R"),
            "The reference does not advertise checkout, product save or repeat sale in records-only mode");
        Dismiss(Popup());
        await restricted;
        window.Close();
        text.SetLanguage("en");
        scale.Save(100);

        async Task NavigateAsync((string Name, Func<Task> Open, Func<UserControl> View, string Key) page)
        {
            await page.Open();
            var view = page.View();
            view.DataContext = shell.CurrentViewModel;
            content.Content = view;
            Pump();
        }
        Task Open()
        {
            more.Focus();
            var task = (Task)open.Invoke(window, null)!;
            Pump();
            return task;
        }
        ShortcutReference Popup() => Descendants(window).OfType<ShortcutReference>().Single();
        static void Dismiss(ShortcutReference popup) =>
            ((Button)popup.FindName("HeaderClose")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        static void KeyDown(FrameworkElement target, Key key) =>
            target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        static (string Key, string Action, string Page, string? Availability)[] Rows(ShortcutReference popup, string table) =>
            ((DataGrid)popup.FindName(table)).Items.Cast<object>().Select(row =>
            {
                string? Read(string property) => (string?)row.GetType().GetProperty(property)!.GetValue(row);
                return (Read("Shortcut")!, Read("Action")!, Read("Page")!, Read("Availability"));
            }).ToArray();
    }
}
