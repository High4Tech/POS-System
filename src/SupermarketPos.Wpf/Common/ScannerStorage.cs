using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using SupermarketPos.Infrastructure.Scanning;

namespace SupermarketPos.Wpf.Common;

public sealed record ScannerConnectionOptions(ScanTransport Method = ScanTransport.Wifi, string? Adapter = null,
    bool Enabled = true, int Port = MobileScannerHost.DefaultPort);
public sealed record ScannerSoundOptions(bool Enabled = true, bool Success = true, bool Warning = true);

public sealed class ScannerStorage : IScannerTrustStore, IScannerIdentityStore
{
    private readonly string _folder;
    private readonly object _gate = new();

    public ScannerStorage(PosStorage pos)
    {
        _folder = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(pos.DatabasePath)!, "Scanner")).FullName;
    }

    public ScannerTrust? Load() => Read<ScannerTrust>("trust");
    public void Save(ScannerTrust? trusted) => Write("trust", trusted);
    public ScannerConnectionOptions LoadConnection() => Read<ScannerConnectionOptions>("connection") ?? new();
    public void SaveConnection(ScannerConnectionOptions options) => Write("connection", options);
    public ScannerSoundOptions LoadSound() => Read<ScannerSoundOptions>("sound") ?? new();
    public void SaveSound(ScannerSoundOptions options) => Write("sound", options);

    public byte[]? LoadCertificate()
    {
        lock (_gate) return ReadBytes("identity");
    }

    public void SaveCertificate(byte[] pfx)
    {
        lock (_gate) WriteBytes("identity", pfx);
    }

    private T? Read<T>(string file) where T : class
    {
        lock (_gate)
        {
            var plaintext = ReadBytes(file);
            if (plaintext is null) return null;
            try { return JsonSerializer.Deserialize<T>(plaintext); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
    }

    private void Write<T>(string file, T? value) where T : class
    {
        lock (_gate)
        {
            if (value is null)
            {
                File.Delete(PathFor(file));
                return;
            }
            var plaintext = JsonSerializer.SerializeToUtf8Bytes(value);
            try { WriteBytes(file, plaintext); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
    }

    private byte[]? ReadBytes(string file)
    {
        var path = PathFor(file);
        return File.Exists(path)
            ? ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser)
            : null;
    }

    private void WriteBytes(string file, byte[] plaintext)
    {
        var encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
        var temporary = Path.Combine(_folder, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew))
            {
                stream.Write(encrypted);
                stream.Flush(true);
            }
            File.Move(temporary, PathFor(file), overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    private string PathFor(string name) => Path.Combine(_folder, name + ".dat");
}
