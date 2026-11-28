using System.IO;
using System.Reflection;
using System.Text.Json;
using SupermarketPos.Infrastructure.Commercial;
using Velopack;

namespace SupermarketPos.Wpf.Common;

public static class RetailUpdatePolicy
{
    public const string PackageId="WijganduRetailApp";
    public const string Channel="testing";
    public const string ManifestUrl="https://raw.githubusercontent.com/High4Tech/pos_backup/main/releases/retail/latest.signed.json";
    public static string CurrentVersion=>(typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion??"1.0.0").Split('+')[0];
    public static string PublicKey
    {
        get
        {
            using var input=typeof(App).Assembly.GetManifestResourceStream("SupermarketPos.RetailUpdatePublicKey")??throw new CommercialException("Updates_Untrusted");
            using var reader=new StreamReader(input);return reader.ReadToEnd();
        }
    }
    public static ReleaseClaims Validate(SignedEnvelope envelope,string key,string current,int schema,bool requireNewer=true)
    {
        ReleaseClaims release;
        try{release=SignedDocuments.Verify<ReleaseClaims>(envelope,key);}catch(CommercialException e){throw new CommercialException("Updates_Untrusted",e);}
        if(release.Schema!=2||release.Product!=SignedDocuments.Product||release.Edition!="retail-standalone"||release.Channel!=Channel||
            release.Notes is null||release.FileName is null||release.Version is null||release.MinimumVersion is null||release.Sha256 is null||release.PublisherThumbprint is null||
            !SemanticVersion.TryParse(release.Version,out var next)||!SemanticVersion.TryParse(current,out var installed)||!SemanticVersion.TryParse(release.MinimumVersion,out var minimum)||
            installed<minimum||requireNewer&&next<=installed||schema<release.MinimumSchema||schema>release.MaximumSchema||
            release.MinimumSchema<0||release.MaximumSchema<release.MinimumSchema||release.Kind is not ("security" or "bugfix" or "minor" or "major")||release.PublishedAt.Year<2020||release.PublishedAt>DateTimeOffset.UtcNow.AddMinutes(10)||release.Notes.Length>8000||
            release.Size is <=0 or >2_000_000_000||release.FileName!=Path.GetFileName(release.FileName)||release.FileName.Contains('\\')||
            !release.FileName.StartsWith(PackageId+"-",StringComparison.Ordinal)||!release.FileName.EndsWith("-full.nupkg",StringComparison.Ordinal)||release.Sha256.Length!=64||!release.Sha256.All(Uri.IsHexDigit)||
            release.PublisherThumbprint.Length!=0&&(release.PublisherThumbprint.Length is not (40 or 64)||!release.PublisherThumbprint.All(Uri.IsHexDigit)))throw new CommercialException("Updates_Ineligible");
        return release;
    }
    public static Uri DownloadUrl(ReleaseClaims release)=>new("https://github.com/High4Tech/pos_backup/releases/download/retail-v"+Uri.EscapeDataString(release.Version)+"/"+Uri.EscapeDataString(release.FileName));
}
