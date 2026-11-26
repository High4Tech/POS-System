using System.Windows;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Infrastructure.Persistence;
using SupermarketPos.Wpf;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.ViewModels;
using SupermarketPos.Wpf.Views;

internal static partial class Program
{
    private static async Task KeyboardSmokeAsync(IServiceProvider sp)
    {
        var setup = sp.GetRequiredService<IBusinessSetup>();
        var auth = sp.GetRequiredService<IAuthService>();
        var password = $"Test-{Guid.NewGuid():N}!";
        var profile=new SupermarketPos.Domain.Business.BusinessProfile{StoreName="Tech Retail keyboard test",BusinessType="electronics"};profile.ApplyBusinessDefaults();
        await setup.CompleteAsync(profile, "smoke-owner", password);
        await auth.LoginAsync("smoke-owner", password);
        var db = sp.GetRequiredService<PosDbContext>();
        var product = await db.Products.OrderBy(p => p.Id).FirstAsync(p => p.BaseStockQuantity >= 5);
        var cart = new Cart();
        cart.AddOrIncrement(product, 2);
        var customer = SaleCustomer.Create("Keyboard test customer", "03000000002", null, null)!;
        var total = PricingService.GrossTotal(cart);
        await sp.GetRequiredService<ISaleService>().CheckoutAsync(cart, new CashPayment(total, total),
            auth.CurrentUser!, customer: customer);

        var shell = sp.GetRequiredService<MainViewModel>();
        var window = sp.GetRequiredService<MainWindow>();
        var views = new (Type Model, Type View)[]
        {
            (typeof(PosViewModel), typeof(PosView)), (typeof(DashboardViewModel), typeof(DashboardView)),
            (typeof(ProductsViewModel), typeof(ProductsView)), (typeof(CustomersViewModel), typeof(CustomersView)),
            (typeof(PurchasesViewModel), typeof(PurchasesView)), (typeof(ReportsViewModel), typeof(ReportsView)),
            (typeof(SettingsViewModel), typeof(SettingsView))
        };
        foreach (var (model, view) in views)
        {
            var template = new DataTemplate(model) { VisualTree = new FrameworkElementFactory(view) };
            window.Resources.Add(template.DataTemplateKey, template);
        }
        window.DataContext = shell;
        window.Title = "POS shortcut smoke test (isolated)";
        System.Windows.Application.Current.MainWindow = window;
        window.Closed += (_, _) => System.Windows.Threading.Dispatcher.ExitAllFrames();
        window.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler((_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is not (Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift))
                Console.WriteLine($"INPUT page={shell.ActiveScreen} key={key} modifiers={Keyboard.Modifiers} handled={e.Handled}");
        }), true);
        shell.IsLoggedIn = true;
        await shell.ShowPosCommand.ExecuteAsync(null);
        window.Show();
        Console.WriteLine($"READY app={typeof(MainWindow).Assembly.Location} store={sp.GetRequiredService<PosStorage>().DatabasePath}");
    }
}
