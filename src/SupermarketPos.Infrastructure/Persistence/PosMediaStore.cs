using System.Security.Cryptography;

namespace SupermarketPos.Infrastructure.Persistence;

/// <summary>Stores image bytes beside the database and represents them in records by a portable token.</summary>
public sealed class PosMediaStore(string dataDirectory)
{
    private readonly string _directory = Path.Combine(dataDirectory, "Media");

    public static bool IsToken(string? reference) =>
        reference is { Length: 32 } && Guid.TryParseExact(reference, "N", out _);

    public string? ResolvePath(string? reference)
    {
        if (IsToken(reference)) return Path.Combine(_directory, reference!.ToLowerInvariant() + ".image");
        return reference is not null && Path.IsPathFullyQualified(reference) ? reference : null;
    }

    public string? ResolveCategoryPath(string? reference)
    {
        if (IsToken(reference)) return ResolvePath(reference);
        if (reference is not { Length: 36 } || !reference.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            !IsToken(reference[..32])) return null;
        return Path.Combine(dataDirectory, "Category Images", reference);
    }

    public string? Import(string? source)
    {
        if (string.IsNullOrWhiteSpace(source) || IsToken(source)) return source;

        Directory.CreateDirectory(_directory);
        var staging = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.Copy(source, staging);
            var hash = HashFile(staging);
            var token = Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
            var destination = ResolvePath(token)!;
            try { File.Move(staging, destination); }
            catch (IOException) when (File.Exists(destination))
            {
                if (!HashFile(destination).AsSpan().SequenceEqual(hash)) throw;
            }
            return token;
        }
        finally { File.Delete(staging); }
    }

    private static byte[] HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return SHA256.HashData(stream);
    }
}
