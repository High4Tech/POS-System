using Microsoft.EntityFrameworkCore;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Business;

namespace SupermarketPos.Infrastructure.Persistence;

public sealed class PosDbContext : DbContext
{
    public Func<bool>? WritePermission { get; set; }
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        CheckWritePermission(); return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        CheckWritePermission(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
    private void CheckWritePermission()
    {
        if (ChangeTracker.HasChanges() && WritePermission?.Invoke() == false) throw new Commercial.CommercialException("License_RecordsOnly");
    }
    public PosDbContext(DbContextOptions<PosDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<InventoryBatch> InventoryBatches => Set<InventoryBatch>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<PurchaseLine> PurchaseLines => Set<PurchaseLine>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SaleReturn> SaleReturns => Set<SaleReturn>();
    public DbSet<CustomerCreditMovement> CustomerCreditMovements => Set<CustomerCreditMovement>();
    public DbSet<DeviceReturnRelease> DeviceReturnReleases => Set<DeviceReturnRelease>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();
    public DbSet<AccountPayment> AccountPayments => Set<AccountPayment>();
    public DbSet<SerializedUnit> SerializedUnits => Set<SerializedUnit>();
    public DbSet<BusinessConfiguration> BusinessConfigurations => Set<BusinessConfiguration>();
    public DbSet<BusinessAudit> BusinessAudits => Set<BusinessAudit>();
    public DbSet<ProductTaxRule> ProductTaxRules => Set<ProductTaxRule>();
    public DbSet<FiscalSubmission> FiscalSubmissions => Set<FiscalSubmission>();
    public DbSet<ServiceProviderAccount> ServiceProviders=>Set<ServiceProviderAccount>();
    public DbSet<MobileServicePackage> MobileServicePackages=>Set<MobileServicePackage>();
    public DbSet<ServiceCheckout> ServiceCheckouts=>Set<ServiceCheckout>();
    public DbSet<ServiceTransaction> ServiceTransactions=>Set<ServiceTransaction>();
    public DbSet<ServicePosting> ServicePostings=>Set<ServicePosting>();
    public DbSet<DigitalCustomer> DigitalCustomers=>Set<DigitalCustomer>();
    public DbSet<MedicineReference> MedicineReferences=>Set<MedicineReference>();
    public DbSet<CatalogImport> CatalogImports=>Set<CatalogImport>();
    public DbSet<StockReconciliation> StockReconciliations=>Set<StockReconciliation>();
    public DbSet<StockLocation> StockLocations=>Set<StockLocation>();
    public DbSet<StockPlacement> StockPlacements=>Set<StockPlacement>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<CustomerCreditMovement>(e=>{e.HasIndex(m=>m.ConfirmationId).IsUnique();e.HasIndex(m=>m.SaleReturnId);e.Property(m=>m.Amount).HasColumnType("TEXT");e.HasOne<SaleReturn>().WithMany().HasForeignKey(m=>m.SaleReturnId).OnDelete(DeleteBehavior.Restrict);e.HasOne<Sale>().WithMany().HasForeignKey(m=>m.TargetSaleId).OnDelete(DeleteBehavior.Restrict);});
        b.Entity<DeviceReturnRelease>(e=>{e.HasIndex(r=>r.ConfirmationId).IsUnique();e.HasIndex(r=>r.SaleLineId).IsUnique();e.HasOne<SaleReturn>().WithMany().HasForeignKey(r=>r.SaleReturnId).OnDelete(DeleteBehavior.Restrict);e.HasOne<SaleLine>().WithMany().HasForeignKey(r=>r.SaleLineId).OnDelete(DeleteBehavior.Restrict);e.HasOne<SerializedUnit>().WithMany().HasForeignKey(r=>r.SerializedUnitId).OnDelete(DeleteBehavior.Restrict);});
        b.Entity<SerializedUnit>().Property(u=>u.PurchaseCost).HasColumnType("TEXT");
        b.Entity<InventoryBatch>().Property(u=>u.PurchaseCost).HasColumnType("TEXT");
        b.Entity<ServicePosting>().HasIndex(p=>p.PostedAt);
        b.Entity<MedicineReference>(e=>{e.HasIndex(m=>m.SourceKey).IsUnique();e.HasIndex(m=>m.RegistrationNumber);e.HasIndex(m=>m.SearchText);});
        b.Entity<CatalogImport>().HasKey(i=>i.TemplateId);
        b.Entity<StockLocation>(e=>{e.HasIndex(l=>l.Barcode).IsUnique();e.HasOne<StockLocation>().WithMany().HasForeignKey(l=>l.ParentId).OnDelete(DeleteBehavior.Restrict);});
        b.Entity<StockPlacement>(e=>{e.HasIndex(p=>p.AllocationKey).IsUnique();e.Property(p=>p.Revision).IsConcurrencyToken();e.HasIndex(p=>new{p.ProductId,p.BatchId});e.HasOne<Product>().WithMany().HasForeignKey(p=>p.ProductId).OnDelete(DeleteBehavior.Restrict);e.HasOne<InventoryBatch>().WithMany().HasForeignKey(p=>p.BatchId).OnDelete(DeleteBehavior.Restrict);e.HasOne<StockLocation>().WithMany().HasForeignKey(p=>p.LocationId).OnDelete(DeleteBehavior.Restrict);});
        b.Entity<StockReconciliation>(e=>{e.HasIndex(r=>r.SaleLineId).IsUnique();e.HasIndex(r=>new{r.ProductId,r.ResolvedAt});e.HasOne<Product>().WithMany().HasForeignKey(r=>r.ProductId).OnDelete(DeleteBehavior.Restrict);e.HasOne<SaleLine>().WithMany().HasForeignKey(r=>r.SaleLineId).OnDelete(DeleteBehavior.Restrict);});
        b.Entity<ServiceProviderAccount>(e=>{foreach(var name in new[]{"OpeningFloat","DefaultCustomerFee","DefaultProviderFee","DefaultCommission","LoadCostRate","CustomerFeePercent"})e.Property<decimal>(name).HasColumnType("TEXT");e.Property(p=>p.Revision).IsConcurrencyToken();e.HasIndex(p=>p.Name).IsUnique();});
        b.Entity<MobileServicePackage>(e=>{e.Property(p=>p.ProviderCostKnown).HasDefaultValue(true);foreach(var name in new[]{"Price","ProviderCost","Commission","CustomerFee","ProviderFee"})e.Property<decimal>(name).HasColumnType("TEXT");e.HasOne<ServiceProviderAccount>().WithMany().HasForeignKey(p=>p.ProviderId).OnDelete(DeleteBehavior.Restrict);});
        b.Entity<ServiceCheckout>(e=>{e.Ignore(c=>c.ReceiptNumber);e.Ignore(c=>c.Change);e.HasIndex(c=>c.ConfirmationId).IsUnique();e.Property(c=>c.Total).HasColumnType("TEXT");e.Property(c=>c.Tendered).HasColumnType("TEXT");e.HasMany(c=>c.Lines).WithOne().HasForeignKey(t=>t.CheckoutId).OnDelete(DeleteBehavior.Restrict);});
        b.Entity<ServiceTransaction>(e=>{e.Property(p=>p.ProviderCostKnown).HasDefaultValue(true);e.Ignore(t=>t.ReceiptNumber);e.Ignore(t=>t.SettlementAmount);e.Ignore(t=>t.CashChange);e.Ignore(t=>t.Income);e.Property(t=>t.Revision).IsConcurrencyToken();e.HasIndex(t=>t.ConfirmationId).IsUnique();e.HasIndex(t=>t.ReversesId).IsUnique();foreach(var name in new[]{"Principal","CustomerFee","ProviderFee","Commission","ProviderCost","Collected","FloatChange"})e.Property<decimal>(name).HasColumnType("TEXT");e.HasOne<ServiceProviderAccount>().WithMany().HasForeignKey(t=>t.ProviderId).OnDelete(DeleteBehavior.Restrict);e.HasOne<ServiceTransaction>().WithMany().HasForeignKey(t=>t.ReversesId).OnDelete(DeleteBehavior.Restrict);});
        b.Entity<ServicePosting>(e=>{e.HasIndex(p=>p.TransactionId).IsUnique();foreach(var name in new[]{"CashChange","FloatChange","Income"})e.Property<decimal>(name).HasColumnType("TEXT");e.HasOne<ServiceTransaction>().WithMany().HasForeignKey(p=>p.TransactionId).OnDelete(DeleteBehavior.Restrict);});
        b.Entity<DigitalCustomer>(e=>{e.HasKey(c=>c.RecipientKey);e.Property(c=>c.RecipientKey).HasMaxLength(160);e.Property(c=>c.Name).HasMaxLength(150);});
        b.Entity<ServiceTransaction>().HasIndex(t=>new{t.RecipientKey,t.OccurredAt});
        b.Entity<BusinessConfiguration>().Property(c => c.Revision).IsConcurrencyToken();
        b.Entity<ProductTaxRule>(e =>
        {
            e.Property(r => r.Rate).HasColumnType("TEXT");
            e.HasIndex(r => new { r.ProductId, r.EffectiveFrom }).IsUnique();
            e.HasOne<Product>().WithMany().HasForeignKey(r => r.ProductId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<FiscalSubmission>(e =>
        {
            e.HasIndex(r => r.SaleId).IsUnique();
            e.HasOne<Sale>().WithMany().HasForeignKey(r => r.SaleId).OnDelete(DeleteBehavior.Restrict);
        });
        // Money stays decimal; the SQLite provider stores decimal as TEXT (never REAL/float),
        // preserving exact values. Numeric comparisons over money are done in memory, not SQL.

        b.Entity<Category>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).IsRequired().HasMaxLength(100);
            e.Property(c => c.ColorHex).HasMaxLength(7);
            e.Property(c => c.ImageFileName).HasMaxLength(36);
        });

        b.Entity<Product>(e =>
        {
            e.Ignore(p=>p.IsFinancialService);
            e.Property(p=>p.Kind).HasMaxLength(16).HasDefaultValue("labeled");
            e.Property(p=>p.ServiceType).HasMaxLength(16).HasDefaultValue("SERVICE");
            e.Property(p=>p.CatalogKey).HasMaxLength(120).HasDefaultValue("");
            e.HasIndex(p=>p.CatalogKey).IsUnique().HasFilter("\"CatalogKey\" <> ''");
            e.Property(p=>p.StockTracked).HasDefaultValue(true).HasSentinel(true);
            e.Property(p=>p.SellingUnit).HasDefaultValue("piece");e.Property(p=>p.UnitsPerStrip).HasDefaultValue(1);e.Property(p=>p.StripSellingPrice).HasColumnType("TEXT");
            e.HasIndex(p=>p.MedicineReferenceId).IsUnique();
            e.HasOne<MedicineReference>().WithMany().HasForeignKey(p=>p.MedicineReferenceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StockLocation>().WithMany().HasForeignKey(p=>p.DefaultLocationId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(p => p.UnassignedStock);
            e.Property(p => p.TrackExpiry).HasDefaultValue(false);
            e.Property(p => p.StockRevision).IsConcurrencyToken();
            e.HasMany(p => p.Batches).WithOne().HasForeignKey(batch => batch.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(p => p.Batches).HasField("_batches").UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasKey(p => p.Id);
            e.Property(p => p.Barcode).IsRequired().HasMaxLength(64);
            e.HasIndex(p => p.Barcode).IsUnique();
            e.Property(p => p.Name).IsRequired().HasMaxLength(200);
            e.Property(p => p.UnitPrice).HasColumnType("TEXT");
            e.Property(p => p.PurchaseCost).HasColumnType("TEXT");
            e.Property(p => p.VatRate).HasColumnType("TEXT");
            e.Ignore(p => p.StockQuantity);
            e.Property<decimal>("LegacyStockQuantity").HasColumnName("StockQuantity").HasColumnType("TEXT");
            e.Property(p=>p.BaseStockQuantity).IsConcurrencyToken();
            e.Property(p=>p.UnitsPerPackage).HasDefaultValue(1);
            e.Property(p=>p.PurchaseUnit).HasDefaultValue("box");
            e.Property(p=>p.PackagePurchaseCost).HasColumnType("TEXT");e.Property(p=>p.PackageSellingPrice).HasColumnType("TEXT");e.Property(p=>p.MinimumStock).HasColumnType("TEXT");
            e.HasIndex(p=>p.PackageBarcode).IsUnique();
            e.Property(p => p.ImagePath).HasMaxLength(500);
            e.Property(p => p.IsActive).HasDefaultValue(true);
            e.HasOne(p => p.Category)
                .WithMany()
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Supplier>().WithMany().HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<InventoryBatch>(e =>
        {
            e.ToTable("InventoryBatches");
            e.Property(batch => batch.Lot).IsRequired().HasMaxLength(20);
            e.Ignore(batch=>batch.Quantity);e.Property<decimal>("LegacyQuantity").HasColumnName("Quantity").HasColumnType("TEXT");
            e.Property(batch=>batch.BaseQuantity).IsConcurrencyToken();
            e.Property(batch=>batch.HoldReason).IsConcurrencyToken();
            e.HasIndex(batch => new { batch.ProductId, batch.Lot }).IsUnique();
            e.HasOne<Supplier>().WithMany().HasForeignKey(batch => batch.SupplierId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Username).IsRequired().HasMaxLength(64);
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.PasswordHash).IsRequired();
            e.Property(u => u.DisplayName).IsRequired().HasMaxLength(100);
            e.Property(u => u.Role).HasConversion<string>();
        });

        b.Entity<SaleReturn>(e =>
        {
            e.HasIndex(r => r.ConfirmationId).IsUnique();
            e.HasIndex(r => new { r.SaleId, r.Status });
            e.HasOne<Sale>().WithMany().HasForeignKey(r => r.SaleId).OnDelete(DeleteBehavior.Restrict);
            foreach(var property in new[]{nameof(SaleReturn.Gross),nameof(SaleReturn.Tax),nameof(SaleReturn.CashRefund),nameof(SaleReturn.CardRefund),nameof(SaleReturn.ReceivableReduction),nameof(SaleReturn.CustomerCredit)})
                e.Property<decimal>(property).HasColumnType("TEXT");
        });
        b.Entity<Sale>(e =>
        {
            e.Ignore(s => s.Outstanding);
            e.Property(s => s.BalancePaid).HasColumnType("TEXT");
            e.Property(s => s.BalanceRevision).IsConcurrencyToken();
            e.Property(s => s.CustomerAccountKey).HasMaxLength(170);
            e.HasKey(s => s.Id);
            e.Property(s => s.ReceiptNo).IsRequired().HasMaxLength(32);
            e.HasIndex(s => s.ReceiptNo).IsUnique();
            e.HasIndex(s => s.CheckoutId).IsUnique();
            e.HasIndex(s => s.CreatedAt);
            e.HasIndex(s => s.CustomerAccountKey);
            e.Property(s => s.SourceReceiptNo).HasMaxLength(80).UseCollation("NOCASE");
            e.HasIndex(s => s.SourceReceiptNo).IsUnique();
            e.Property(s => s.CashierName).HasMaxLength(100);
            e.Property(s => s.GrossTotal).HasColumnType("TEXT");
            e.Property(s => s.NetTotal).HasColumnType("TEXT");
            e.Property(s => s.VatTotal).HasColumnType("TEXT");
            e.Property(s => s.PaymentMethod).IsRequired().HasMaxLength(20);
            e.Property(s => s.AmountPaid).HasColumnType("TEXT");
            e.Property(s => s.CashTendered).HasColumnType("TEXT");
            e.Property(s => s.ChangeGiven).HasColumnType("TEXT");
            e.Ignore(s => s.VatBreakdown); // computed from lines
            e.Ignore(s => s.Customer);
            e.Ignore(s => s.GrossProfit);
            e.Property(s => s.CustomerName).HasMaxLength(150);
            e.Property(s => s.CustomerPhone).HasMaxLength(60);
            e.Property(s => s.CustomerEmail).HasMaxLength(150);
            e.Property(s => s.CustomerAddress).HasMaxLength(300);
            e.HasMany(s => s.Lines)
                .WithOne()
                .HasForeignKey(l => l.SaleId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Navigation(s => s.Lines)
                .HasField("_lines")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        b.Entity<SaleLine>(e =>
        {
            e.HasIndex(s => new { s.ProductId, s.SaleId });
            e.Property(l => l.BatchLot).HasMaxLength(20);
            e.Property(l => l.SerialNumber).HasMaxLength(64);
            e.Property(l => l.Imei1).HasMaxLength(15);
            e.Property(l => l.Imei2).HasMaxLength(15);
            e.HasIndex(l => new { l.ProductId, l.SerialNumber }).HasFilter("SerialNumber IS NOT NULL");
            e.HasIndex(l => new { l.ProductId, l.SerialNumber,l.SerialCycle }).IsUnique().HasFilter("SerialNumber IS NOT NULL");
            e.HasKey(l => l.Id);
            e.Property(l => l.Barcode).HasMaxLength(64);
            e.Property(l => l.Name).IsRequired().HasMaxLength(200);
            e.Property(l => l.UnitPrice).HasColumnType("TEXT");
            e.Property(l => l.UnitCost).HasColumnType("TEXT");
            e.Property(l => l.VatRate).HasColumnType("TEXT");
            e.Property(l => l.Quantity).HasColumnType("TEXT");
            e.Property(l => l.LineGross).HasColumnType("TEXT");
            e.Property(l => l.DiscountDescription).HasMaxLength(64);
            e.Ignore(l => l.NetAmount);
            e.Ignore(l => l.VatAmount);
        });

        b.Entity<Purchase>(e =>
        {
            e.Ignore(p => p.Outstanding);
            e.Property(p => p.InitialAmountPaid).HasColumnType("TEXT");
            e.Property(p => p.BalancePaid).HasColumnType("TEXT");
            e.Property(p => p.BalanceRevision).IsConcurrencyToken();
            e.Property(p => p.InitialPaymentMethod).HasMaxLength(20);
            e.Property(p => p.Supplier).IsRequired().HasMaxLength(150);
            e.Property(p => p.Reference).IsRequired().HasMaxLength(80);
            e.Property(p => p.ReceivedBy).IsRequired().HasMaxLength(100);
            e.Property(p => p.Notes).HasMaxLength(500).HasDefaultValue("");
            e.HasIndex(p => p.ReceivedAt);
            e.HasIndex(p => p.SupplierId);
            e.HasIndex(p => p.ConfirmationId).IsUnique();
            e.HasOne<Supplier>().WithMany().HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(p => p.Total);
            e.HasMany(p => p.Lines).WithOne().HasForeignKey(l => l.PurchaseId);
            e.Navigation(p => p.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        b.Entity<PurchaseLine>(e =>
        {
            e.Ignore(l=>l.DisplayQuantity);e.Ignore(l=>l.DisplayUnitCost);
            e.Property(l => l.BatchLot).HasMaxLength(20);
            e.Property(l => l.ProductName).IsRequired().HasMaxLength(200);
            e.Property(l => l.Barcode).IsRequired().HasMaxLength(64);
            e.Property(l => l.Quantity).HasColumnType("TEXT");
            e.Property(l => l.UnitCost).HasColumnType("TEXT");
            e.Ignore(l => l.Total);
        });

        b.Entity<Supplier>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(150).UseCollation("NOCASE");
            e.Property(s => s.Kind).HasMaxLength(16);
            e.Property(s => s.ContactName).HasMaxLength(150);
            e.Property(s => s.Phone).HasMaxLength(60);
            e.Property(s => s.Email).HasMaxLength(150);
            e.Property(s => s.Address).HasMaxLength(300);
            e.HasIndex(s => s.Name).IsUnique();
        });

        b.Entity<StockMovement>(e =>
        {
            e.Property(m => m.BatchLot).HasMaxLength(20);
            e.Property(m => m.ProductName).HasMaxLength(200);
            e.Property(m => m.QuantityChange).HasColumnType("TEXT");
            e.Property(m => m.Reference).HasMaxLength(1200);
            e.HasIndex(m => m.ConfirmationId).IsUnique();
            e.HasIndex(m => new { m.ProductId, m.OccurredAt });
        });

        b.Entity<AppSettings>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.StoreName).IsRequired().HasMaxLength(150);
            e.Property(s => s.StoreAddress).HasMaxLength(300);
            e.Property(s => s.Language).HasMaxLength(5);
            e.Property(s => s.LogoPath).HasMaxLength(500);
            e.Property(s => s.LowStockThreshold).HasColumnType("TEXT");
            e.Property(s => s.VatRatesCsv).IsRequired().HasMaxLength(100);
            e.Ignore(s => s.VatRates);
        });

        b.Entity<AccountPayment>(e =>
        {
            e.HasIndex(p => p.ConfirmationId).IsUnique();
            e.Property(p => p.Amount).HasColumnType("TEXT");
            e.Property(p => p.Method).HasMaxLength(20);
            e.Property(p => p.Reference).HasMaxLength(150);
            e.Property(p => p.Operator).HasMaxLength(100);
            e.HasOne<Sale>().WithMany().HasForeignKey(p => p.SaleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Purchase>().WithMany().HasForeignKey(p => p.PurchaseId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<SerializedUnit>(e =>
        {
            e.Property(u => u.Serial).HasMaxLength(64);
            e.Property(u => u.Imei1).HasMaxLength(15);
            e.Property(u => u.Imei2).HasMaxLength(15);
            e.Property(u => u.SaleId).IsConcurrencyToken();
            e.HasIndex(u => u.Serial).IsUnique();
            e.HasIndex(u => u.Imei1).IsUnique();
            e.HasIndex(u => u.Imei2).IsUnique();
            e.HasOne<Product>().WithMany().HasForeignKey(u => u.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Purchase>().WithMany().HasForeignKey(u => u.PurchaseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Sale>().WithMany().HasForeignKey(u => u.SaleId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
