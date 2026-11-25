using System.IO;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Velopack;
using Velopack.Locators;
using SupermarketPos.Infrastructure.Commercial;

namespace SupermarketPos.Wpf.Common;

public sealed class SoftwareUpdates(LicenseClient license, PosStorage storage)
{
    public const int Schema = 10; // Pharmacy catalog details; reject incompatible releases.
    public Version CurrentVersion => typeof(App).Assembly.GetName().Version ?? new(1,0,0);
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
            if(json.RootElement.GetProperty("status").GetString()=="install-requested"&&Version.TryParse(json.RootElement.GetProperty("version").GetString(),out var version)&&version==new Version(CurrentVersion.ToString(3)))
                File.AppendAllText(file,JsonSerializer.Serialize(new{version=version.ToString(3),at=DateTimeOffset.UtcNow,status="startup-verified"})+Environment.NewLine);
        }
        catch(Exception e) when(e is IOException or JsonException or InvalidOperationException){ /* History cannot block normal startup. */ }
    }
    public void ResumeDownloaded()
    {
        var file=Path.Combine(_folder,"release.signed.json");
        if(!File.Exists(file))return;
        try
        {
            var envelope=JsonSerializer.Deserialize<SignedEnvelope>(File.ReadAllText(file),SignedDocuments.Json)!;
            var release=SignedDocuments.Release(envelope,license.Options.PublicKeyPem,license.Current??throw new CommercialException("License_Required"),license.Options.Channel,CurrentVersion,Schema);
            if(File.Exists(Path.Combine(_folder,release.FileName))){_envelope=envelope;Available=release;}
        }
        catch(Exception e) when(e is CommercialException or IOException or JsonException){ /* An old or invalid package is never trusted. */ }
    }
    public async Task CheckAsync(CancellationToken ct = default)
    {
        if (license.Current is null) throw new CommercialException("License_Required");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        using var response = await http.PostAsJsonAsync(license.Options.Endpoint("/v1/updates"), new { certificate=license.Certificate, channel=license.Options.Channel, current=CurrentVersion.ToString(3) }, ct);
        if (!response.IsSuccessStatusCode) throw new CommercialException("Updates_CheckFailed");
        var json = await response.Content.ReadAsStringAsync(ct);
        var update = JsonSerializer.Deserialize<UpdateResponse>(json, SignedDocuments.Json);
        _envelope = update?.Envelope;
        _releaseId = update?.Id;
        Available = _envelope is null ? null : SignedDocuments.Release(_envelope, license.Options.PublicKeyPem, license.Current!, license.Options.Channel, CurrentVersion, Schema);
        LastCheck = DateTimeOffset.UtcNow;
        File.WriteAllText(Path.Combine(_folder,"last-check.txt"),LastCheck.Value.ToString("O"));
        Changed?.Invoke();
    }
    public async Task DownloadAsync(CancellationToken ct = default)
    {
        if (Available is not { } release || _releaseId is null) throw new CommercialException("Updates_None");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var request = new HttpRequestMessage(HttpMethod.Post,license.Options.Endpoint("/v1/download")) { Content=JsonContent.Create(new { certificate=license.Certificate,releaseId=_releaseId,channel=license.Options.Channel,current=CurrentVersion.ToString(3) }) };
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
            await VerifyPackageAsync(temp,release,ct);
            File.Move(temp,Path.Combine(_folder,release.FileName),true);
            File.WriteAllText(Path.Combine(_folder,"release.signed.json"),JsonSerializer.Serialize(_envelope,SignedDocuments.Json));
        }
        finally { File.Delete(temp); }
        Changed?.Invoke();
    }
    public async Task ImportAsync(string envelopeFile,CancellationToken ct=default)
    {
        var envelope=JsonSerializer.Deserialize<SignedEnvelope>(await File.ReadAllTextAsync(envelopeFile,ct),SignedDocuments.Json)??throw new CommercialException("Updates_Untrusted");
        var release=SignedDocuments.Release(envelope,license.Options.PublicKeyPem,license.Current??throw new CommercialException("License_Required"),license.Options.Channel,CurrentVersion,Schema);
        var package=Path.Combine(Path.GetDirectoryName(envelopeFile)!,release.FileName);
        await VerifyPackageAsync(package,release,ct);
        var destination=Path.Combine(_folder,release.FileName);
        if(!Path.GetFullPath(package).Equals(Path.GetFullPath(destination),StringComparison.OrdinalIgnoreCase))File.Copy(package,destination,true);
        _envelope=envelope;Available=release;
        File.WriteAllText(Path.Combine(_folder,"release.signed.json"),JsonSerializer.Serialize(envelope,SignedDocuments.Json));Changed?.Invoke();
    }
    public static async Task VerifyPackageAsync(string path,ReleaseClaims release,CancellationToken ct=default)
    {
        var info=new System.IO.FileInfo(path);
        if(!info.Exists||info.Length!=release.Size)throw new CommercialException("Updates_Untrusted");
        await using(var file=File.OpenRead(path))
            if(!Convert.ToHexString(await SHA256.HashDataAsync(file,ct)).Equals(release.Sha256,StringComparison.OrdinalIgnoreCase))throw new CommercialException("Updates_Untrusted");
        using var zip=ZipFile.OpenRead(path);
        if(zip.Entries.Any(e=>e.FullName.Contains("..")||e.FullName.StartsWith('/')||e.FullName.Contains(':')))throw new CommercialException("Updates_Untrusted");
        var entries=zip.Entries.Where(e=>e.FullName.Replace('\\','/').EndsWith("/SupermarketPos.exe",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(entries.Length!=1||entries[0].Length>100_000_000)throw new CommercialException("Updates_Untrusted");
        var temp=Path.Combine(Path.GetTempPath(),"pos-verify-"+Guid.NewGuid().ToString("N")+".exe");
        try{entries[0].ExtractToFile(temp);WindowsPublisherTrust.Verify(temp,release.PublisherThumbprint);}
        finally{File.Delete(temp);}
    }
    public async Task InstallAsync(Func<bool> hasDrafts,Func<Task> stopAndClose)
    {
        if(hasDrafts())throw new CommercialException("Updates_FinishDrafts");
        var envelope=JsonSerializer.Deserialize<SignedEnvelope>(File.ReadAllText(Path.Combine(_folder,"release.signed.json")),SignedDocuments.Json)!;
        var release=SignedDocuments.Release(envelope,license.Options.PublicKeyPem,license.Current??throw new CommercialException("License_Required"),license.Options.Channel,CurrentVersion,Schema);
        await VerifyPackageAsync(Path.Combine(_folder,release.FileName),release);
        var manager=new UpdateManager(_folder,new UpdateOptions{ExplicitChannel=license.Options.Channel});
        if(!manager.IsInstalled)throw new CommercialException("Updates_InstallerRequired");
        if(new DriveInfo(Path.GetPathRoot(_folder)!).AvailableFreeSpace<release.Size*3)throw new CommercialException("Storage_DiskSpace");
        if(new DriveInfo(Path.GetPathRoot(VelopackLocator.Current.PackagesDir!)!).AvailableFreeSpace<release.Size*3)throw new CommercialException("Storage_DiskSpace");
        // Feed is synthesized only from authenticated metadata; no untrusted feed reaches Velopack.
        File.WriteAllText(Path.Combine(_folder,$"releases.{license.Options.Channel}.json"),LocalFeed(release));
        var update=await manager.CheckForUpdatesAsync()??throw new CommercialException("Updates_None");
        await manager.DownloadUpdatesAsync(update);
        if(update.TargetFullRelease.FileName!=release.FileName)throw new CommercialException("Updates_Untrusted");
        // Check the exact copy Velopack will install, including an already-cached package.
        await VerifyPackageAsync(Path.Combine(VelopackLocator.Current.PackagesDir!,release.FileName),release);
        if(hasDrafts())throw new CommercialException("Updates_FinishDrafts");
        await DataBackup.CreateAsync(Path.GetDirectoryName(storage.DatabasePath)!,Path.Combine(Path.GetDirectoryName(storage.DatabasePath)!,"Backups","Before-update-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".zip"));
        await stopAndClose();
        File.AppendAllText(Path.Combine(_folder,"history.jsonl"),JsonSerializer.Serialize(new{version=release.Version,at=DateTimeOffset.UtcNow,status="install-requested"})+Environment.NewLine);
        manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
    }
    public static string LocalFeed(ReleaseClaims release)=>JsonSerializer.Serialize(new { Assets=new[]{new { PackageId="SupermarketPos",Version=release.Version,Type="Full",FileName=release.FileName,SHA256=release.Sha256,Size=release.Size,NotesMarkdown=release.Notes }} });
    private record UpdateResponse(SignedEnvelope Envelope,string Version,string Id);
}
