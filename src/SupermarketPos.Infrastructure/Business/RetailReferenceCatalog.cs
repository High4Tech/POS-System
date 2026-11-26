using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SupermarketPos.Infrastructure.Persistence;
namespace SupermarketPos.Infrastructure.Business;

public sealed record RetailReference(string Key,string Kind,string Name,string Category,string Brand,string Model,string Details,string SourcePrice,string SourceUrl,string State,string Logo="")
{
    public string Caption=>Name+" · "+Category;
}

public sealed class RetailReferenceCatalog(PosDbContext db)
{
    private readonly string _folder=Path.Combine(db.Database.GetDbConnection().DataSource is ":memory:" or ""?Path.GetTempPath():Path.GetDirectoryName(db.Database.GetDbConnection().DataSource)!,"ReferenceCatalogs");
    private SqliteConnection Open(string name)
    {
        using var source=typeof(RetailReferenceCatalog).Assembly.GetManifestResourceStream("SupermarketPos.Catalogs.Tech."+name+".sqlite")??throw new InvalidOperationException("Retail reference unavailable.");
        using var bytes=new MemoryStream();source.CopyTo(bytes);var data=bytes.ToArray();
        var folder=Directory.CreateDirectory(_folder).FullName;var path=Path.Combine(folder,name+"-"+Convert.ToHexString(SHA256.HashData(data))[..16]+".sqlite");
        if(!File.Exists(path)){var stage=path+"."+Guid.NewGuid().ToString("N");File.WriteAllBytes(stage,data);try{File.Move(stage,path,false);}catch(IOException)when(File.Exists(path)){File.Delete(stage);}}
        var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());connection.Open();return connection;
    }
    public Task<IReadOnlyList<RetailReference>> SearchAsync(string term,string kind="model",int limit=100,CancellationToken ct=default)=>Task.Run<IReadOnlyList<RetailReference>>(()=>
    {
        using var connection=Open(kind=="model"?"Hardware":"Finance");using var command=connection.CreateCommand();
        command.CommandText=kind switch
        {
            "model"=>"""
                SELECT 'model:'||m.id,'model',b.name||' '||m.model_name,c.name,b.name,m.model_name,
                m.specs_json||' · '||COALESCE((SELECT GROUP_CONCAT(variant_label,' / ') FROM variants WHERE model_id=m.id),'')||' · '||COALESCE(c.parent_name,''),
                COALESCE(CAST((SELECT price_pkr FROM market_price_refs WHERE model_id=m.id ORDER BY observed_date DESC,id DESC LIMIT 1) AS TEXT),''),
                COALESCE(m.source_url,''),m.verification_status,COALESCE(b.logo_path,'')
                FROM models m JOIN brands b ON b.id=m.brand_id JOIN categories c ON c.id=m.category_id
                WHERE lower(b.name||' '||m.model_name||' '||c.name) LIKE $term ESCAPE '\' ORDER BY b.name,m.model_name LIMIT $limit
                """,
            "package"=>"""
                SELECT 'package:'||p.id,'package',p.name,COALESCE(p.category,''),o.name,p.operator_id,
                COALESCE(p.validity,'')||' · '||COALESCE(p.base_data_gb,'')||' GB · '||COALESCE(p.social_data_gb,'')||' Social GB · '||COALESCE(p.onnet_minutes,0)||' on-net min · '||COALESCE(p.offnet_minutes,0)||' off-net min · '||COALESCE(p.sms,0)||' SMS · '||COALESCE(p.eligibility,'')||' · '||COALESCE((SELECT GROUP_CONCAT(region_label||' '||COALESCE(eligibility_detail,''),' / ') FROM package_region_rules WHERE package_id=p.id),'')||' · '||COALESCE(p.remarks,''),
                COALESCE(p.price_pkr,''),p.source_url,p.verification_state,''
                FROM mobile_packages p JOIN mobile_operators o ON o.id=p.operator_id
                WHERE lower(p.name||' '||o.name||' '||COALESCE(p.category,'')) LIKE $term ESCAPE '\' ORDER BY o.name,p.name LIMIT $limit
                """,
            _=>"""
                SELECT * FROM (
                SELECT 'provider:'||id,'provider',display_name,provider_type,'','',COALESCE(notes,''),'',COALESCE(source_url,''),COALESCE(regulatory_status,''),'' FROM providers
                UNION ALL SELECT 'brand:'||id,'provider',brand_name,brand_type,'','',COALESCE(notes,''),'',COALESCE(website,''),'directory','' FROM provider_brands
                UNION ALL SELECT 'network:'||id,'network',name,operator_type,'',id,COALESCE(notes,''),'',COALESCE(source_url,''),'directory','' FROM mobile_operators
                ) WHERE lower(column3) LIKE $term ESCAPE '\' LIMIT $limit
                """
        };
        // Compound SELECT column names come from the first SELECT's explicit aliases.
        if(kind is not ("model" or "package"))command.CommandText=command.CommandText.Replace("SELECT 'provider:'||id,'provider',display_name,provider_type", "SELECT 'provider:'||id AS key,'provider' AS kind,display_name AS column3,provider_type");
        command.Parameters.AddWithValue("$term","%"+term.Trim().ToLowerInvariant().Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_")+"%");
        command.Parameters.AddWithValue("$limit",Math.Clamp(limit,1,1000));
        using var rows=command.ExecuteReader();var result=new List<RetailReference>();while(rows.Read())
        {ct.ThrowIfCancellationRequested();string S(int i)=>rows.IsDBNull(i)?"":Convert.ToString(rows.GetValue(i),CultureInfo.InvariantCulture)??"";result.Add(new(S(0),S(1),S(2),S(3),S(4),S(5),S(6),S(7),S(8),S(9),S(10)));}
        return kind is "model" or "package"?result:result.DistinctBy(r=>r.Name,StringComparer.OrdinalIgnoreCase).ToArray();
    },ct);
    public async Task<RetailReference?> FindAsync(string key,CancellationToken ct=default)=>(await SearchAsync("",key.StartsWith("model:")?"model":key.StartsWith("package:")?"package":"provider",1000,ct)).SingleOrDefault(r=>r.Key==key);
    public Task<IReadOnlyList<(string Name,string Parent)>> CategoriesAsync()=>Task.Run<IReadOnlyList<(string,string)>>(()=>
    {using var c=Open("Hardware");using var q=c.CreateCommand();q.CommandText="SELECT name,COALESCE(parent_name,'') FROM categories ORDER BY name";using var rows=q.ExecuteReader();var list=new List<(string,string)>();while(rows.Read())list.Add((rows.GetString(0),rows.GetString(1)));return list;});
    public Task<IReadOnlyList<string>> BrandsAsync()=>Task.Run<IReadOnlyList<string>>(()=>
    {using var c=Open("Hardware");using var q=c.CreateCommand();q.CommandText="SELECT name FROM brands ORDER BY name";using var rows=q.ExecuteReader();var list=new List<string>();while(rows.Read())list.Add(rows.GetString(0));return list;});
    public async Task<string> PackageValidityAsync(string key)
    {using var c=Open("Finance");using var q=c.CreateCommand();q.CommandText="SELECT COALESCE(validity,'') FROM mobile_packages WHERE id=$id";q.Parameters.AddWithValue("$id",int.Parse(key.Split(':')[1],CultureInfo.InvariantCulture));return (string?)await q.ExecuteScalarAsync()??"";}
}
