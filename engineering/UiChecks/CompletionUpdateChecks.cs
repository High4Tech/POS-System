using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using SupermarketPos.Domain.Common;
using SupermarketPos.Infrastructure.Commercial;
using SupermarketPos.Infrastructure.Documents;
using SupermarketPos.Wpf.Common;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

internal static partial class Program
{
    private static async Task SignedRetailPackageChecks(string[] args,SoftwareUpdates updates)
    {
        Assert(RetailTestEdition.Standalone,"Real Retail package checks use the published standalone edition");
        var index=Array.IndexOf(args,"--signed-retail-package");if(index<0||index+2>=args.Length)throw new ArgumentException("Supply signed metadata and its package.");
        var envelope=JsonSerializer.Deserialize<SignedEnvelope>(File.ReadAllText(args[index+1]),SignedDocuments.Json)!;
        var release=RetailUpdatePolicy.Validate(envelope,RetailUpdatePolicy.PublicKey,"1.2.0-beta.7",15);
        Assert(release.FileName==Path.GetFileName(args[index+2]),"Actual Retail package matches its publisher-signed filename and upgrade policy");
        await (Task)typeof(SoftwareUpdates).GetMethod("VerifyPackageCoreAsync",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[args[index+2],release,true,CancellationToken.None])!;_checks++;
        if(args.Contains("--online-feed"))
        {
            await updates.CheckAsync();Assert(updates.LastCheck.HasValue&&updates.Available is null,"Published Retail feed verifies without licensing and treats the current release as up to date");
        }
    }

    private static async Task CompletionUpdateChecks(string root,SoftwareUpdates updates)
    {
        Assert(AppBrand.Name==(RetailTestEdition.Enabled?"Wijgandu":"ORMA"),"Software branding follows the edition");
        Assert(SoftwareBrand.Name==AppBrand.Name&&ReceiptDocument.SoftwareFooter.StartsWith(AppBrand.Name+" by "),"Receipt software footer follows edition without changing business identity");
        var icon=Application.GetResourceStream(new Uri("/SupermarketPos;component/Assets/app.ico",UriKind.Relative))!;
        using(var stream=icon.Stream)
        {
            var hash=Convert.ToHexString(SHA256.HashData(stream));
            Assert(hash==(RetailTestEdition.Enabled?"5F3AE0828ED00A78E214E41FC411D720B459330E1FD20625C49D1CEE48A80A14":"E82F7B77460C9D9516EA2955F12C03A9B5E8524089FACB26598DE487190E73E4"),"WPF caption resolves the correct edition icon");
        }
        if(RetailTestEdition.Standalone)
        {
            using var trusted=ECDsa.Create();trusted.ImportFromPem(RetailUpdatePolicy.PublicKey);
            Assert(trusted.KeySize==256,"Standalone build embeds its Retail verification key");
        }
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);var publicKey=key.ExportSubjectPublicKeyInfoPem();
        var folder=Directory.CreateDirectory(Path.Combine(root,"retail-update-check")).FullName;
        var path=Path.Combine(folder,"WijganduRetailApp-1.2.0-beta.9-full.nupkg");
        var claims=new ReleaseClaims(2,SignedDocuments.Product,"retail-standalone","testing","1.2.0-beta.9","1.2.0-beta.7",14,15,"bugfix",DateTimeOffset.UtcNow,Path.GetFileName(path),1,new('a',64),"","Test release");
        SignedEnvelope Sign(ReleaseClaims value)
        {
            var bytes=JsonSerializer.SerializeToUtf8Bytes(value,SignedDocuments.Json);
            return new(Convert.ToBase64String(bytes),Convert.ToBase64String(key.SignData(bytes,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
        }
        void Rejected(SignedEnvelope envelope,string verificationKey,string current="1.2.0-beta.8",int schema=15)
        {
            try{RetailUpdatePolicy.Validate(envelope,verificationKey,current,schema);throw new Exception("Untrusted or ineligible release accepted");}catch(CommercialException){_checks++;}
        }
        Assert(RetailUpdatePolicy.Validate(Sign(claims),publicKey,"1.2.0-beta.8",15)==claims,"Signed Retail update works without a commercial license");
        Rejected(Sign(claims with{Edition="retail"}),publicKey);Rejected(Sign(claims with{Channel="stable"}),publicKey);
        Rejected(Sign(claims with{MinimumSchema=16}),publicKey);Rejected(Sign(claims with{FileName="../evil-full.nupkg"}),publicKey);
        Rejected(Sign(claims with{FileName="SupermarketPos-1.2.0-beta.9-full.nupkg"}),publicKey);
        Rejected(Sign(claims),publicKey,"1.2.0-beta.9");Rejected(Sign(claims),publicKey,"1.2.0-beta.10");
        using(var other=ECDsa.Create(ECCurve.NamedCurves.nistP256))Rejected(Sign(claims),other.ExportSubjectPublicKeyInfoPem());
        var signed=Sign(claims);Rejected(signed with{Payload=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{}"))},publicKey);
        Assert(RetailUpdatePolicy.Validate(signed,publicKey,"1.2.0-beta.9",15,false)==claims,"Current signed feed is a harmless no-update result");
        Assert(RetailUpdatePolicy.DownloadUrl(claims).AbsoluteUri.StartsWith("https://github.com/High4Tech/pos_backup/releases/download/retail-v1.2.0-beta.9/"),"Retail downloads stay in their own repository and release");
        ReleaseClaims Package(string id,string version,string exe="Wijgandu.exe")
        {
            if(File.Exists(path))File.Delete(path);
            using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))
            {
                using(var writer=new StreamWriter(zip.CreateEntry("WijganduRetailApp.nuspec").Open()))writer.Write($"<package><metadata><id>{id}</id><version>{version}</version></metadata></package>");
                using(var writer=new StreamWriter(zip.CreateEntry("lib/net8.0/"+exe).Open()))writer.Write("Inert verification fixture; never executed");
            }
            return claims with{Size=new FileInfo(path).Length,Sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))};
        }
        Task Verify(ReleaseClaims value)=>(Task)typeof(SoftwareUpdates).GetMethod("VerifyPackageCoreAsync",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[path,value,true,CancellationToken.None])!;
        async Task BadPackage(ReleaseClaims value){try{await Verify(value);throw new Exception("Invalid package accepted");}catch(CommercialException){_checks++;}}
        var valid=Package(RetailUpdatePolicy.PackageId,claims.Version);await Verify(valid);_checks++;
        File.WriteAllText(Path.Combine(folder,"releases.testing.json"),SoftwareUpdates.LocalFeed(valid));
        var downloads=Directory.CreateDirectory(Path.Combine(root,"retail-downloads")).FullName;
        var manager=new UpdateManager(new SimpleFileSource(new DirectoryInfo(folder)),new UpdateOptions{ExplicitChannel="testing"},new TestVelopackLocator(RetailUpdatePolicy.PackageId,"1.2.0-beta.8",downloads,null!));
        var next=await manager.CheckForUpdatesAsync();Assert(next?.TargetFullRelease.PackageId==RetailUpdatePolicy.PackageId&&next.TargetFullRelease.Version.ToString()==valid.Version,"Velopack recognizes the separate Retail package and prerelease version");
        await manager.DownloadUpdatesAsync(next!);
        Assert(Directory.GetFiles(downloads,valid.FileName,SearchOption.AllDirectories).Any(p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))==valid.Sha256),"Retail staged package retains the authenticated hash");
        File.AppendAllText(path,"tamper");await BadPackage(valid);
        await BadPackage(Package("SupermarketPos",claims.Version));await BadPackage(Package(RetailUpdatePolicy.PackageId,"1.2.0-beta.8"));
        await BadPackage(Package(RetailUpdatePolicy.PackageId,claims.Version,"SupermarketPos.exe"));
        await UpdateFeedChecks(root,updates);
    }
}
