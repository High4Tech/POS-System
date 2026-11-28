using System.IO;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Velopack;
using Velopack.Locators;
using System.Xml.Linq;
using SupermarketPos.Infrastructure.Commercial;

namespace SupermarketPos.Wpf.Common;

public sealed class SoftwareUpdates(LicenseClient license, PosStorage storage)
{
    public const int Schema = 15;
    public Version CurrentVersion => typeof(App).Assembly.GetName().Version ?? new(1,0,0);
    public string DisplayVersion=>RetailTestEdition.Standalone?RetailUpdatePolicy.CurrentVersion:CurrentVersion.ToString(3);
    public string Channel=>RetailTestEdition.Standalone?RetailUpdatePolicy.Channel:license.Options.Channel;
    private readonly string _folder = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(storage.DatabasePath)!, "Updates")).FullName;
    public ReleaseClaims? Available { get; private set; }
    private SignedEnvelope? _envelope;
    private string? _releaseId;
    public DateTimeOffset? LastCheck { get; private set; } = ReadLastCheck(storage);
    private static DateTimeOffset? ReadLastCheck(PosStorage storage)
    {
        var file=Path.Combine(Path.GetDirectoryName(storage.DatabasePath)!,"Updates","last-check.txt");
        try {return File.Exists(file)&&DateTimeOffset.TryParse(File.ReadAllText(file),out var date)?date:null;}
        catch(IOException){return null;}
    }
    public bool Downloaded => Available is { } release && File.Exists(Path.Combine(_folder, release.FileName));
    public event Action? Changed;
    public void RecordSuccessfulStartup()
    {
        var file=Path.Combine(_folder,"history.jsonl");
        try
        {
            var last=File.Exists(file)?File.ReadLines(file).LastOrDefault():null;
            if(last is null)return;
            using var json=JsonDocument.Parse(last);
            var label=json.RootElement.GetProperty("version").GetString()??"";
            var matches=RetailTestEdition.Standalone?SemanticVersion.TryParse(label,out var release)&&SemanticVersion.TryParse(DisplayVersion,out var current)&&release==current:Version.TryParse(label,out var version)&&version==new Version(CurrentVersion.ToString(3));
            if(json.RootElement.GetProperty("status").GetString()=="install-requested"&&matches)
                File.AppendAllText(file,JsonSerializer.Serialize(new{version=label,at=DateTimeOffset.UtcNow,status="startup-verified"})+Environment.NewLine);
        }
        catch(Exception e) when(e is IOException or JsonException or InvalidOperationException or KeyNotFoundException){ /* History cannot block normal startup. */ }
    }
    public void ResumeDownloaded()
    {
        var file=Path.Combine(_folder,"release.signed.json");
        if(!File.Exists(file))return;
        try
        {
            if(new FileInfo(file).Length>65536)return;
            var envelope=JsonSerializer.Deserialize<SignedEnvelope>(File.ReadAllText(file),SignedDocuments.Json)??throw new CommercialException("Updates_Untrusted");
            var release=ValidateEnvelope(envelope);
            if(File.Exists(Path.Combine(_folder,release.FileName))){_envelope=envelope;Available=release;}
        }
        catch(Exception e) when(e is CommercialException or IOException or JsonException){ /* An old or invalid package is never trusted. */ }
    }
    public async Task CheckAsync(CancellationToken ct = default)
    {
        if(RetailTestEdition.Standalone)
        {
            using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(15),MaxResponseContentBufferSize=65536};
            using var result=await client.GetAsync(RetailUpdatePolicy.ManifestUrl,ct);
            if(!result.IsSuccessStatusCode)throw new CommercialException("Updates_CheckFailed");
            var envelope=JsonSerializer.Deserialize<SignedEnvelope>(await result.Content.ReadAsStringAsync(ct),SignedDocuments.Json)??throw new CommercialException("Updates_Untrusted");
            var release=RetailUpdatePolicy.Validate(envelope,RetailUpdatePolicy.PublicKey,DisplayVersion,Schema,requireNewer:false);
            _envelope=envelope;Available=SemanticVersion.Parse(release.Version)>SemanticVersion.Parse(DisplayVersion)?release:null;
            RecordCheck();return;
        }
        if (license.Current is null) throw new CommercialException("License_Required");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        using var response = await http.PostAsJsonAsync(license.Options.Endpoint("/v1/updates"), new { certificate=license.Certificate, channel=license.Options.Channel, current=CurrentVersion.ToString(3) }, ct);
        if (!response.IsSuccessStatusCode) throw new CommercialException("Updates_CheckFailed");
        var json = await response.Content.ReadAsStringAsync(ct);
        var update = JsonSerializer.Deserialize<UpdateResponse>(json, SignedDocuments.Json);
        _envelope = update?.Envelope;
        _releaseId = update?.Id;
        Available = _envelope is null ? null : SignedDocuments.Release(_envelope, license.Options.PublicKeyPem, license.Current!, license.Options.Channel, CurrentVersion, Schema);
        RecordCheck();
    }
    private void RecordCheck(){LastCheck=DateTimeOffset.UtcNow;File.WriteAllText(Path.Combine(_folder,"last-check.txt"),LastCheck.Value.ToString("O"));Changed?.Invoke();}
    private ReleaseClaims ValidateEnvelope(SignedEnvelope envelope)=>RetailTestEdition.Standalone
        ?RetailUpdatePolicy.Validate(envelope,RetailUpdatePolicy.PublicKey,DisplayVersion,Schema)
        :SignedDocuments.Release(envelope,license.Options.PublicKeyPem,license.Current??throw new CommercialException("License_Required"),license.Options.Channel,CurrentVersion,Schema);
    private Task VerifySelectedPackageAsync(string path,ReleaseClaims release,CancellationToken ct=default)=>VerifyPackageCoreAsync(path,release,RetailTestEdition.Standalone,ct);
    public async Task DownloadAsync(CancellationToken ct = default)
    {
        if (Available is not { } release || !RetailTestEdition.Standalone&&_releaseId is null) throw new CommercialException("Updates_None");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var request = RetailTestEdition.Standalone?new HttpRequestMessage(HttpMethod.Get,RetailUpdatePolicy.DownloadUrl(release)):new HttpRequestMessage(HttpMethod.Post,license.Options.Endpoint("/v1/download")) { Content=JsonContent.Create(new { certificate=license.Certificate,releaseId=_releaseId,channel=license.Options.Channel,current=CurrentVersion.ToString(3) }) };
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(!response.IsSuccessStatusCode)throw new CommercialException("Updates_CheckFailed");
        var temp=Path.Combine(_folder,release.FileName+".partial");
        try
        {
            await using(var input=await response.Content.ReadAsStreamAsync(ct))
            await using(var output=File.Create(temp))
            {
                var buffer=new byte[81920];long count=0;int read;
                while((read=await input.ReadAsync(buffer,ct))>0){count+=read;if(count>release.Size)throw new CommercialException("Updates_Untrusted");await output.WriteAsync(buffer.AsMemory(0,read),ct);}
            }
            await VerifySelectedPackageAsync(temp,release,ct);
            File.Move(temp,Path.Combine(_folder,release.FileName),true);
            File.WriteAllText(Path.Combine(_folder,"release.signed.json"),JsonSerializer.Serialize(_envelope,SignedDocuments.Json));
        }
        finally { File.Delete(temp); }
        Changed?.Invoke();
    }
    public async Task ImportAsync(string envelopeFile,CancellationToken ct=default)
    {
        if(new FileInfo(envelopeFile).Length>65536)throw new CommercialException("Updates_Untrusted");
        var envelope=JsonSerializer.Deserialize<SignedEnvelope>(await File.ReadAllTextAsync(envelopeFile,ct),SignedDocuments.Json)??throw new CommercialException("Updates_Untrusted");
        var release=ValidateEnvelope(envelope);
        var package=Path.Combine(Path.GetDirectoryName(envelopeFile)!,release.FileName);
        await VerifySelectedPackageAsync(package,release,ct);
        var destination=Path.Combine(_folder,release.FileName);
        if(!Path.GetFullPath(package).Equals(Path.GetFullPath(destination),StringComparison.OrdinalIgnoreCase))File.Copy(package,destination,true);
        _envelope=envelope;Available=release;
        File.WriteAllText(Path.Combine(_folder,"release.signed.json"),JsonSerializer.Serialize(envelope,SignedDocuments.Json));Changed?.Invoke();
    }
    public static Task VerifyPackageAsync(string path,ReleaseClaims release,CancellationToken ct=default)=>VerifyPackageCoreAsync(path,release,false,ct);
    private static async Task VerifyPackageCoreAsync(string path,ReleaseClaims release,bool retail,CancellationToken ct)
    {
        var info=new System.IO.FileInfo(path);
        if(!info.Exists||info.Length!=release.Size)throw new CommercialException("Updates_Untrusted");
        await using(var file=File.OpenRead(path))
            if(!Convert.ToHexString(await SHA256.HashDataAsync(file,ct)).Equals(release.Sha256,StringComparison.OrdinalIgnoreCase))throw new CommercialException("Updates_Untrusted");
        using var zip=ZipFile.OpenRead(path);
        if(zip.Entries.Any(e=>e.FullName.Contains("..")||e.FullName.Replace('\\','/').StartsWith('/')||e.FullName.Contains(':')))throw new CommercialException("Updates_Untrusted");
        var exe=retail?"Wijgandu.exe":"SupermarketPos.exe";
        var entries=zip.Entries.Where(e=>e.FullName.Replace('\\','/').EndsWith('/'+exe,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(entries.Length!=1||entries[0].Length>100_000_000)throw new CommercialException("Updates_Untrusted");
        if(retail)
        {
            var specs=zip.Entries.Where(e=>!e.FullName.Contains('/')&&e.FullName.EndsWith(".nuspec",StringComparison.OrdinalIgnoreCase)).ToArray();
            if(specs.Length!=1)throw new CommercialException("Updates_Untrusted");
            using var stream=specs[0].Open();var doc=XDocument.Load(stream);var metadata=doc.Root?.Elements().SingleOrDefault(e=>e.Name.LocalName=="metadata");
            var id=metadata?.Elements().SingleOrDefault(e=>e.Name.LocalName=="id")?.Value;var version=metadata?.Elements().SingleOrDefault(e=>e.Name.LocalName=="version")?.Value;
            if(id!=RetailUpdatePolicy.PackageId||!SemanticVersion.TryParse(version,out var packaged)||packaged!=SemanticVersion.Parse(release.Version))throw new CommercialException("Updates_Untrusted");
        }
        var temp=Path.Combine(Path.GetTempPath(),"pos-verify-"+Guid.NewGuid().ToString("N")+".exe");
        try{entries[0].ExtractToFile(temp);if(!retail||release.PublisherThumbprint.Length>0)WindowsPublisherTrust.Verify(temp,release.PublisherThumbprint);}
        finally{File.Delete(temp);}
    }
    public async Task InstallAsync(Func<bool> hasDrafts,Func<Task> stopAndClose)
    {
        if(hasDrafts())throw new CommercialException("Updates_FinishDrafts");
        var metadataFile=Path.Combine(_folder,"release.signed.json");if(new FileInfo(metadataFile).Length>65536)throw new CommercialException("Updates_Untrusted");
        var envelope=JsonSerializer.Deserialize<SignedEnvelope>(File.ReadAllText(metadataFile),SignedDocuments.Json)??throw new CommercialException("Updates_Untrusted");
        var release=ValidateEnvelope(envelope);
        await VerifySelectedPackageAsync(Path.Combine(_folder,release.FileName),release);
        var manager=new UpdateManager(_folder,new UpdateOptions{ExplicitChannel=Channel});
        if(!manager.IsInstalled)throw new CommercialException("Updates_InstallerRequired");
        if(new DriveInfo(Path.GetPathRoot(_folder)!).AvailableFreeSpace<release.Size*3)throw new CommercialException("Storage_DiskSpace");
        if(new DriveInfo(Path.GetPathRoot(VelopackLocator.Current.PackagesDir!)!).AvailableFreeSpace<release.Size*3)throw new CommercialException("Storage_DiskSpace");
        // Feed is synthesized only from authenticated metadata; no untrusted feed reaches Velopack.
        File.WriteAllText(Path.Combine(_folder,$"releases.{Channel}.json"),LocalFeed(release));
        var update=await manager.CheckForUpdatesAsync()??throw new CommercialException("Updates_None");
        await manager.DownloadUpdatesAsync(update);
        if(update.TargetFullRelease.FileName!=release.FileName)throw new CommercialException("Updates_Untrusted");
        // Check the exact copy Velopack will install, including an already-cached package.
        await VerifySelectedPackageAsync(Path.Combine(VelopackLocator.Current.PackagesDir!,release.FileName),release);
        if(hasDrafts())throw new CommercialException("Updates_FinishDrafts");
        await DataBackup.CreateAsync(Path.GetDirectoryName(storage.DatabasePath)!,Path.Combine(Path.GetDirectoryName(storage.DatabasePath)!,"Backups","Before-update-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".zip"));
        await stopAndClose();
        File.AppendAllText(Path.Combine(_folder,"history.jsonl"),JsonSerializer.Serialize(new{version=release.Version,at=DateTimeOffset.UtcNow,status="install-requested"})+Environment.NewLine);
        manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
    }
    public static string LocalFeed(ReleaseClaims release)=>JsonSerializer.Serialize(new { Assets=new[]{new { PackageId=release.Schema==2&&release.Edition=="retail-standalone"?RetailUpdatePolicy.PackageId:"SupermarketPos",Version=release.Version,Type="Full",FileName=release.FileName,SHA256=release.Sha256,Size=release.Size,NotesMarkdown=release.Notes }} });
    private record UpdateResponse(SignedEnvelope Envelope,string Version,string Id);
}
