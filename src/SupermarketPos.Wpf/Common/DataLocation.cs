using System.IO;
using SupermarketPos.Infrastructure.Commercial;

namespace SupermarketPos.Wpf.Common;
public static class DataLocation
{
    // Only a protected folder pointer lives here; business records remain in the chosen data folder.
    private static ProtectedLocalStore Bootstrap => new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),RetailTestEdition.Enabled?"High4TechPosTest":"SupermarketPos","Bootstrap"));
    public static string Resolve(string fallback)
    {
        var store=Bootstrap;
        var pending=store.Read("relocate");
        if(!string.IsNullOrWhiteSpace(pending))
        {
            var paths=System.Text.Json.JsonSerializer.Deserialize<string[]>(pending)!;
            Task.Run(async()=>
            {
                ValidateTarget(paths[1], false);
                var archive=Path.Combine(paths[0],"Backups","Before-relocation-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".zip");
                await DataBackup.CreateAsync(paths[0],archive);
                var targetStore=new ProtectedLocalStore(Path.Combine(paths[1],"Protected"));
                await DataBackup.StageRestoreAsync(archive,paths[1],targetStore);
                await DataBackup.ApplyPendingRestoreAsync(paths[1],targetStore);
                var sourceProtected=Path.Combine(paths[0],"Protected");
                if(Directory.Exists(sourceProtected))
                    foreach(var file in Directory.EnumerateFiles(sourceProtected,"*.protected"))
                        if(Path.GetFileName(file) is "activation.protected" or "fbr-sandbox-token.protected") File.Copy(file,Path.Combine(paths[1],"Protected",Path.GetFileName(file)),true);
                store.Write("data-folder",paths[1]);store.Write("relocate","");
            }).GetAwaiter().GetResult();
        }
        return store.Read("data-folder") is { Length:>0 } selected?Path.GetFullPath(selected):Path.GetFullPath(fallback);
    }
    public static void Schedule(string source,string target)
    {
        ValidateChange(source,target);
        target=Path.GetFullPath(target.Trim());source=Path.GetFullPath(source);
        if(source.Equals(target,StringComparison.OrdinalIgnoreCase))return;
        Bootstrap.Write("relocate",System.Text.Json.JsonSerializer.Serialize(new[]{source,target}));
    }
    public static void ValidateChange(string source,string target)
    {
        if(!Path.IsPathFullyQualified(target))throw new CommercialException("Storage_InvalidFolder");
        target=Path.GetFullPath(target.Trim());source=Path.GetFullPath(source);
        if(source.Equals(target,StringComparison.OrdinalIgnoreCase))return;
        if(target.StartsWith(source+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||source.StartsWith(target+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new CommercialException("Storage_InvalidFolder");
        ValidateTarget(target);
    }
    private static void ValidateTarget(string path, bool requireEmpty = true)
    {
        if(!Path.IsPathFullyQualified(path)||path==Path.GetPathRoot(path)||new DriveInfo(Path.GetPathRoot(path)!).DriveType!=DriveType.Fixed||
            Path.GetFullPath(path).StartsWith(AppContext.BaseDirectory,StringComparison.OrdinalIgnoreCase))throw new CommercialException("Storage_InvalidFolder");
        if(requireEmpty&&Directory.Exists(path)&&Directory.EnumerateFileSystemEntries(path).Any())throw new CommercialException("Storage_EmptyFolder");
        Directory.CreateDirectory(path);
        var check=Path.Combine(path,".pos-write-check-"+Guid.NewGuid().ToString("N"));File.WriteAllText(check,"");File.Delete(check);
    }
}
