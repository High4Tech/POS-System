using System.Security.Cryptography;

namespace SupermarketPos.Infrastructure.Scanning;

public enum ScanMode { Idle, Scanning, Unknown, ProductEntry, Checkout, Away, ProductCapture, Purchasing, Receiving }

public sealed record ScanRequest(Guid EventId, string? Barcode, Guid Epoch, string? Format, int Version = 1,
    string? Symbology = null, SupermarketPos.Domain.Common.Gs1Barcode? Gs1 = null);
public sealed record ScanOutcome(string Outcome, string? Product = null, decimal? RemainingStock = null);
public sealed record ScannerReply(string State, Guid Epoch, string Register, string? Outcome = null, string? Product = null,
    Guid? EventId = null, bool Received = false, decimal? RemainingStock = null);
public sealed record ScannerPairing(string Code, DateTimeOffset ExpiresAt);
public sealed record ScannerAuthorization(string Token, string? RefreshToken, DateTimeOffset ExpiresAt, ScannerReply Scanner);
public sealed record ScannerTrust(string DeviceName, byte[] RefreshHash, byte[] AccessHash, DateTimeOffset ExpiresAt);

public interface IScannerTrustStore
{
    ScannerTrust? Load();
    void Save(ScannerTrust? trusted);
}

public interface IScannerIdentityStore
{
    byte[]? LoadCertificate();
    void SaveCertificate(byte[] pfx);
}

public sealed class ScannerSession
{
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromHours(24);
    private readonly object _sync = new();
    private readonly TimeProvider _clock;
    private readonly IScannerTrustStore? _store;
    private readonly TimeSpan _duration;
    private readonly SemaphoreSlim _delivery = new(1);
    private readonly Dictionary<Guid, ScanOutcome> _handled = new();
    private readonly Queue<Guid> _order = new();
    private ScannerPairing? _pairing;
    private ScannerTrust? _trust;
    private DateTimeOffset _lastSeen;
    private Guid _epoch = Guid.NewGuid();
    private ScanMode _mode;
    private string _register = "POS";
    private bool _wasConnected;

    public ScannerSession(TimeProvider? clock = null, IScannerTrustStore? store = null, TimeSpan? duration = null)
    {
        _clock = clock ?? TimeProvider.System;
        _store = store;
        _duration = duration ?? DefaultDuration;
        if (_duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        _trust = store?.Load();
    }

    public (bool Paired, bool Connected, ScanMode Mode) Status
    {
        get
        {
            lock (_sync)
                return (_trust is not null, _trust is not null && _lastSeen > _clock.GetUtcNow() - HeartbeatTimeout &&
                    _trust.ExpiresAt > _clock.GetUtcNow(), _mode);
        }
    }

    public string? DeviceName
    {
        get { lock (_sync) return _trust?.DeviceName; }
    }

    public void SetWorkflow(ScanMode mode, string register)
    {
        lock (_sync)
        {
            _register = register;
            if (_mode == mode) return;
            _mode = mode;
            _epoch = Guid.NewGuid();
        }
    }

    public ScannerPairing CreatePairing()
    {
        lock (_sync)
        {
            if (_trust is not null) throw new InvalidOperationException("A phone is already paired.");
            return _pairing = new ScannerPairing(NewSecret(24), _clock.GetUtcNow().AddMinutes(5));
        }
    }

    // Kept for in-process callers of the existing pairing API.
    public (string Token, ScannerReply State)? Pair(string? code)
    {
        var result = PairTrusted(code, null);
        return result is null ? null : (result.Token, result.Scanner);
    }

    public ScannerAuthorization? PairTrusted(string? code, string? deviceName)
    {
        lock (_sync)
        {
            if (_trust is not null || _pairing is null || _clock.GetUtcNow() >= _pairing.ExpiresAt ||
                !ExactSecret(_pairing.Code, code)) return null;

            var name = new string((deviceName ?? "").Where(ch => !char.IsControl(ch)).Take(60).ToArray()).Trim();
            var refresh = NewSecret(32);
            return Authorize(name.Length == 0 ? "Android" : name, Hash(refresh), refresh);
        }
    }

    public ScannerAuthorization? Renew(string? refreshCredential)
    {
        lock (_sync)
            return _trust is { } trusted && Matches(trusted.RefreshHash, refreshCredential)
                ? Authorize(trusted.DeviceName, trusted.RefreshHash, null)
                : null;
    }

    public void Forget()
    {
        lock (_sync)
        {
            _store?.Save(null);
            _trust = null;
            _pairing = null;
            ResetConnection();
            _handled.Clear();
            _order.Clear();
        }
    }

    public void Disconnect()
    {
        lock (_sync)
        {
            _pairing = null;
            ResetConnection();
        }
    }

    public ScannerReply? Ping(string? credential)
    {
        lock (_sync)
        {
            if (!Authorized(credential)) return null;
            MarkSeen();
            return Reply();
        }
    }

    public bool CanApply(Guid epoch)
    {
        lock (_sync)
            return IsAccepting(_mode) && _trust is { } trusted && trusted.ExpiresAt > _clock.GetUtcNow() && _epoch == epoch;
    }

    public async Task<ScannerReply?> ReceiveAsync(string? credential, ScanRequest request,
        Func<ScanRequest, CancellationToken, Task<ScanOutcome>> dispatch, CancellationToken ct = default)
    {
        lock (_sync)
        {
            if (!Authorized(credential)) return null;
            MarkSeen();
            if (_handled.TryGetValue(request.EventId, out var prior)) return Reply(prior);
            if (!IsAccepting(_mode) || _epoch != request.Epoch) return Reply(new ScanOutcome("paused"));
            if (!_delivery.Wait(0)) return Reply(new ScanOutcome("busy"));
        }

        try
        {
            var outcome = await dispatch(request, ct);
            lock (_sync)
            {
                if (!Authorized(credential)) return null;
                _handled[request.EventId] = outcome;
                _order.Enqueue(request.EventId);
                if (_order.Count > 512) _handled.Remove(_order.Dequeue());
                return Reply(outcome);
            }
        }
        finally
        {
            _delivery.Release();
        }
    }

    private ScannerAuthorization Authorize(string name, byte[] refreshHash, string? refresh)
    {
        var access = NewSecret(32);
        var expires = _clock.GetUtcNow() + _duration;
        var trust = new ScannerTrust(name, refreshHash, Hash(access), expires);
        _store?.Save(trust);
        _trust = trust;
        _pairing = null;
        _epoch = Guid.NewGuid();
        _lastSeen = _clock.GetUtcNow();
        _wasConnected = true;
        return new ScannerAuthorization(access, refresh, expires, Reply());
    }

    private void MarkSeen()
    {
        var now = _clock.GetUtcNow();
        if (!_wasConnected || now - _lastSeen >= HeartbeatTimeout) _epoch = Guid.NewGuid();
        _lastSeen = now;
        _wasConnected = true;
    }

    private void ResetConnection()
    {
        _lastSeen = default;
        _wasConnected = false;
        _epoch = Guid.NewGuid();
    }

    private bool Authorized(string? credential) =>
        _trust is { } trusted && trusted.ExpiresAt > _clock.GetUtcNow() && Matches(trusted.AccessHash, credential);

    private static bool IsAccepting(ScanMode mode) => mode is ScanMode.Scanning or ScanMode.ProductCapture or ScanMode.Purchasing;

    private ScannerReply Reply(ScanOutcome? outcome = null) =>
        new(_mode switch
        {
            ScanMode.Scanning => "scanning",
            ScanMode.Purchasing => "purchasing",
            ScanMode.ProductCapture => "product_capture",
            ScanMode.Receiving => "receiving",
            ScanMode.Unknown => "unknown",
            ScanMode.ProductEntry => "product_entry",
            ScanMode.Checkout => "checkout",
            ScanMode.Away => "away",
            _ => "idle"
        }, _epoch, _register, outcome?.Outcome, outcome?.Product, RemainingStock: outcome?.RemainingStock);

    private static string NewSecret(int count) => Convert.ToHexString(RandomNumberGenerator.GetBytes(count));

    private static byte[] Hash(string value) => SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(value));

    private static bool Matches(byte[] expected, string? offered) =>
        offered?.Length == 64 && offered.All(char.IsAsciiHexDigit) &&
        CryptographicOperations.FixedTimeEquals(expected, Hash(offered));

    private static bool ExactSecret(string expected, string? offered) =>
        offered?.Length == expected.Length && offered.All(char.IsAsciiHexDigit) &&
        CryptographicOperations.FixedTimeEquals(Hash(expected), Hash(offered));
}
