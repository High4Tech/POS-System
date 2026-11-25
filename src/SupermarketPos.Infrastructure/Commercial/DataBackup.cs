using System.IO.Compression;
using Microsoft.Data.Sqlite;

namespace SupermarketPos.Infrastructure.Commercial;

public static class DataBackup
{
    private static readonly string[] Included = ["Media", "CategoryImages", "Category Images", "Product Images", "Store Profile", "Scanner", "Documents", "PDFs"];
    public static async Task<string> CreateAsync(string dataDirectory, string destination, CancellationToken ct = default)
    {
        var source = Path.Combine(dataDirectory, "pos.db");
        var temporaryDb = Path.Combine(dataDirectory, "backup-" + Guid.NewGuid().ToString("N") + ".db");
        var temporaryZip = destination + ".partial";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        try
        {
            using (var original = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            using (var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporaryDb, Pooling = false }.ToString()))
            { await original.OpenAsync(ct); await backup.OpenAsync(ct); original.BackupDatabase(backup);using var journal=backup.CreateCommand();journal.CommandText="PRAGMA journal_mode=DELETE";await journal.ExecuteScalarAsync(ct); }
            await VerifyAsync(temporaryDb, ct);
            using (var zip = ZipFile.Open(temporaryZip, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(temporaryDb, "pos.db");
                foreach (var name in Included)
                {
                    var folder = Path.Combine(dataDirectory, name);
                    if (!Directory.Exists(folder)) continue;
                    foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                        zip.CreateEntryFromFile(file.FullName, Path.GetRelativePath(dataDirectory, file.FullName).Replace('\\', '/'));
                }
                foreach (var file in Directory.EnumerateFiles(dataDirectory, "*.json").Concat(Directory.EnumerateFiles(dataDirectory, "*.txt")))
                    zip.CreateEntryFromFile(file, Path.GetFileName(file));
            }
            File.Move(temporaryZip, destination, true);
            return destination;
        }
        finally { File.Delete(temporaryDb);File.Delete(temporaryDb+"-wal");File.Delete(temporaryDb+"-shm");File.Delete(temporaryZip); }
    }
    public static async Task VerifyAsync(string path, CancellationToken ct = default)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await db.OpenAsync(ct); using var check = db.CreateCommand(); check.CommandText = "PRAGMA quick_check";
        if (await check.ExecuteScalarAsync(ct) as string != "ok") throw new CommercialException("Storage_InvalidBackup");
    }
    public static async Task StageRestoreAsync(string archive, string dataDirectory, ProtectedLocalStore store, CancellationToken ct = default)
    {
        var stage = Path.Combine(dataDirectory, "Restore-staged-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        long total = 0;
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > 100_000) throw new CommercialException("Storage_InvalidBackup");
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            total += entry.Length;
            if (total > 10_000_000_000 || name.StartsWith('/') || name.Split('/').Any(s => s is ".." or ".") || name.Contains(':') || name.EndsWith('/'))
                throw new CommercialException("Storage_InvalidBackup");
            var allowed = name == "pos.db" || Included.Any(f => name.StartsWith(f + "/", StringComparison.Ordinal)) ||
                !name.Contains('/') && (name.EndsWith(".json") || name.EndsWith(".txt"));
            if (!allowed) throw new CommercialException("Storage_InvalidBackup");
            var path = Path.GetFullPath(Path.Combine(stage, name));
            if (!path.StartsWith(stage + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new CommercialException("Storage_InvalidBackup");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path); // duplicate entries are rejected
        }
        await VerifyAsync(Path.Combine(stage, "pos.db"), ct);
        using (var check = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=Path.Combine(stage,"pos.db"), Mode=SqliteOpenMode.ReadOnly, Pooling=false }.ToString()))
        {
            await check.OpenAsync(ct);
            using var cmd=check.CreateCommand();cmd.CommandText="SELECT MAX(MigrationId) FROM __EFMigrationsHistory";
            var last=await cmd.ExecuteScalarAsync(ct) as string;
            if (last is null || string.CompareOrdinal(last, Persistence.Migrations.MedicineDetails.MigrationId)>0) throw new CommercialException("Storage_IncompatibleBackup");
        }
        store.Write("pending-restore", stage);
    }
    // Only call before creating any EF context/scanner. Preserve a backup of the compatible old state.
    public static async Task ApplyPendingRestoreAsync(string dataDirectory, ProtectedLocalStore store)
    {
        var stage = store.Read("pending-restore");
        if (string.IsNullOrWhiteSpace(stage)) return;
        if (Path.GetDirectoryName(stage) != Path.GetFullPath(dataDirectory) || !Path.GetFileName(stage).StartsWith("Restore-staged-")) throw new CommercialException("Storage_InvalidBackup");
        await VerifyAsync(Path.Combine(stage, "pos.db"));
        var backup = Path.Combine(dataDirectory, "Backups", "Before-restore-" + Path.GetFileName(stage) + ".zip");
        if (File.Exists(Path.Combine(dataDirectory, "pos.db"))&&!File.Exists(backup)) await CreateAsync(dataDirectory, backup);
        SqliteConnection.ClearAllPools();
        File.Delete(Path.Combine(dataDirectory, "pos.db-wal")); File.Delete(Path.Combine(dataDirectory, "pos.db-shm"));
        foreach (var source in Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories))
        {
            if(Path.GetFileName(source)=="pos.db"&&Path.GetDirectoryName(source)==stage)continue;
            var path = Path.Combine(dataDirectory, Path.GetRelativePath(stage, source));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(source, path, true);
        }
        // Install the verified database last. A failed media copy leaves the old database intact;
        // the protected staging marker permits a retry before any application context opens.
        var stagedDb=Path.Combine(dataDirectory,"restore-verified.db");
        File.Copy(Path.Combine(stage,"pos.db"),stagedDb,true);
        await VerifyAsync(stagedDb);
        File.Move(stagedDb,Path.Combine(dataDirectory,"pos.db"),true);
        await VerifyAsync(Path.Combine(dataDirectory, "pos.db"));
        store.Write("pending-restore", "");
    }
}
