using System.Net.Http.Json;
using System.Text.Json;

namespace SupermarketPos.Infrastructure.Commercial;

public record PublisherOptions(string Api, string PublicKeyPem, bool Commercial, string Channel = "stable", int Major = 1, string? Support = null, DateTimeOffset? PublishedAt = null)
{
    public Uri Endpoint(string route)
    {
        if (!Uri.TryCreate(Api, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && !(uri.IsLoopback && !Commercial)))
            throw new CommercialException("License_ServicePending");
        return new Uri(uri.ToString().TrimEnd('/') + route);
    }
}
public sealed class LicenseClient(PublisherOptions options, ProtectedLocalStore storage, string device)
{
    public PublisherOptions Options => options;
    public string Device => device;
    public LicenseClaims? Current
    {
        get
        {
            var saved = storage.Read("activation");
            var claims = saved is null ? null : SignedDocuments.License(JsonSerializer.Deserialize<SignedEnvelope>(saved, SignedDocuments.Json)!, options.PublicKeyPem, device, options.Major);
            if (claims is not null && options.Commercial && (options.PublishedAt is null || options.PublishedAt > claims.UpdatesUntil)) throw new CommercialException("License_Invalid");
            return claims;
        }
    }
    public SignedEnvelope? Certificate => storage.Read("activation") is { } saved ? JsonSerializer.Deserialize<SignedEnvelope>(saved, SignedDocuments.Json) : null;
    public void Import(string json)
    {
        if (json.Length > 64_000) throw new CommercialException("License_Invalid");
        var envelope = JsonSerializer.Deserialize<SignedEnvelope>(json, SignedDocuments.Json) ?? throw new CommercialException("License_Invalid");
        var claims = SignedDocuments.License(envelope, options.PublicKeyPem, device, options.Major);
        if (options.Commercial && (options.PublishedAt is null || options.PublishedAt > claims.UpdatesUntil)) throw new CommercialException("License_Invalid");
        storage.Write("activation", JsonSerializer.Serialize(envelope, SignedDocuments.Json));
    }
    public static void ValidateActivationInput(string key,string label)
    {
        if(!System.Text.RegularExpressions.Regex.IsMatch(key.Trim(),"^POS-[a-fA-F0-9]{48}$"))throw new CommercialException("Validation_LicenseKey");
        if(string.IsNullOrWhiteSpace(label)||label.Length>100||label.Any(char.IsControl))throw new CommercialException("Validation_DeviceLabel");
    }
    public string OfflineRequest(string key, string label)
    {
        ValidateActivationInput(key,label);
        return JsonSerializer.Serialize(new { schema = 1, product = SignedDocuments.Product, key = key.Trim(), device, label = label.Trim(), nonce = Guid.NewGuid().ToString("N") }, SignedDocuments.Json);
    }
    public async Task ActivateAsync(string key, string label, CancellationToken ct = default)
    {
        ValidateActivationInput(key,label);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize=65536 };
        using var response = await http.PostAsJsonAsync(options.Endpoint("/v1/activate"), new { key = key.Trim(), device, label = label.Trim() }, ct);
        if (!response.IsSuccessStatusCode) throw new CommercialException("License_ActivationFailed");
        var json = await response.Content.ReadAsStringAsync(ct);
        Import(json);
    }
    public async Task RequestTransferAsync(CancellationToken ct = default)
    {
        var certificate = Certificate ?? throw new CommercialException("License_Invalid");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        using var response = await http.PostAsJsonAsync(options.Endpoint("/v1/transfer-request"), new { certificate }, ct);
        if (!response.IsSuccessStatusCode) throw new CommercialException("License_ActivationFailed");
    }
}
