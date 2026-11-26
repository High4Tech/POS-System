using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using QuestPDF.Infrastructure;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Application.Services;
using SupermarketPos.Infrastructure.Documents;
using SupermarketPos.Infrastructure.Persistence;
using SupermarketPos.Infrastructure.Business;

namespace SupermarketPos.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers persistence + application services. Pass a custom DB path for tests.</summary>
    public static IServiceCollection AddPos(this IServiceCollection services, string? dbPath = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        dbPath ??= DefaultDbPath();
        services.AddDbContext<PosDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
        services.AddSingleton(new PosMediaStore(Path.GetDirectoryName(Path.GetFullPath(dbPath))!));

        // Persistence
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ISaleRepository, SaleRepository>();
        services.AddScoped<IPurchaseRepository, PurchaseRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddScoped<IReceiptNumberGenerator, ReceiptNumberGenerator>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ITradeLedger, TradeLedger>();
        services.AddScoped<IBusinessSetup, BusinessSetup>();
        services.AddScoped<DigitalServiceLedger>();
        services.AddScoped<RetailReferenceCatalog>();services.AddScoped<RetailInventory>();services.AddScoped<RetailCheckout>();
        services.AddScoped<MedicineCatalogService>();
        services.AddScoped<PharmacyInventory>();
        services.AddScoped<StockLocationService>();services.AddScoped<IStockLocations>(p=>p.GetRequiredService<StockLocationService>());
        services.AddScoped<IBusinessCompliance, FbrCompliance>();

        // Application services
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ISaleService, SaleService>();
        services.AddScoped<RegisterBarcodeLookup>();
        services.AddScoped<BulkRecordService>();
        services.AddScoped<IPurchaseService, PurchaseService>();
        services.AddScoped<InvoiceMarginService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IBarcodeService, BarcodeService>();

        // Documents (QuestPDF)
        services.AddScoped<IReceiptService, ReceiptService>();
        services.AddScoped<IReportPdfService, ReportPdfService>();
        services.AddScoped<IShelfLabelService, ShelfLabelService>();

        return services;
    }

    /// <summary>Applies migrations and seeds first-run data.</summary>
    public static async Task InitializeDatabaseAsync(IServiceProvider provider, CancellationToken ct = default, bool createDefaultUser = true)
    {
        var db = provider.GetRequiredService<PosDbContext>();
        var source = db.Database.GetDbConnection().DataSource;
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToArray();
        var recoveryBackup = File.Exists(source) && pending.Length > 0 ? await BackupDatabaseAsync(source, "Before-schema-upgrade", ct) : null;
        if (File.Exists(source) && (await db.Database.GetPendingMigrationsAsync(ct)).Contains(Persistence.Migrations.BusinessOnboarding.MigrationId))
            await BackupDatabaseAsync(source, "Before-business-onboarding", ct);
        if (File.Exists(source) && (await db.Database.GetPendingMigrationsAsync(ct))
                .Contains(Persistence.Migrations.AccountsAndDevices.MigrationId))
            await BackupDatabaseAsync(source, "Before-accounts-devices", ct);
        if (File.Exists(source) && (await db.Database.GetPendingMigrationsAsync(ct))
                .Contains(Persistence.Migrations.InventoryExpiry.MigrationId))
            await BackupDatabaseAsync(source, "Before-expiry", ct);
        try
        {
            await db.Database.MigrateAsync(ct);
            await Commercial.DataBackup.VerifyAsync(source, ct);
        }
        catch when (recoveryBackup is not null)
        {
            await db.Database.CloseConnectionAsync();
            SqliteConnection.ClearAllPools();
            var failed = Path.Combine(Path.GetDirectoryName(source)!, "Backups", "Failed-schema-upgrade-" + Guid.NewGuid().ToString("N") + ".db");
            if (File.Exists(source)) File.Move(source, failed);
            foreach (var suffix in new[] { "-wal", "-shm" })
                if (File.Exists(source + suffix)) File.Move(source + suffix, failed + suffix);
            File.Copy(recoveryBackup, source);
            db.ChangeTracker.Clear();
            throw new Domain.Business.BusinessValidationException("Setup_MigrationRecovered");
        }
        var hasher = provider.GetRequiredService<IPasswordHasher>();
        await DatabaseSeeder.SeedAsync(db, hasher, ct, createDefaultUser);
        await UpgradeImageReferencesAsync(db, provider.GetRequiredService<PosMediaStore>(), source, ct);
    }

    private static async Task UpgradeImageReferencesAsync(PosDbContext db, PosMediaStore media, string source, CancellationToken ct)
    {
        var products = await db.Products.Where(product => product.ImagePath != null &&
            (product.ImagePath.Contains("\\") || product.ImagePath.Contains("/"))).ToListAsync(ct);
        foreach (var product in products)
            if (ImportExistingImage(media, media.ResolvePath(product.ImagePath)) is { } token) product.SetImagePath(token);

        var categories = await db.Categories.Where(category => category.ImageFileName != null && category.ImageFileName.Length != 32).ToListAsync(ct);
        foreach (var category in categories)
            if (ImportExistingImage(media, media.ResolveCategoryPath(category.ImageFileName)) is { } token) category.SetImage(token);

        var profiles = await db.Settings.Where(profile => profile.LogoPath != null &&
            (profile.LogoPath.Contains("\\") || profile.LogoPath.Contains("/"))).ToListAsync(ct);
        foreach (var profile in profiles)
            if (ImportExistingImage(media, media.ResolvePath(profile.LogoPath)) is { } token) profile.SetLogoPath(token);

        if (!db.ChangeTracker.HasChanges()) return;
        if (File.Exists(source)) await BackupDatabaseAsync(source, "Before-media", ct);
        await db.SaveChangesAsync(ct);
    }

    private static string? ImportExistingImage(PosMediaStore media, string? path)
    {
        if (path is null || !File.Exists(path)) return null;
        try { return media.Import(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning("Could not copy a legacy POS image: {0}", error.Message);
            return null;
        }
    }

    private static async Task<string> BackupDatabaseAsync(string source, string label, CancellationToken ct)
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source))!, "Backups")).FullName;
        var path = Path.Combine(folder, $"{label}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
        var temporary = path + ".tmp";
        try
        {
            var options = new SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false };
            await using (var backup = new SqliteConnection(options.ToString()))
            {
                await using var original = new SqliteConnection(new SqliteConnectionStringBuilder
                    { DataSource = source, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
                await original.OpenAsync(ct);
                await backup.OpenAsync(ct);
                original.BackupDatabase(backup);
            }
            File.Move(temporary, path);
            await Commercial.DataBackup.VerifyAsync(path, ct);
            return path;
        }
        finally { File.Delete(temporary); }
    }

    public static string DefaultDbPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SupermarketPos");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "pos.db");
    }
}
