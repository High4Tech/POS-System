using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupermarketPos.Infrastructure.Commercial;

public record SignedEnvelope(string Payload, string Signature);
public record LicenseClaims(int Schema, string Product, string LicenseId, string ActivationId, string Device,
    string Edition, int Major, DateTimeOffset UpdatesUntil, DateTimeOffset IssuedAt);
public record ReleaseClaims(int Schema, string Product, string Edition, string Channel, string Version, string MinimumVersion,
    int MinimumSchema, int MaximumSchema, string Kind, DateTimeOffset PublishedAt, string FileName, long Size, string Sha256, string PublisherThumbprint, string Notes = "");

public static class SignedDocuments
{
    public const string Product = "supermarketpos";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static T Verify<T>(SignedEnvelope envelope, string publicKeyPem)
    {
        try
        {
            var payload = Convert.FromBase64String(envelope.Payload);
            var signature = Convert.FromBase64String(envelope.Signature);
            if (payload.Length > 32_768 || signature.Length != 64) throw new CryptographicException();
            using var key = ECDsa.Create();
            key.ImportFromPem(publicKeyPem);
            if (key.KeySize != 256 || !key.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw new CryptographicException();
            return JsonSerializer.Deserialize<T>(payload, Json) ?? throw new CryptographicException();
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException or CryptographicException)
        { throw new CommercialException("License_Invalid", ex); }
    }
    public static LicenseClaims License(SignedEnvelope envelope, string key, string device, int major)
    {
        var claims = Verify<LicenseClaims>(envelope, key);
        if (claims.Schema != 1 || claims.Product != Product || claims.Device != device || claims.Major < major ||
            string.IsNullOrWhiteSpace(claims.ActivationId) || claims.IssuedAt > DateTimeOffset.UtcNow.AddMinutes(10)) throw new CommercialException("License_Invalid");
        return claims;
    }
    public static ReleaseClaims Release(SignedEnvelope envelope, string key, LicenseClaims license, string channel, Version current, int schema)
    {
        var release = Verify<ReleaseClaims>(envelope, key);
        if (release.Schema != 1 || release.Product != Product || release.Edition != license.Edition || release.Channel != channel ||
            !Version.TryParse(release.Version, out var next) || !Version.TryParse(release.MinimumVersion, out var min) || next <= current || current < min ||
            next.Major > license.Major || schema < release.MinimumSchema || schema > release.MaximumSchema ||
            release.Kind is not ("security" or "bugfix" or "minor" or "major") || release.PublishedAt > DateTimeOffset.UtcNow.AddMinutes(10) ||
            release.PublishedAt > license.UpdatesUntil || release.Notes.Length > 8000 || release.Size is <= 0 or > 2_000_000_000 ||
            release.FileName != Path.GetFileName(release.FileName) || release.FileName.Contains('\\') ||
            !release.FileName.EndsWith("-full.nupkg", StringComparison.OrdinalIgnoreCase) || release.Sha256.Length != 64 ||
            !release.Sha256.All(Uri.IsHexDigit) || release.PublisherThumbprint.Length is not (40 or 64) || !release.PublisherThumbprint.All(Uri.IsHexDigit))
            throw new CommercialException("Updates_Ineligible");
        return release;
    }
}
public sealed class CommercialException(string key, Exception? inner = null) : Exception(key, inner) { }
