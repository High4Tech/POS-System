using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Sqlite.Design.Internal;
using Microsoft.Extensions.DependencyInjection;
using SupermarketPos.Infrastructure.Persistence;
using SupermarketPos.Infrastructure.Commercial;
using Microsoft.Data.Sqlite;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using QuestPDF.Fluent;

if(args.Length==2&&args[0]=="query-plans")
{
    var database=Path.Combine(Path.GetFullPath(args[1]),"pos.db");
    await using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=database,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
    await connection.OpenAsync();
    var queries=new (string Name,string Sql)[]
    {
        ("Product barcode","SELECT Id FROM Products WHERE Barcode='00000000'"),
        ("Product name","SELECT Id FROM Products WHERE instr(lower(Name),'sample')>0"),
        ("Product category","SELECT Id FROM Products WHERE CategoryId=1"),
        ("Receipt number","SELECT Id FROM Sales WHERE ReceiptNo='2026-000001'"),
        ("Invoice date window","SELECT Id FROM Sales WHERE CreatedAt>='2026-01-01' AND CreatedAt<'2026-12-31' ORDER BY CreatedAt DESC LIMIT 101"),
        ("Customer history/frequency","SELECT Id FROM Sales WHERE CustomerAccountKey='phone:03000000000' ORDER BY CreatedAt DESC, Id DESC LIMIT 500"),
        ("Product sale history","SELECT SaleId FROM SaleLines WHERE ProductId=1"),
        ("Posted returns","SELECT Id FROM SaleReturns WHERE SaleId=1 AND Status='Posted'"),
        ("Stock movements","SELECT Id FROM StockMovements WHERE ProductId=1 ORDER BY OccurredAt DESC"),
        ("Product batch","SELECT Id FROM InventoryBatches WHERE ProductId=1 AND Lot='fixture'"),
        ("Expiry across products","SELECT Id FROM InventoryBatches WHERE ExpiryDate<'2026-12-31'")
    };
    foreach(var (name,sql) in queries)
    {
        using var command=connection.CreateCommand();
        command.CommandText="EXPLAIN QUERY PLAN "+sql;
        await using var rows=await command.ExecuteReaderAsync();
        while(await rows.ReadAsync()) Console.WriteLine($"{name}: {rows.GetString(3)}");
    }
    return;
}

if(args.Length==2&&args[0]=="verify-local-build")
{
    using var file=File.OpenRead(args[1]);using var pe=new PEReader(file);var reader=pe.GetMetadataReader();var definition=reader.GetAssemblyDefinition();
    var values=new Dictionary<string,string?>();
    foreach(var handle in definition.GetCustomAttributes())
    {
        var attribute=reader.GetCustomAttribute(handle);if(attribute.Constructor.Kind!=HandleKind.MemberReference)continue;
        var member=reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);if(member.Parent.Kind!=HandleKind.TypeReference)continue;
        var type=reader.GetTypeReference((TypeReferenceHandle)member.Parent);if(reader.GetString(type.Name)!="AssemblyMetadataAttribute")continue;
        var blob=reader.GetBlobReader(attribute.Value);if(blob.ReadUInt16()!=1)continue;values[blob.ReadSerializedString()!]=blob.ReadSerializedString();
    }
    if(!values.TryGetValue("CommercialBuild",out var mode)||!bool.TryParse(mode,out var commercial)||commercial||!string.IsNullOrEmpty(values.GetValueOrDefault("PublisherApi"))||!string.IsNullOrEmpty(values.GetValueOrDefault("PublisherPublicKey")))throw new Exception("Not the intended local development build");
    if(values.GetValueOrDefault("BuildConfiguration") != "Release" ||
        !DateTimeOffset.TryParse(values.GetValueOrDefault("BuildTimestampUtc"),out _) ||
        values.GetValueOrDefault("BuildSourceFingerprint") is not { Length:64 } fingerprint || !fingerprint.All(Uri.IsHexDigit))
        throw new Exception("A verified Release build needs a UTC build timestamp and SHA-256 source fingerprint.");
    Console.WriteLine($"Verified local development build {definition.Version}: no embedded publisher endpoint or activation requirement.");return;
}

if(args.Length==2&&args[0]=="receipt-check")
{
    var folder=Path.GetFullPath(args[1]);await using var read=new PosDbContext(new DbContextOptionsBuilder<PosDbContext>().UseSqlite(new SqliteConnectionStringBuilder{DataSource=Path.Combine(folder,"pos.db"),Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString()).Options);
    var sale=await read.Sales.AsNoTracking().Include(s=>s.Lines).OrderBy(s=>s.Id).FirstAsync();var settings=await read.Settings.AsNoTracking().SingleAsync();var output=Directory.CreateDirectory(Path.Combine(folder,"receipt-checks")).FullName;
    QuestPDF.Settings.License=QuestPDF.Infrastructure.LicenseType.Community;
    using(var bitmap=new SkiaSharp.SKBitmap(60,40)){bitmap.Erase(SkiaSharp.SKColors.Red);using var data=bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);File.WriteAllBytes(Path.Combine(output,"test-logo.png"),data.ToArray());}
    foreach(var lang in new[]{"en","ur"})foreach(var paper in new[]{"58","80"})
    {
        var profile=new SupermarketPos.Domain.Business.BusinessProfile{StoreName="QA pharmacy",Address="Test street",InvoiceLanguage=lang,Language=lang,Paper=paper,Phone="03000000000",Phone2="03110000000",ReceiptNotes=[new(){Text=lang=="ur"?"دوائی استعمال کرنے سے پہلے ہدایات پڑھیں۔":"Read the medicine instructions before use.",Enabled=true}]};
        var doc=new SupermarketPos.Infrastructure.Documents.ReceiptDocument(sale,settings,Path.Combine(output,"test-logo.png"),profile);doc.GeneratePdf(Path.Combine(output,$"receipt-{lang}-{paper}.pdf"));var pages=doc.GenerateImages(new QuestPDF.Infrastructure.ImageGenerationSettings{RasterDpi=144}).ToArray();if(pages.Length!=1)throw new Exception("Unexpected thermal receipt pagination");File.WriteAllBytes(Path.Combine(output,$"receipt-{lang}-{paper}.png"),pages[0]);
        using var rendered=SkiaSharp.SKBitmap.Decode(pages[0]);if(Math.Abs(rendered.Width-int.Parse(paper)/25.4*144)>2)throw new Exception("Receipt width differs from chosen paper");foreach(var pixel in rendered.Pixels)if(Math.Abs(pixel.Red-pixel.Green)>2||Math.Abs(pixel.Red-pixel.Blue)>2)throw new Exception("Receipt is not monochrome");
    }
    var rack=new SupermarketPos.Domain.Entities.StockLocation("QA rack");foreach(var qr in new[]{false,true}){var doc=new SupermarketPos.Infrastructure.Documents.LocationLabelDocument([rack],"ur",qr);doc.GeneratePdf(Path.Combine(output,qr?"rack-qr.pdf":"rack-code128.pdf"));File.WriteAllBytes(Path.Combine(output,qr?"rack-qr.png":"rack-code128.png"),doc.GenerateImages(new QuestPDF.Infrastructure.ImageGenerationSettings{RasterDpi=144}).First());}
    Console.WriteLine("PASS: 58/80 mm English/Urdu receipts, grayscale logo and text, and both Urdu rack label formats rendered. Physical printer output remains untested.");return;
}
if(args.Length==2&&args[0]=="upgrade-check")
{
    var file=Path.Combine(Path.GetFullPath(args[1]),"pos.db");var columns=new Dictionary<string,string[]>();var original=new Dictionary<string,string>();
    async Task<string> Fingerprint(string table,string[] names)
    {
        await using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=file,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());await connection.OpenAsync();
        using var command=connection.CreateCommand();command.CommandText="SELECT "+string.Join(",",names.Select(n=>"\""+n.Replace("\"","\"\"")+"\""))+" FROM \""+table.Replace("\"","\"\"")+"\" ORDER BY "+string.Join(",",Enumerable.Range(1,names.Length));
        var rows=new List<object[]>();using var reader=await command.ExecuteReaderAsync();while(await reader.ReadAsync()){var values=new object[reader.FieldCount];reader.GetValues(values);rows.Add(values.Select(v=>v is DBNull?null!:v).ToArray());}
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(rows)));
    }
    await using(var read=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=file,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString()))
    {
        await read.OpenAsync();using var tables=read.CreateCommand();tables.CommandText="SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name!='__EFMigrationsHistory' ORDER BY name";var names=new List<string>();using(var rows=await tables.ExecuteReaderAsync())while(await rows.ReadAsync())names.Add(rows.GetString(0));
        foreach(var table in names){using var command=read.CreateCommand();command.CommandText="PRAGMA table_info(\""+table.Replace("\"","\"\"")+"\")";var fields=new List<string>();using(var rows=await command.ExecuteReaderAsync())while(await rows.ReadAsync())fields.Add(rows.GetString(1));columns[table]=fields.ToArray();}
    }
    foreach(var (table,names) in columns)original[table]=await Fingerprint(table,names);
    await using(var upgraded=new PosDbContext(new DbContextOptionsBuilder<PosDbContext>().UseSqlite("Data Source="+file).Options)){await upgraded.Database.MigrateAsync();if(upgraded.Database.HasPendingModelChanges())throw new Exception("Snapshot differs from schema");}
    await DataBackup.VerifyAsync(file);
    foreach(var (table,names) in columns)if(original[table]!=await Fingerprint(table,names))throw new Exception("Existing data changed during migration: "+table);
    Console.WriteLine($"PASS: schema 14; all original columns/rows preserved in {columns.Count} existing tables; SQLite integrity verified.");return;
}
if(args.Length==3&&args[0]=="restore-check")
{
    var target=Path.GetFullPath(args[2]);Directory.CreateDirectory(target);var protectedStore=new ProtectedLocalStore(Path.Combine(target,"Protected"));
    await DataBackup.StageRestoreAsync(Path.GetFullPath(args[1]),target,protectedStore);await DataBackup.ApplyPendingRestoreAsync(target,protectedStore);
    await DataBackup.VerifyAsync(Path.Combine(target,"pos.db"));Console.WriteLine("PASS: archive verified, staged and restored using the application's restore service.");return;
}
if(args.Length==3&&args[0]=="backup")
{
    Console.WriteLine(await DataBackup.CreateAsync(Path.GetFullPath(args[1]),Path.GetFullPath(args[2])));return;
}
if(args.Length==3&&args[0]=="switch-tech-retail")
{
    var folder=Path.GetFullPath(args[1]);
    var database=Path.Combine(folder,"pos.db");
    if(!File.Exists(database))throw new InvalidOperationException("Existing POS database is required.");
    var archive=await DataBackup.CreateAsync(folder,Path.GetFullPath(args[2]));
    using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=database,Pooling=false}.ToString());
    await connection.OpenAsync();
    using var transaction=connection.BeginTransaction();
    using var read=connection.CreateCommand();read.Transaction=transaction;
    read.CommandText="SELECT ProfileJson FROM BusinessConfigurations";
    var original=(string?)await read.ExecuteScalarAsync()??throw new InvalidOperationException("Business configuration is missing.");
    var profile=System.Text.Json.Nodes.JsonNode.Parse(original)!.AsObject();
    var before=profile["BusinessType"]?.GetValue<string>();
    profile["BusinessType"]="electronics";profile["MedicineTemplate"]="none";
    using var update=connection.CreateCommand();update.Transaction=transaction;
    update.CommandText="UPDATE BusinessConfigurations SET ProfileJson=$profile,Revision=Revision+1";
    update.Parameters.AddWithValue("$profile",profile.ToJsonString());
    if(await update.ExecuteNonQueryAsync()!=1)throw new InvalidOperationException("Expected exactly one business configuration.");
    await transaction.CommitAsync();
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{Before=before,After="electronics",Backup=archive}));
    return;
}
if(args.Length==2&&args[0]=="setup-status")
{
    using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(Path.GetFullPath(args[1]),"pos.db"),Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());await connection.OpenAsync();
    using var command=connection.CreateCommand();command.CommandText="SELECT Completed,Step,ProfileJson FROM BusinessConfigurations";using var row=await command.ExecuteReaderAsync();
    while(await row.ReadAsync()){using var profile=System.Text.Json.JsonDocument.Parse(row.GetString(2));Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{Completed=row.GetBoolean(0),Step=row.GetInt32(1),Language=profile.RootElement.GetProperty("Language").GetString(),BusinessType=profile.RootElement.TryGetProperty("BusinessType",out var type)?type.GetString():null}));}return;
}
if(args.Length==2&&args[0]=="data-fingerprint")
{
    using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(Path.GetFullPath(args[1]),"pos.db"),Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());await connection.OpenAsync();
    var hashes=new SortedDictionary<string,string>();
    foreach(var table in new[]{"Products","Categories","Sales","SaleLines","Suppliers","Purchases","PurchaseLines","InventoryBatches","SerializedUnits","AccountPayments","ServiceProviders","MobileServicePackages","ServiceTransactions","ServicePostings"})
    {
        using var command=connection.CreateCommand();command.CommandText=table switch
        {
            "ServiceProviders"=>"SELECT Id,Name,AllowedTypes,OpeningFloat,FeePolicy,CommissionPolicy,IsActive,Revision FROM ServiceProviders ORDER BY Id",
            "MobileServicePackages"=>"SELECT Id,ProviderId,Name,Kind,Price,ProviderCost,Commission,Validity,IsActive FROM MobileServicePackages ORDER BY Id",
            "ServiceTransactions"=>"SELECT Id,ConfirmationId,ProviderId,PackageId,ReversesId,Type,Status,Reference,CustomerPhone,Operator,OccurredAt,StoreName,ProviderName,PackageName,Principal,CustomerFee,ProviderFee,Commission,ProviderCost,Collected,FloatChange,Revision FROM ServiceTransactions ORDER BY Id",
            _=>$"SELECT * FROM \"{table}\" ORDER BY Id"
        };
        var rows=new List<object[]>();using var reader=await command.ExecuteReaderAsync();while(await reader.ReadAsync()){var values=new object[reader.FieldCount];reader.GetValues(values);rows.Add(values.Select(v=>v is DBNull?null!:v).ToArray());}
        hashes[table]=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(rows)));
    }
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(hashes));return;
}
if(args.Length==2&&args[0]=="inspect")
{
    using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(Path.GetFullPath(args[1]),"pos.db"),Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());await connection.OpenAsync();
    var counts=new SortedDictionary<string,long>();
    foreach(var table in new[]{"Products","Categories","Users","Sales","SaleLines","Suppliers","Purchases","PurchaseLines","InventoryBatches","SerializedUnits","AccountPayments","ServiceProviders","MobileServicePackages","ServiceTransactions","ServicePostings"})
    {
        using var exists=connection.CreateCommand();exists.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";exists.Parameters.AddWithValue("$name",table);
        if(Convert.ToInt64(await exists.ExecuteScalarAsync())==0)continue;
        using var count=connection.CreateCommand();count.CommandText=$"SELECT COUNT(*) FROM \"{table}\"";counts[table]=Convert.ToInt64(await count.ExecuteScalarAsync());
    }
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(counts));return;
}
if(args.Length==2&&args[0]=="verify-retail-stock")
{
    using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(Path.GetFullPath(args[1]),"pos.db"),Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());await connection.OpenAsync();
    var verified=0;
    foreach(var sql in new[]{"SELECT Unit,StockQuantity,BaseStockQuantity FROM Products","SELECT BaseUnit,Quantity,BaseQuantity FROM InventoryBatches"})
    {
        using var command=connection.CreateCommand();command.CommandText=sql;using var rows=await command.ExecuteReaderAsync();
        while(await rows.ReadAsync())
        {
            var quantity=decimal.Parse(rows.GetString(1),System.Globalization.CultureInfo.InvariantCulture);
            var expected=SupermarketPos.Domain.Pricing.MeasuredGoods.BaseQuantity(quantity,(SupermarketPos.Domain.Common.Unit)rows.GetInt32(0));
            if(expected!=rows.GetInt64(2))throw new Exception("Converted stock differs from the pre-upgrade legacy balance. Check whether new trading activity has occurred.");verified++;
        }
    }
    Console.WriteLine($"Verified exact integer conversion of {verified} product/batch balances.");return;
}

using var db = new PosDbContext(new DbContextOptionsBuilder<PosDbContext>().UseSqlite("Data Source=:memory:").Options);
var services = new ServiceCollection().AddEntityFrameworkDesignTimeServices().AddDbContextDesignTimeServices(db);
new SqliteDesignTimeServices().ConfigureDesignTimeServices(services);
using var provider = services.BuildServiceProvider();
var generator = provider.GetRequiredService<IMigrationsCodeGenerator>();
var code = generator.GenerateSnapshot("SupermarketPos.Infrastructure.Persistence.Migrations",typeof(PosDbContext),"PosDbContextModelSnapshot",db.GetService<IDesignTimeModel>().Model);
File.WriteAllText(args[0],code);
Console.WriteLine("Generated current EF model snapshot.");
