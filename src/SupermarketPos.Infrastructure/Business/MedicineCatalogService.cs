using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Application.Services;
using SupermarketPos.Domain.Business;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Infrastructure.Persistence;

namespace SupermarketPos.Infrastructure.Business;

public sealed class MedicineCatalogService(PosDbContext db,IAuthService auth)
{
    public const string TemplateId="pakistan-drap";
    public const string Header="Sr. No,Registration Number,Product Name,Generic Name,Company Name";
    public static Stream? OpenBuiltin()=>typeof(MedicineCatalogService).Assembly.GetManifestResourceStream("SupermarketPos.Catalogs.DRAP.csv");
    public static bool BuiltinAvailable {get{using var s=OpenBuiltin();return s is not null;}}
    public Task<CatalogImport?> StatusAsync(CancellationToken ct=default)=>db.CatalogImports.AsNoTracking().SingleOrDefaultAsync(i=>i.TemplateId==TemplateId,ct);

    public async Task EnsureSelectedAsync(CancellationToken ct=default)
    {
        var row=await db.BusinessConfigurations.AsNoTracking().SingleOrDefaultAsync(ct);
        var profile=row is null?null:JsonSerializer.Deserialize<BusinessProfile>(row.ProfileJson);
        if(profile?.IsPharmacy==true&&profile.MedicineTemplate==TemplateId&&await StatusAsync(ct) is null&&BuiltinAvailable)
            await ImportBuiltinCoreAsync(ct);
    }
    public Task<int> ImportBuiltinAsync(CancellationToken ct=default){auth.RequireAdmin();return ImportBuiltinCoreAsync(ct);}
    private async Task<int> ImportBuiltinCoreAsync(CancellationToken ct)
    {
        using var source=OpenBuiltin()??throw new DomainException("Pharmacy_CatalogUnavailable");
        using var bytes=new MemoryStream();await source.CopyToAsync(bytes,ct);
        return await ImportBytesAsync(bytes.ToArray(),"DRAP_Complete_Medicine_Database.csv",ct);
    }
    public Task<int> ImportFileAsync(byte[] csv,string fileName,CancellationToken ct=default)
    {
        auth.RequireAdmin();if(csv.Length>25_000_000)throw new DomainException("Pharmacy_InvalidCatalog");
        return ImportBytesAsync(csv,Path.GetFileName(fileName),ct);
    }
    private async Task<int> ImportBytesAsync(byte[] csv,string fileName,CancellationToken ct)
    {
        var hash=Convert.ToHexString(SHA256.HashData(csv));
        var saved=await StatusAsync(ct);if(saved?.ContentHash==hash)return saved.RowCount;
        var connection=(SqliteConnection)db.Database.GetDbConnection();
        // A separate file connection keeps a large reference import off the UI thread
        // and away from the store's scoped transaction/context. In-memory tests share theirs.
        var options=new SqliteConnectionStringBuilder(connection.ConnectionString);
        if(options.DataSource==":memory:"||options.Mode==SqliteOpenMode.Memory)return Import(connection,csv,hash,fileName,ct);
        var connectionString=new SqliteConnectionStringBuilder(connection.ConnectionString){Mode=SqliteOpenMode.ReadWrite}.ToString();
        return await Task.Run(()=>{using var own=new SqliteConnection(connectionString);own.Open();return Import(own,csv,hash,fileName,ct);},ct);
    }
    private static int Import(SqliteConnection connection,byte[] bytes,string hash,string fileName,CancellationToken ct)
    {
        using var reader=new StreamReader(new MemoryStream(bytes),Encoding.UTF8,true);
        var rows=BulkRecordService.ReadCsv(reader,Header);
        if(rows.Count>100_000)throw new DomainException("Pharmacy_InvalidCatalog");
        using var transaction=connection.BeginTransaction();
        using(var schema=connection.CreateCommand()){schema.Transaction=transaction;schema.CommandText=MedicineSearchSchema.Sql;schema.ExecuteNonQuery();}
        using var insert=connection.CreateCommand();insert.Transaction=transaction;
        insert.CommandText="""
            INSERT INTO MedicineReferences(SourceKey,RegistrationNumber,SourceRow,Name,GenericName,Manufacturer,Strength,Formulation,PackDescription,Barcode,SearchText)
            VALUES($key,$reg,$row,$name,$generic,$company,'','','','',$search)
            ON CONFLICT(SourceKey) DO UPDATE SET RegistrationNumber=excluded.RegistrationNumber,Name=excluded.Name,GenericName=excluded.GenericName,Manufacturer=excluded.Manufacturer,SearchText=excluded.SearchText;
            """;
        foreach(var key in new[]{"$key","$reg","$row","$name","$generic","$company","$search"})insert.Parameters.Add(key,SqliteType.Text);
        var used=new HashSet<string>(StringComparer.Ordinal);
        foreach(var row in rows)
        {
            ct.ThrowIfCancellationRequested();var f=row.Fields;
            if(f[0].Length is <1 or >20||!f[0].All(char.IsAsciiDigit)||f[1].Length is <1 or >100||f[2].Length is <1 or >500||f[3].Length>2000||f[4].Length>1000||f.Any(v=>v.Contains('\0')))throw new DomainException("Pharmacy_InvalidCatalog");
            var identity="DRAP:"+f[1]+":"+f[0];
            if(!used.Add(identity))throw new DomainException("Pharmacy_InvalidCatalog");
            insert.Parameters["$key"].Value=identity;insert.Parameters["$reg"].Value=f[1];insert.Parameters["$row"].Value=f[0];insert.Parameters["$name"].Value=f[2];insert.Parameters["$generic"].Value=f[3];insert.Parameters["$company"].Value=f[4];insert.Parameters["$search"].Value=Normalize(f[2]+" "+f[3]+" "+f[4]);insert.ExecuteNonQuery();
        }
        using var manifest=connection.CreateCommand();manifest.Transaction=transaction;
        manifest.CommandText="""
            INSERT INTO CatalogImports(TemplateId,ContentHash,RowCount,ImportedAt,SourceFile,RedistributionStatus) VALUES($id,$hash,$count,$at,$file,'unverified')
            ON CONFLICT(TemplateId) DO UPDATE SET ContentHash=excluded.ContentHash,RowCount=excluded.RowCount,ImportedAt=excluded.ImportedAt,SourceFile=excluded.SourceFile;
            """;
        manifest.Parameters.AddWithValue("$id",TemplateId);manifest.Parameters.AddWithValue("$hash",hash);manifest.Parameters.AddWithValue("$count",rows.Count);manifest.Parameters.AddWithValue("$at",DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture));manifest.Parameters.AddWithValue("$file",fileName);manifest.ExecuteNonQuery();
        transaction.Commit();return rows.Count;
    }
    public static string Normalize(string text)=>string.Join(' ',new string(text.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Select(c=>char.IsLetterOrDigit(c)?c:' ').ToArray()).Split(' ',StringSplitOptions.RemoveEmptyEntries));

    public async Task<IReadOnlyList<MedicineMatch>> SearchAsync(string term,int limit=20,CancellationToken ct=default)
    {
        var options=new SqliteConnectionStringBuilder(db.Database.GetConnectionString());
        if(options.DataSource==":memory:"||options.Mode==SqliteOpenMode.Memory)return await SearchCoreAsync(term,limit,ct);
        await using var search=new PosDbContext(new DbContextOptionsBuilder<PosDbContext>().UseSqlite(options.ToString()).Options);
        return await new MedicineCatalogService(search,auth).SearchCoreAsync(term,limit,ct);
    }
    private async Task<IReadOnlyList<MedicineMatch>> SearchCoreAsync(string term,int limit,CancellationToken ct)
    {
        term=term.Trim();if(term.Length<1)return [];
        if(term.Length>150)term=term[..150];limit=Math.Clamp(limit,1,50);
        var normalized=Normalize(term);var ids=new List<int>();
        if(normalized.Length>0&&await db.CatalogImports.AsNoTracking().AnyAsync(ct))
        {
            var conn=db.Database.GetDbConnection();var wasClosed=conn.State==System.Data.ConnectionState.Closed;if(wasClosed)await conn.OpenAsync(ct);
            try
            {
                using var query=conn.CreateCommand();
                query.CommandText="""
                    SELECT m.Id FROM MedicineReferenceSearch JOIN MedicineReferences m ON m.Id=MedicineReferenceSearch.rowid
                    WHERE MedicineReferenceSearch MATCH $query
                    ORDER BY (lower(m.Name)=lower($name)) DESC, EXISTS(SELECT 1 FROM Products p WHERE p.MedicineReferenceId=m.Id AND p.IsActive=1) DESC,
                    (SELECT COUNT(*) FROM SaleLines sl JOIN Products p ON p.Id=sl.ProductId WHERE p.MedicineReferenceId=m.Id) DESC,
                    (m.SearchText LIKE $prefix) DESC, bm25(MedicineReferenceSearch) LIMIT $limit
                    """;
                foreach(var pair in new[]{("$query",string.Join(" AND ",normalized.Split(' ').Take(12).Select(w=>"\""+w+"\"*"))),("$name",term),("$prefix",normalized+"%")}){var p=query.CreateParameter();p.ParameterName=pair.Item1;p.Value=pair.Item2;query.Parameters.Add(p);}
                var count=query.CreateParameter();count.ParameterName="$limit";count.Value=limit;query.Parameters.Add(count);
                using var results=await query.ExecuteReaderAsync(ct);while(await results.ReadAsync(ct))ids.Add(results.GetInt32(0));
            }
            finally{if(wasClosed)await conn.CloseAsync();}
            if(ids.Count<limit)
            {
                var more=await db.MedicineReferences.AsNoTracking().Where(m=>!ids.Contains(m.Id)&&m.SearchText.Contains(normalized)).OrderBy(m=>m.Name).Select(m=>m.Id).Take(limit-ids.Count).ToListAsync(ct);ids.AddRange(more);
            }
        }
        var references=await db.MedicineReferences.AsNoTracking().Where(m=>ids.Contains(m.Id)).ToDictionaryAsync(m=>m.Id,ct);
        var lower=term.ToLowerInvariant();
        var local=await db.Products.AsNoTracking().Include(p=>p.Batches).Where(p=>p.IsActive&&(p.MedicineReferenceId.HasValue&&ids.Contains(p.MedicineReferenceId.Value)||p.Name.ToLower().Contains(lower)||p.MedicineGeneric.ToLower().Contains(lower)||p.MedicineManufacturer.ToLower().Contains(lower)||p.Barcode==term)).Take(100).ToArrayAsync(ct);
        var frequent=await db.SaleLines.AsNoTracking().Where(l=>local.Select(p=>p.Id).Contains(l.ProductId)).GroupBy(l=>l.ProductId).Select(g=>new{Id=g.Key,Count=g.Count()}).ToDictionaryAsync(g=>g.Id,g=>g.Count,ct);
        var result=new List<MedicineMatch>();
        foreach(var id in ids)
        {
            var m=references[id];var p=local.FirstOrDefault(p=>p.MedicineReferenceId==id);result.Add(Match(m,p));
        }
        result.AddRange(local.Where(p=>!p.MedicineReferenceId.HasValue||!ids.Contains(p.MedicineReferenceId.Value)).Select(p=>Match(null,p)));
        var labels=await LocationLabelsAsync(local.Select(p=>p.Id).ToArray(),ct);
        return result.OrderByDescending(m=>string.Equals(m.Name,term,StringComparison.OrdinalIgnoreCase)).ThenByDescending(m=>m.ProductId.HasValue).ThenByDescending(m=>m.ProductId.HasValue?frequent.GetValueOrDefault(m.ProductId.Value):0).ThenByDescending(m=>Normalize(m.Name).StartsWith(normalized,StringComparison.Ordinal)).ThenBy(m=>m.Name).Take(limit).Select(m=>m with{Locations=m.ProductId is{} id?labels.GetValueOrDefault(id,""):""}).ToArray();
    }
    private static MedicineMatch Match(MedicineReference? m,Product? p)=>new(m?.Id,p?.Id,p?.Name??m!.Name,p?.MedicineGeneric.Length>0?p.MedicineGeneric:m?.GenericName??"",p?.MedicineManufacturer.Length>0?p.MedicineManufacturer:m?.Manufacturer??"",p?.MedicineStrength.Length>0?p.MedicineStrength:m?.Strength??"",p?.MedicineForm.Length>0?p.MedicineForm:m?.Formulation??"",m?.PackDescription??"",p?.Barcode??m?.Barcode??"",p?.UnitPrice>0?p.UnitPrice:null,p?.StockTracked==true?p.AvailableForSale(DateOnly.FromDateTime(DateTime.Today)):null,p is null?"CatalogOnly":!p.StockTracked?p.UnitPrice>0?"StockUntracked":"NeedsSetup":p.AvailableForSale(DateOnly.FromDateTime(DateTime.Today))>0?"InStock":"OutOfStock");
    public async Task<Dictionary<int,MedicineMatch>> DescribeAsync(IReadOnlyList<Product> products,CancellationToken ct=default)
    {
        var ids=products.Where(p=>p.MedicineReferenceId.HasValue).Select(p=>p.MedicineReferenceId!.Value).ToArray();var references=await db.MedicineReferences.AsNoTracking().Where(m=>ids.Contains(m.Id)).ToDictionaryAsync(m=>m.Id,ct);var locations=await LocationLabelsAsync(products.Select(p=>p.Id).ToArray(),ct);
        return products.ToDictionary(p=>p.Id,p=>Match(p.MedicineReferenceId is{} id?references.GetValueOrDefault(id):null,p) with{Locations=locations.GetValueOrDefault(p.Id,"")});
    }
    private async Task<Dictionary<int,string>> LocationLabelsAsync(int[] ids,CancellationToken ct)
    {
        var rows=await (from stock in db.StockPlacements.AsNoTracking() join location in db.StockLocations on stock.LocationId equals location.Id join parent in db.StockLocations on location.ParentId equals (int?)parent.Id into parents from parent in parents.DefaultIfEmpty() where ids.Contains(stock.ProductId) select new{stock.ProductId,Name=parent==null?location.Name:parent.Name+" / "+location.Name,stock.BaseQuantity}).ToArrayAsync(ct);
        return rows.GroupBy(r=>r.ProductId).ToDictionary(g=>g.Key,g=>string.Join(" · ",g.Select(r=>$"{r.Name}: {r.BaseQuantity}")));
    }
}
