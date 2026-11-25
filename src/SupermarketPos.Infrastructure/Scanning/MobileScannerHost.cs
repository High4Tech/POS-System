using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SupermarketPos.Infrastructure.Scanning;

public sealed class MobileScannerHost(ScannerSession session, IScannerIdentityStore? identity = null) : IAsyncDisposable
{
    public const int DefaultPort = 8765;
    public const string CertificateHost = "pos-scannar.local";
    private readonly SemaphoreSlim _lifecycle = new(1);
    private WebApplication? _app;
    private X509Certificate2? _certificate;
    public Uri? Address { get; private set; }

    public async Task StartAsync(LanEndpoint endpoint, Func<ScanRequest, CancellationToken, Task<ScanOutcome>> dispatch,
        int port = DefaultPort, CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try { await StartListenerAsync(endpoint, dispatch, port, ct).ConfigureAwait(false); }
        finally { _lifecycle.Release(); }
    }

    private async Task StartListenerAsync(LanEndpoint endpoint, Func<ScanRequest, CancellationToken, Task<ScanOutcome>> dispatch,
        int port, CancellationToken ct)
    {
        if (_app is not null) throw new InvalidOperationException("The scanner listener is already running.");
        var certificate = CreateCertificate(endpoint.Address);
        WebApplication? app = null;
        try
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                Args = Array.Empty<string>(),
                ContentRootPath = AppContext.BaseDirectory
            });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(server =>
            {
                server.AddServerHeader = false;
                server.Limits.MaxRequestBodySize = 4096;
                server.Limits.MaxRequestHeadersTotalSize = 8192;
                server.Limits.MaxConcurrentConnections = 12;
                server.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
                server.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(25);
                server.Listen(endpoint.Address, port, listener =>
                {
                    listener.Protocols = HttpProtocols.Http1;
                    listener.UseHttps(certificate);
                });
            });
            app = builder.Build();
            app.Use(async (context, next) =>
            {
                if (!endpoint.Contains(context.Connection.RemoteIpAddress))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
                context.Response.Headers.CacheControl = "no-store";
                await next(context);
            });
            app.MapPost("/pair", async (HttpContext context) =>
            {
                var request = await ReadAsync<PairRequest>(context.Request);
                if (request is null) return Results.BadRequest();
                return session.PairTrusted(request.Code, request.DeviceName) is { } paired
                    ? Results.Json(paired) : Results.Unauthorized();
            });
            app.MapPost("/renew", (HttpContext context) =>
                session.Renew(Authorization(context)) is { } renewed ? Results.Json(renewed) : Results.Unauthorized());
            app.MapGet("/ping", (HttpContext context) =>
                session.Ping(Authorization(context)) is { } state ? Results.Json(state) : Results.Unauthorized());
            var stopping = app.Lifetime.ApplicationStopping;
            app.MapPost("/scan", async (HttpContext context) =>
            {
                var token = Authorization(context);
                if (session.Ping(token) is null) return Results.Unauthorized();
                var request = await ReadAsync<ScanRequest>(context.Request);
                if (request is null || request.Version != 1 || request.EventId == Guid.Empty || request.Epoch == Guid.Empty ||
                    string.IsNullOrWhiteSpace(request.Barcode) || request.Barcode.Length > 512 ||
                    request.Format?.Length > 32 || request.Symbology?.Length > 8 ||
                    request.Barcode.Any(c => char.IsControl(c) && c != '\u001d'))
                    return Results.BadRequest();

                var marked = Domain.Common.Gs1Barcode.IsMarked(request.Barcode, request.Symbology);
                if(Uri.TryCreate(request.Barcode,UriKind.Absolute,out var url)&&url.Scheme is "http" or "https"&&!marked)return Results.BadRequest();
                var gs1 = marked ? Domain.Common.Gs1Barcode.Parse(request.Barcode, DateOnly.FromDateTime(DateTime.Today)) : null;
                if (marked && gs1 is null || !marked && request.Barcode.Contains('\u001d') ||
                    request.Gs1 is not null && request.Gs1 != gs1)
                    return Results.BadRequest();
                request = request with { Gs1 = gs1 };

                var reply = await session.ReceiveAsync(token, request, dispatch, stopping);
                return reply is null ? Results.Unauthorized() : Results.Json(reply with
                {
                    EventId = request.EventId,
                    Received = true
                });
            });
            await app.StartAsync(ct).ConfigureAwait(false);
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
            Address = new Uri(addresses!.Addresses.Single());
            _certificate = certificate;
            _app = app;
        }
        catch
        {
            if (app is not null) await app.DisposeAsync().ConfigureAwait(false);
            certificate.Dispose();
            throw;
        }
    }

    public string PairingLink(ScannerPairing pairing, ScanTransport transport = ScanTransport.Wifi)
    {
        var address = Address ?? throw new InvalidOperationException("The scanner listener has not started.");
        var pin = _certificate!.GetCertHashString(HashAlgorithmName.SHA256);
        var method = transport.ToString().ToLowerInvariant();
        return $"techpos://pair?v=1&method={method}&host={address.Host}&port={address.Port}&pin={pin}&code={pairing.Code}&identity={CertificateHost}";
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try { await StopListenerAsync().ConfigureAwait(false); }
        finally { _lifecycle.Release(); }
    }

    private async Task StopListenerAsync()
    {
        if (_app is not { } app) return;
        _app = null;
        Address = null;
        session.Disconnect();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await app.StopAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await app.DisposeAsync().ConfigureAwait(false);
            _certificate?.Dispose();
            _certificate = null;
        }
    }

    private static string? Authorization(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..] : null;
    }

    private static async Task<T?> ReadAsync<T>(HttpRequest request) where T : class
    {
        if (!request.HasJsonContentType()) return null;
        try { return await request.ReadFromJsonAsync<T>(cancellationToken: request.HttpContext.RequestAborted); }
        catch (JsonException) { return null; }
        catch (Microsoft.AspNetCore.Http.BadHttpRequestException) { return null; }
    }

    private X509Certificate2 CreateCertificate(IPAddress address)
    {
        if (identity?.LoadCertificate() is { } persisted)
        {
            try { return ImportCertificate(persisted); }
            finally { CryptographicOperations.ZeroMemory(persisted); }
        }
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={CertificateHost}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(address);
        san.AddDnsName(CertificateHost);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
        // Schannel needs a user key container for TLS. This import is removed on disposal.
        var pfx = generated.Export(X509ContentType.Pkcs12);
        try
        {
            identity?.SaveCertificate(pfx);
            return ImportCertificate(pfx);
        }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }

    private static X509Certificate2 ImportCertificate(byte[] pfx) =>
        new(pfx, (string?)null, X509KeyStorageFlags.UserKeySet);

    private sealed record PairRequest(string? Code, string? DeviceName);
}
