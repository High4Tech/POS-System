using System.Windows;
using System.Windows.Threading;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SupermarketPos.Infrastructure;
using SupermarketPos.Infrastructure.Scanning;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Wpf.ViewModels;
using SupermarketPos.Wpf.Views;
using System.IO;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Infrastructure.Commercial;

namespace SupermarketPos.Wpf;

public partial class App : System.Windows.Application
{
    private IServiceProvider? _provider;
    private ServiceProvider? _root;
    private IServiceScope _scope = null!;
    private bool _closing;
    private bool _canClose;
    private Diagnostics? _diagnostics;
    private readonly DispatcherTimer _backupTimer=new(){Interval=TimeSpan.FromMinutes(30)};
    private BackupSchedule? _backupSchedule;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;

        try
        {
            var storage = new PosStorage();
            var folder = Path.GetDirectoryName(storage.DatabasePath)!;
            _diagnostics = new Diagnostics(Path.Combine(folder, "Diagnostics"));
            var protectedStore = new ProtectedLocalStore(Path.Combine(folder, "Protected"));
            await DataBackup.ApplyPendingRestoreAsync(folder, protectedStore);
            var services = new ServiceCollection();
            services.AddSingleton(storage);
            services.AddSingleton(_diagnostics);
            services.AddSingleton(protectedStore);
            var publisher = PublisherConfiguration.Read();
            services.AddSingleton(publisher);
            services.AddSingleton(new LicenseClient(publisher, protectedStore, ProtectedLocalStore.DeviceHash()));
            services.AddSingleton<SoftwareUpdates>();
            services.AddPos(storage.DatabasePath);
            RegisterViewModels(services);

            var root = _root = services.BuildServiceProvider();
            // One long-lived scope drives the whole single-user register app.
            _scope = root.CreateScope();
            _provider = _scope.ServiceProvider;

            await DependencyInjection.InitializeDatabaseAsync(_provider, createDefaultUser: false);

            // Apply persisted language before any window shows.
            var settings = _provider.GetRequiredService<SupermarketPos.Application.Abstractions.ISettingsRepository>();
            var current = await settings.GetAsync();
            LocalizationManager.Instance.SetLanguage(current.Language);
            _provider.GetRequiredService<StoreProfile>().Apply(current);
            _provider.GetRequiredService<DesktopTextSize>().Load();
            TableLayout.Load(storage);
            PanelHeights.Load(storage);

            var setup = await _provider.GetRequiredService<IBusinessSetup>().LoadAsync();
            var license = _provider.GetRequiredService<LicenseClient>();
            var activationRequired = false;
            try { activationRequired = publisher.Commercial && license.Current is null; }
            catch (CommercialException) { activationRequired = publisher.Commercial; }
            if (!setup.Completed || activationRequired)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var wizard = _provider.GetRequiredService<OnboardingWindow>();
                if (wizard.ShowDialog() != true) { Shutdown(); return; }
                current = await settings.GetAsync();
                LocalizationManager.Instance.SetLanguage(current.Language);
                _provider.GetRequiredService<StoreProfile>().Apply(current);
            }
            var context = _provider.GetRequiredService<SupermarketPos.Infrastructure.Persistence.PosDbContext>();
            await _provider.GetRequiredService<SupermarketPos.Infrastructure.Business.MedicineCatalogService>().EnsureSelectedAsync();
            _provider.GetRequiredService<StoreProfile>().Apply(await settings.GetAsync());
            context.WritePermission = () => { try { return !publisher.Commercial || license.Current is not null; } catch { return false; } };

            var window = _provider.GetRequiredService<MainWindow>();
            var shell = _provider.GetRequiredService<MainViewModel>();
            window.DataContext = shell;
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Closing += CloseScannerBeforeWindow;
            window.Show();
            _provider.GetRequiredService<SoftwareUpdates>().RecordSuccessfulStartup();
            _provider.GetRequiredService<ScannerController>().Start();
            _backupSchedule=new BackupSchedule(Path.GetDirectoryName(_provider.GetRequiredService<PosStorage>().DatabasePath)!);_backupTimer.Tick+=async(_,_)=>await CheckScheduledBackupAsync();_backupTimer.Start();_ =CheckScheduledBackupAsync();
            if (_provider.GetRequiredService<IAuthService>().IsLoggedIn) await shell.EnterConfiguredStoreAsync();
            _ = CheckUpdatesInBackgroundAsync();
        }
        catch (Exception ex)
        {
            _diagnostics?.TryCapture(ex, "startup");
            NoticeDialog.Show(ex is CommercialException or SupermarketPos.Domain.Business.BusinessValidationException ? LocalizationManager.Instance[ex.Message] : LocalizationManager.Instance["Setup_StartupFailed"], LocalizationManager.Instance["Common_Error"]);
            Shutdown(1);
        }
    }

    private async void CloseScannerBeforeWindow(object? sender, CancelEventArgs e)
    {
        if (_canClose || sender is not Window window) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        try
        {
            // Keep the dispatcher available while in-flight scan handlers finish.
            if (_provider?.GetService<ScannerController>() is { } scanner)
                await scanner.StopAsync();
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("POS scanner shutdown failed: {0}", ex.Message);
        }
        finally
        {
            _canClose = true;
            await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Background);
            window.Close();
        }
    }

    private static void RegisterViewModels(IServiceCollection services)
    {
        services.AddSingleton<MainWindow>();
        services.AddSingleton<StoreProfile>();
        services.AddSingleton<DesktopTextSize>();
        services.AddScoped<StockAlertsViewModel>();
        services.AddScoped<SupermarketPos.Infrastructure.Business.SaleHistory>();
        services.AddScoped<SupermarketPos.Infrastructure.Business.ReturnsService>();
        services.AddSingleton<ScannerStorage>();
        services.AddSingleton<IScannerTrustStore>(sp => sp.GetRequiredService<ScannerStorage>());
        services.AddSingleton<IScannerIdentityStore>(sp => sp.GetRequiredService<ScannerStorage>());
        services.AddSingleton(sp => new ScannerSession(store: sp.GetRequiredService<IScannerTrustStore>()));
        services.AddSingleton<MobileScannerHost>();
        services.AddSingleton<ScannerFeedback>();
        services.AddScoped<ScannerController>();
        services.AddScoped<ReceiptPreviewDialog>();
        services.AddScoped<DigitalServicesView>();
        services.AddScoped<MainViewModel>();
        services.AddScoped<LoginViewModel>();
        services.AddTransient<OnboardingViewModel>();
        services.AddTransient<OnboardingWindow>();
        services.AddTransient<BusinessSettingsWindow>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<PosViewModel>();
        services.AddTransient<ProductsViewModel>();
        services.AddTransient<PurchasesViewModel>();
        services.AddTransient<ReportsViewModel>();
        services.AddTransient<CustomersViewModel>();
        services.AddTransient<SettingsViewModel>();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _diagnostics?.TryCapture(e.Exception, "desktop");
        NoticeDialog.Show(e.Exception is CommercialException or SupermarketPos.Domain.Business.BusinessValidationException ? LocalizationManager.Instance[e.Exception.Message] : LocalizationManager.Instance["Setup_UnexpectedError"], LocalizationManager.Instance["Common_Error"]);
        e.Handled = true;
    }

    private async Task CheckScheduledBackupAsync()
    {
        try{if(_backupSchedule is not null)await Task.Run(()=>_backupSchedule.RunDueAsync());}
        catch(Exception ex){_diagnostics?.TryCapture(ex,"scheduled-backup");}
    }
    private async Task CheckUpdatesInBackgroundAsync()
    {
        try
        {
            if (_provider?.GetRequiredService<LicenseClient>().Current is not null)
                await _provider.GetRequiredService<SoftwareUpdates>().CheckAsync();
        }
        catch { /* Offline use and a valid certificate are independent of update service availability. */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _backupTimer.Stop();
        if(_scope is IAsyncDisposable asyncScope)asyncScope.DisposeAsync().AsTask().GetAwaiter().GetResult();else _scope?.Dispose();
        _root?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnExit(e);
    }
}
