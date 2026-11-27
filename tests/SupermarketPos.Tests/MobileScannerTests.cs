using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography;
using System.Text.Json;
using SupermarketPos.Infrastructure.Scanning;
using SupermarketPos.Domain.Common;

namespace SupermarketPos.Tests;

public sealed class MobileScannerTests
{
    [Fact]
    public async Task Gs1PayloadPreservesRawGroupSeparatorsAndValidatesPhoneMetadata()
    {
        var session = new ScannerSession();
        session.SetWorkflow(ScanMode.Scanning, "Register");
        ScanRequest? delivered = null;
        await using var host = new MobileScannerHost(session);
        await host.StartAsync(new LanEndpoint("Test", IPAddress.Loopback, IPAddress.Parse("255.0.0.0")),
            (request, _) =>
            {
                delivered = request;
                return Task.FromResult(new ScanOutcome("added"));
            }, port: 0);
        var link = host.PairingLink(session.CreatePairing());
        using var client = CreateClient(host.Address!, link.Split("pin=")[1].Split('&')[0]);
        using var paired = await client.PostAsJsonAsync("/pair", new { code = link.Split("code=")[1].Split('&')[0] });
        paired.EnsureSuccessStatusCode();
        using var data = JsonDocument.Parse(await paired.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", data.RootElement.GetProperty("token").GetString());
        var state = (await client.GetFromJsonAsync<ScannerReply>("/ping"))!;
        const string raw = "01095211015300011728063010B28\u001d21SERIAL1";
        var gs1 = new Gs1Barcode("09521101530001", "B28", new DateOnly(2028, 6, 30), "SERIAL1");
        var scan = new ScanRequest(Guid.NewGuid(), raw, state.Epoch, "data_matrix", Symbology: "]d2", Gs1: gs1);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/scan", scan with { Gs1 = gs1 with { Batch = "other" } })).StatusCode);
        Assert.Null(delivered);
        Assert.Equal("added", (await SendAsync(client, scan)).Outcome);
        Assert.Equal(raw, delivered!.Barcode);
        Assert.Equal(gs1, delivered.Gs1);
        var plain = scan with { EventId = Guid.NewGuid(), Barcode = "0109521101530001", Symbology = "]d1", Gs1 = null };
        Assert.Equal("added", (await SendAsync(client, plain)).Outcome);
        Assert.Null(delivered.Gs1);
    }
    [Fact]
    public async Task PinnedHttpsPairingAuthorizesScansAndDistinguishesRepeatsFromRedelivery()
    {
        var session = new ScannerSession(new AdjustableClock());
        session.SetWorkflow(ScanMode.Scanning, "Test Register");
        var applied = 0;
        await using var host = new MobileScannerHost(session);
        await host.StartAsync(new LanEndpoint("Test", IPAddress.Loopback, IPAddress.Parse("255.0.0.0")),
            (request, _) =>
            {
                Interlocked.Increment(ref applied);
                return Task.FromResult(new ScanOutcome("added", request.Barcode));
            }, port: 0);
        var pairing = session.CreatePairing();
        var pin = host.PairingLink(pairing).Split("pin=")[1].Split('&')[0];
        using var client = CreateClient(host.Address!, pin);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/pair", new { code = new string('0', 48) })).StatusCode);
        var response = await client.PostAsJsonAsync("/pair", new { code = pairing.Code });
        response.EnsureSuccessStatusCode();
        using var paired = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            paired.RootElement.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/pair", new { code = pairing.Code })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/scan", new { barcode = "no event ID" })).StatusCode);

        var state = (await client.GetFromJsonAsync<ScannerReply>("/ping"))!;
        Assert.Equal("Test Register", state.Register);
        var first = new ScanRequest(Guid.NewGuid(), "000123", state.Epoch, "code_128");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/scan", first with { Version = 2 })).StatusCode);
        using var legacy = await client.PostAsJsonAsync("/scan", new
        {
            first.EventId,
            first.Barcode,
            first.Epoch,
            first.Format
        });
        legacy.EnsureSuccessStatusCode();
        Assert.Equal("added", (await SendAsync(client, first)).Outcome);
        Assert.Equal("added", (await SendAsync(client, first)).Outcome);
        Assert.Equal("added", (await SendAsync(client, first with { EventId = Guid.NewGuid() })).Outcome);
        Assert.Equal(2, applied);

        session.Forget();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/scan", first)).StatusCode);
        using var untrusted = CreateClient(host.Address!, new string('0', 64));
        await Assert.ThrowsAsync<HttpRequestException>(() => untrusted.GetAsync("/ping"));
    }

    [Theory]
    [InlineData(ScanTransport.Wifi, "wifi")]
    [InlineData(ScanTransport.Bluetooth, "bluetooth")]
    [InlineData(ScanTransport.Usb, "usb")]
    public async Task EachTransportLabelsItsPairingLinkAndUsesTheSameReceiver(ScanTransport transport, string wireName)
    {
        var session = new ScannerSession();
        session.SetWorkflow(ScanMode.Scanning, "Register");
        await using var host = new MobileScannerHost(session);
        await host.StartAsync(new LanEndpoint("Test", IPAddress.Loopback, IPAddress.Parse("255.0.0.0")),
            (_, _) => Task.FromResult(new ScanOutcome("added")), port: 0);
        var pairing = session.CreatePairing();
        var link = host.PairingLink(pairing, transport);
        Assert.Contains($"&method={wireName}&", link);
        var pin = link.Split("pin=")[1].Split('&')[0];
        using var client = CreateClient(host.Address!, pin);
        using var paired = await client.PostAsJsonAsync("/pair", new { code = pairing.Code });
        paired.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await paired.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            body.RootElement.GetProperty("token").GetString());
        var state = (await client.GetFromJsonAsync<ScannerReply>("/ping"))!;
        var scan = new ScanRequest(Guid.NewGuid(), "000123", state.Epoch, "code_128");
        Assert.Equal("added", (await SendAsync(client, scan)).Outcome);
    }

    [Fact]
    public async Task PausedAndDisconnectedScansDoNotReplayIntoTheRegister()
    {
        var clock = new AdjustableClock();
        var session = new ScannerSession(clock);
        session.SetWorkflow(ScanMode.Scanning, "Test Register");
        var expired = session.CreatePairing();
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Null(session.Pair(expired.Code));
        var (token, state) = session.Pair(session.CreatePairing().Code)!.Value;
        var unknown = new ScanRequest(Guid.NewGuid(), "001", state.Epoch, "manual");
        var result = await session.ReceiveAsync(token, unknown, (_, _) =>
        {
            session.SetWorkflow(ScanMode.Unknown, "Test Register");
            return Task.FromResult(new ScanOutcome("unknown"));
        });
        Assert.Equal("unknown", result!.Outcome);

        foreach (var mode in new[] { ScanMode.Unknown, ScanMode.ProductEntry, ScanMode.Checkout, ScanMode.Away, ScanMode.Idle })
        {
            session.SetWorkflow(mode, "Test Register");
            var paused = session.Ping(token)!;
            var request = new ScanRequest(Guid.NewGuid(), "002", paused.Epoch, "ean_13");
            Assert.Equal("paused", (await session.ReceiveAsync(token, request, Unexpected))!.Outcome);
        }

        session.SetWorkflow(ScanMode.Scanning, "Test Register");
        var beforeDisconnect = session.Ping(token)!;
        clock.Advance(TimeSpan.FromSeconds(8));
        Assert.False(session.Status.Connected);
        var reconnect = session.Ping(token)!;
        Assert.True(session.Status.Connected);
        Assert.NotEqual(beforeDisconnect.Epoch, reconnect.Epoch);
        Assert.Equal("paused", (await session.ReceiveAsync(token, unknown with
        {
            EventId = Guid.NewGuid(),
            Epoch = beforeDisconnect.Epoch
        }, Unexpected))!.Outcome);
        Assert.Equal("unknown", (await session.ReceiveAsync(token, unknown, Unexpected))!.Outcome);
    }

    [Fact]
    public async Task BusyDeliveryIsRejectedInsteadOfQueued()
    {
        var session = new ScannerSession();
        session.SetWorkflow(ScanMode.Scanning, "Register");
        var (token, state) = session.Pair(session.CreatePairing().Code)!.Value;
        var first = new ScanRequest(Guid.NewGuid(), "123", state.Epoch, "manual");
        var finish = new TaskCompletionSource<ScanOutcome>();
        var inFlight = session.ReceiveAsync(token, first, (_, _) => finish.Task);
        Assert.Equal("busy", (await session.ReceiveAsync(token, first with { EventId = Guid.NewGuid() }, Unexpected))!.Outcome);
        finish.SetResult(new ScanOutcome("added"));
        Assert.Equal("added", (await inFlight)!.Outcome);
    }

    [Fact]
    public async Task PairingSurvivesRestartAndRenewalWithoutReusingTheWorkflowEpoch()
    {
        var clock = new AdjustableClock();
        var storage = new MemoryScannerStorage();
        var endpoint = new LanEndpoint("Test", IPAddress.Loopback, IPAddress.Parse("255.0.0.0"));
        ScannerAuthorization pair;
        string pin;
        var first = new ScannerSession(clock, storage);
        await using (var host = new MobileScannerHost(first, storage))
        {
            await host.StartAsync(endpoint, Unexpected, port: 0);
            var pairing = first.CreatePairing();
            pin = host.PairingLink(pairing).Split("pin=")[1].Split('&')[0];
            using var phone = CreateClient(host.Address!, pin);
            using var response = await phone.PostAsJsonAsync("/pair", new { code = pairing.Code, deviceName = "Shop phone" });
            response.EnsureSuccessStatusCode();
            pair = (await response.Content.ReadFromJsonAsync<ScannerAuthorization>())!;
            Assert.NotNull(pair.RefreshToken);
            Assert.Equal(clock.GetUtcNow().AddHours(24), pair.ExpiresAt);
        }

        var resumed = new ScannerSession(clock, storage);
        Assert.True(resumed.Status.Paired);
        Assert.Equal("Shop phone", resumed.DeviceName);
        await using var restarted = new MobileScannerHost(resumed, storage);
        await restarted.StartAsync(endpoint, (scan, _) =>
            Task.FromResult(new ScanOutcome("purchase_added", scan.Barcode)), port: 0);
        using var client = CreateClient(restarted.Address!, pin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pair.Token);
        var restored = (await client.GetFromJsonAsync<ScannerReply>("/ping"))!;
        Assert.NotEqual(pair.Scanner.Epoch, restored.Epoch);
        clock.Advance(TimeSpan.FromHours(24));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/renew", new { })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pair.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/ping")).StatusCode);
        using var renewal = await client.PostAsJsonAsync("/renew", new { });
        renewal.EnsureSuccessStatusCode();
        var authorization = (await renewal.Content.ReadFromJsonAsync<ScannerAuthorization>())!;
        Assert.NotEqual(pair.Token, authorization.Token);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authorization.Token);
        resumed.SetWorkflow(ScanMode.Purchasing, "Shop");
        var purchase = (await client.GetFromJsonAsync<ScannerReply>("/ping"))!;
        var accepted = await SendAsync(client, new ScanRequest(Guid.NewGuid(), "001", purchase.Epoch, "manual"));
        Assert.Equal("purchase_added", accepted.Outcome);
        Assert.True(accepted.Received);
        resumed.SetWorkflow(ScanMode.ProductCapture, "Shop");
        Assert.Equal("paused", (await SendAsync(client,
            new ScanRequest(Guid.NewGuid(), "001", purchase.Epoch, "manual"))).Outcome);
        resumed.Forget();
        Assert.False(new ScannerSession(clock, storage).Status.Paired);
        Assert.Null(resumed.Renew(pair.RefreshToken));
    }

    private static Task<ScanOutcome> Unexpected(ScanRequest request, CancellationToken ct) =>
        throw new InvalidOperationException("A rejected scan unexpectedly reached the register.");

    private static async Task<ScannerReply> SendAsync(HttpClient client, ScanRequest scan)
    {
        using var response = await client.PostAsJsonAsync("/scan", scan);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ScannerReply>())!;
    }

    private static HttpClient CreateClient(Uri address, string pin) =>
        new(new HttpClientHandler
        {
            UseProxy = false,
            ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
                certificate?.GetCertHashString(HashAlgorithmName.SHA256) == pin &&
                (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) == SslPolicyErrors.None
        }) { BaseAddress = address, Timeout = TimeSpan.FromSeconds(10) };

    private sealed class AdjustableClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan time) => _now += time;
    }

    private sealed class MemoryScannerStorage : IScannerTrustStore, IScannerIdentityStore
    {
        private ScannerTrust? _trusted;
        private byte[]? _certificate;
        public ScannerTrust? Load() => _trusted;
        public void Save(ScannerTrust? trusted) => _trusted = trusted;
        public byte[]? LoadCertificate() => _certificate?.ToArray();
        public void SaveCertificate(byte[] pfx) => _certificate = pfx.ToArray();
    }
}
