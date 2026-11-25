using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Application.Services;
using SupermarketPos.Domain.Business;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Domain.Pricing;
using SupermarketPos.Infrastructure;
using SupermarketPos.Infrastructure.Business;
using SupermarketPos.Infrastructure.Commercial;
using SupermarketPos.Infrastructure.Persistence;

namespace SupermarketPos.Tests;
public sealed class CommercialStorageTests
{
    private static string Root()=>Directory.CreateDirectory(Path.Combine(Path.GetTempPath(),"pos-commercial-check-"+Guid.NewGuid().ToString("N"))).FullName;
    private static ServiceProvider Provider(string folder)=>new ServiceCollection().AddPos(Path.Combine(folder,"pos.db")).BuildServiceProvider();
    [Fact]
    public async Task BackupRestorePreservesDatabaseMediaAndPreferencesAndRejectsTraversal()
    {
        var root=Root();
        try
        {
            using(var provider=Provider(root))using(var scope=provider.CreateScope())
            {
                await DependencyInjection.InitializeDatabaseAsync(scope.ServiceProvider,createDefaultUser:false);
                var db=scope.ServiceProvider.GetRequiredService<PosDbContext>();
                Assert.Empty(db.Users);db.Categories.Add(new Category("Preserved category"));await db.SaveChangesAsync();
            }
            Directory.CreateDirectory(Path.Combine(root,"Media"));File.WriteAllText(Path.Combine(root,"Media","sample.png"),"image copy");
            File.WriteAllText(Path.Combine(root,"desktop-text-size.txt"),"175");
            var archive=await DataBackup.CreateAsync(root,Path.Combine(root,"Backups","test.zip"));
            using(var provider=Provider(root))using(var scope=provider.CreateScope())
            {var db=scope.ServiceProvider.GetRequiredService<PosDbContext>();db.Categories.Add(new Category("Later category"));await db.SaveChangesAsync();}
            var protectedStore=new ProtectedLocalStore(Path.Combine(root,"Protected"));
            await DataBackup.StageRestoreAsync(archive,root,protectedStore);await DataBackup.ApplyPendingRestoreAsync(root,protectedStore);
            using(var provider=Provider(root))using(var scope=provider.CreateScope())
            {var db=scope.ServiceProvider.GetRequiredService<PosDbContext>();Assert.Contains(db.Categories,c=>c.Name=="Preserved category");Assert.DoesNotContain(db.Categories,c=>c.Name=="Later category");}
            Assert.Equal("image copy",File.ReadAllText(Path.Combine(root,"Media","sample.png")));Assert.Equal("175",File.ReadAllText(Path.Combine(root,"desktop-text-size.txt")));
            Assert.Equal("",protectedStore.Read("pending-restore"));Assert.Contains(Directory.GetFiles(Path.Combine(root,"Backups")),s=>Path.GetFileName(s).StartsWith("Before-restore-"));
            var malicious=Path.Combine(root,"evil.zip");using(var zip=ZipFile.Open(malicious,ZipArchiveMode.Create))using(var writer=new StreamWriter(zip.CreateEntry("../escape.txt").Open()))writer.Write("escape");
            await Assert.ThrowsAsync<CommercialException>(()=>DataBackup.StageRestoreAsync(malicious,root,protectedStore));Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(root)!,"escape.txt")));
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
    }
    [Fact]
    public async Task FailedAdditiveMigrationRestoresPriorCompatibleDatabase()
    {
        var root=Root();
        try
        {
            using(var provider=Provider(root))using(var scope=provider.CreateScope())
            {
                var db=scope.ServiceProvider.GetRequiredService<PosDbContext>();
                await db.GetService<IMigrator>().MigrateAsync("20261119010000_AccountsAndDevices");
                await db.Database.ExecuteSqlRawAsync("CREATE TABLE BusinessConfigurations (Id INTEGER PRIMARY KEY)"); // simulate a conflicting partial external change
            }
            using(var provider=Provider(root))using(var scope=provider.CreateScope())
            {
                var error=await Assert.ThrowsAsync<BusinessValidationException>(()=>DependencyInjection.InitializeDatabaseAsync(scope.ServiceProvider,createDefaultUser:false));
                Assert.Equal("Setup_MigrationRecovered",error.Message);
            }
            using(var provider=Provider(root))using(var scope=provider.CreateScope())
            {
                var db=scope.ServiceProvider.GetRequiredService<PosDbContext>();
                Assert.DoesNotContain("20261119140000_BusinessOnboarding",await db.Database.GetAppliedMigrationsAsync());
                Assert.Contains("20261119010000_AccountsAndDevices",await db.Database.GetAppliedMigrationsAsync());
            }
            await DataBackup.VerifyAsync(Path.Combine(root,"pos.db"));Assert.NotEmpty(Directory.GetFiles(Path.Combine(root,"Backups"),"Failed-schema-upgrade-*"));
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
    }
    [Fact]
    public void WindowsProtectedOfflineCertificateSurvivesClientRestartAndRejectsAnotherDevice()
    {
        var root=Root();
        try
        {
            using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var claims=new LicenseClaims(1,SignedDocuments.Product,"test","seat","pc-a","retail",1,DateTimeOffset.UtcNow.AddYears(-1),DateTimeOffset.UtcNow.AddYears(-2));
            var payload=JsonSerializer.SerializeToUtf8Bytes(claims,SignedDocuments.Json);
            var certificate=new SignedEnvelope(Convert.ToBase64String(payload),Convert.ToBase64String(key.SignData(payload,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
            var options=new PublisherOptions("https://unavailable.invalid",key.ExportSubjectPublicKeyInfoPem(),true,PublishedAt:DateTimeOffset.UtcNow.AddYears(-2));
            new LicenseClient(options,new ProtectedLocalStore(root),"pc-a").Import(JsonSerializer.Serialize(certificate,SignedDocuments.Json));
            Assert.Equal(claims,new LicenseClient(options,new ProtectedLocalStore(root),"pc-a").Current);
            Assert.Throws<CommercialException>(()=>new LicenseClient(options,new ProtectedLocalStore(root),"pc-b").Current);
            Assert.DoesNotContain(certificate.Payload,File.ReadAllText(Path.Combine(root,"activation.protected")));
            Assert.Equal(64,ProtectedLocalStore.DeviceHash().Length);
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public async Task RegisteredSaleStoresImmutableMappingWithoutInventingFbrNumber()
    {
        using var test=new TestDb();var db=test.Context;
        db.Settings.Add(new AppSettings("Tax shop","Address","en",5,[.18m]));var hash=new BCryptPasswordHasher();
        var auth=new AuthService(new UserRepository(db),hash);var setup=new BusinessSetup(db,new UnitOfWork(db),auth,hash);
        await db.SaveChangesAsync();
        await setup.CompleteAsync(new(){StoreName="Tax shop",Registered=true,LegalName="Legal Shop",City="Lahore",Address="Address",SalesTaxRegistered=true,Strn="1234567890123",FbrRoute="digital",FbrProvider="mock",FbrStatus="sandbox",InvoicePrefix="LHR"},"owner","StrongPassword123!");
        await auth.LoginAsync("owner","StrongPassword123!");var category=new Category("Test");db.Categories.Add(category);await db.SaveChangesAsync();
        var product=new Product("1234","Phone",118,.18m,Unit.Piece,2,category.Id);db.Products.Add(product);await db.SaveChangesAsync();
        var cart=new Cart();cart.AddOrIncrement(product,1);
        var sale=await new SaleService(new ProductRepository(db),new SaleRepository(db),new ReceiptNumberGenerator(db),new UnitOfWork(db),compliance:new FbrCompliance(db)).CheckoutAsync(cart,new CashPayment(118,118),auth.CurrentUser!);
        var mapping=await db.FiscalSubmissions.SingleAsync();Assert.Equal("sandbox-validated",mapping.Status);Assert.Null(mapping.AuthorityInvoiceNumber);Assert.StartsWith("LHR-",sale.ReceiptNo);
        using var json=JsonDocument.Parse(mapping.InvoiceJson);Assert.Equal("Legal Shop",json.RootElement.GetProperty("businessProfile").GetProperty("LegalName").GetString());
        Assert.Equal(18,json.RootElement.GetProperty("tax").GetDecimal());
        await setup.CompleteAsync((await setup.LoadAsync()).Profile with{LegalName="Changed after sale"},null,null);
        Assert.Contains("Legal Shop",(await db.FiscalSubmissions.SingleAsync()).InvoiceJson);Assert.DoesNotContain("Changed after sale",mapping.InvoiceJson);
    }
}
