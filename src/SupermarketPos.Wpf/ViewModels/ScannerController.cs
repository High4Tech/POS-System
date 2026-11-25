using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupermarketPos.Infrastructure.Scanning;
using SupermarketPos.Wpf.Common;
using SupermarketPos.Wpf.Localization;
using SupermarketPos.Wpf.Views;
using ZXing;
using ZXing.QrCode;

namespace SupermarketPos.Wpf.ViewModels;

public sealed partial class ScannerController : ObservableObject
{
    private readonly ScannerSession _session;
    private readonly MobileScannerHost _host;
    private readonly ScannerStorage _storage;
    private readonly StoreProfile _profile;
    private readonly ScannerFeedback _sound;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private ScannerConnectionOptions _options;
    private object? _owner;
    private ScanMode _mode = ScanMode.Away;
    private Func<ScanRequest, Task<ScanOutcome>>? _handler;
    private Guid _physicalEpoch;
    private bool _physicalBusy;
    private System.Windows.Controls.TextBox? _barcodeField;
    private Func<ScanRequest,Func<bool>,Task<ScanOutcome>>? _fieldHandler;
    private Guid _fieldGeneration;
    private bool _starting;
    private bool _stopping;
    private bool _panelOpen;
    private int _pausedDialogs;
    private string? _link;
    private DateTimeOffset _qrExpiresAt;

    public ScannerController(ScannerSession session, MobileScannerHost host, ScannerStorage storage,
        StoreProfile profile, ScannerFeedback sound)
    {
        _session = session;
        _host = host;
        _storage = storage;
        _profile = profile;
        _sound = sound;
        _options = storage.LoadConnection();
        _method = _options.Method;
        _timer.Tick += async (_, _) => await PollAsync();
        PublishStatus();
    }

    [ObservableProperty] private ScanTransport _method;
    [ObservableProperty] private IReadOnlyList<LanEndpoint> _networks = Array.Empty<LanEndpoint>();
    [ObservableProperty] private LanEndpoint? _network;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string? _deviceName;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string? _manualDetails;
    [ObservableProperty] private bool _isPaired;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _showManual;
    [ObservableProperty] private ImageSource? _qr;
    public string Instructions => Text[Method switch
    {
        ScanTransport.Bluetooth => "Scanner_BluetoothSetup",
        ScanTransport.Usb => "Scanner_UsbSetup",
        _ => "Scanner_PairInstruction"
    }];
    public bool IsUnpaired => !IsPaired;
    private static LocalizationManager Text => LocalizationManager.Instance;

    public void Start()
    {
        ApplyMode();
        _timer.Start();
        _ = PollAsync();
    }

    public async Task StopAsync()
    {
        _stopping = true;
        _timer.Stop();
        Activate(null);
        await _host.DisposeAsync();
    }

    public void Activate(object? owner)
    {
        if (ReferenceEquals(owner, _owner)) return;
        _owner = owner;
        _physicalEpoch=Guid.Empty;
        _handler = null;
        _mode = ScanMode.Away;
        ApplyMode();
    }

    public void SetWorkflow(object owner, ScanMode mode, Func<ScanRequest, Task<ScanOutcome>>? handler = null)
    {
        if (!ReferenceEquals(owner, _owner)) return;
        if(_mode!=mode)_physicalEpoch=Guid.Empty;
        _mode = mode;
        _handler = handler;
        ApplyMode();
    }

    public bool CanApply(object owner, Guid epoch) =>
        ReferenceEquals(_owner, owner) && !ScanPaused && (_session.CanApply(epoch)||_physicalBusy&&_physicalEpoch==epoch&&_mode is ScanMode.Scanning or ScanMode.ProductCapture or ScanMode.Purchasing);

    private bool HasBarcodeField=>_barcodeField is{IsKeyboardFocused:true,IsVisible:true,IsEnabled:true}&&_fieldHandler is not null;
    private bool ScanPaused => _panelOpen || _pausedDialogs > 0 || InAppDialog.IsOpen&&!HasBarcodeField;

    public void AttachBarcodeField(System.Windows.Controls.TextBox field,Func<ScanRequest,Func<bool>,Task<ScanOutcome>> handler)
    {
        void Change(bool focused)
        {
            if(!focused&&!ReferenceEquals(_barcodeField,field))return;
            _barcodeField=focused?field:null;_fieldHandler=focused?handler:null;_fieldGeneration=Guid.NewGuid();
            _physicalEpoch=Guid.Empty;
            _session.SetWorkflow(ScanMode.Idle,_profile.Name);ApplyMode();
        }
        field.GotKeyboardFocus+=(_,_)=>Change(true);
        field.LostKeyboardFocus+=(_,_)=>Change(false);
        field.Unloaded+=(_,_)=>Change(false);
        field.PreviewKeyDown+=async(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){e.Handled=true;await DispatchPhysicalAsync(field.Text);}};
        if(field.IsKeyboardFocused)Change(true);
    }

    private Task<ScanOutcome> RouteAsync(ScanRequest request)
    {
        if(HasBarcodeField&&_fieldHandler is{} fieldHandler)
        {
            var generation=_fieldGeneration;
            bool Current()=>generation==_fieldGeneration&&HasBarcodeField&&!ScanPaused&&
                (_session.CanApply(request.Epoch)||_physicalBusy&&_physicalEpoch==request.Epoch);
            return fieldHandler(request,Current);
        }
        return _handler is{} handler?handler(request):Task.FromResult(new ScanOutcome("paused"));
    }
    public async Task<ScanOutcome> DispatchPhysicalAsync(string raw)
    {
        if(_physicalBusy||ScanPaused||!HasBarcodeField&&(_handler is null||_mode is not (ScanMode.Scanning or ScanMode.ProductCapture or ScanMode.Purchasing)))return new("paused");
        raw=raw.Trim();var marked=SupermarketPos.Domain.Common.Gs1Barcode.IsMarked(raw,null);var gs1=marked?SupermarketPos.Domain.Common.Gs1Barcode.Parse(raw,DateOnly.FromDateTime(DateTime.Today)):null;
        if(raw.Length is <1 or >512||raw.Any(c=>char.IsControl(c)&&c!='\u001d')||marked&&gs1 is null||Uri.TryCreate(raw,UriKind.Absolute,out var uri)&&uri.Scheme is "http" or "https"&&!marked)return new("invalid");
        _physicalBusy=true;_physicalEpoch=Guid.NewGuid();
        try{var result=await RouteAsync(new(Guid.NewGuid(),raw,_physicalEpoch,"physical",Gs1:gs1));_sound.PlayScanResult(result,_profile.LowStockThreshold);return result;}
        catch(Exception){Error=Text["Scanner_LookupError"];_sound.PlayWarning();return new("error");}
        finally{_physicalBusy=false;_physicalEpoch=Guid.Empty;}
    }

    public void WhileScanPaused(Action displayDialog)
    {
        _pausedDialogs++;
        ApplyMode();
        try { displayDialog(); }
        finally
        {
            _pausedDialogs--;
            ApplyMode();
        }
    }

    private void ApplyMode() => _session.SetWorkflow(ScanPaused ? ScanMode.Idle : HasBarcodeField?ScanMode.ProductCapture:_mode, _profile.Name);

    private Task<ScanOutcome> DispatchAsync(ScanRequest request, CancellationToken ct)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || ct.IsCancellationRequested)
            return Task.FromResult(new ScanOutcome("paused"));
        return dispatcher.InvokeAsync(async () =>
        {
            if (!_session.CanApply(request.Epoch) || ScanPaused || !HasBarcodeField&&_handler is null)
                return new ScanOutcome("paused");

            var result = await RouteAsync(request);
            _sound.PlayScanResult(result, _profile.LowStockThreshold);
            return result;
        }, DispatcherPriority.Normal, ct).Task.Unwrap();
    }

    [RelayCommand]
    private void OpenPanel()
    {
        if (_panelOpen) return;
        _panelOpen = true;
        ApplyMode();
        LoadAdapters();
        try
        {
            new ScannerConnectionWindow
            {
                DataContext = this,
                Owner = System.Windows.Application.Current.MainWindow
            }.ShowDialog();
        }
        finally
        {
            _panelOpen = false;
            ApplyMode();
        }
    }

    partial void OnMethodChanged(ScanTransport value)
    {
        LoadAdapters();
        ClearQr();
        OnPropertyChanged(nameof(Instructions));
    }

    partial void OnIsPairedChanged(bool value) => OnPropertyChanged(nameof(IsUnpaired));

    [RelayCommand]
    private Task PairPhoneAsync() => ConfigureAsync(pair: true);

    [RelayCommand]
    private Task ReconnectAsync() => ConfigureAsync(pair: false);

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (_starting) return;
        try
        {
            _options = _options with { Enabled = false };
            _storage.SaveConnection(_options);
            ClearQr();
            await _host.DisposeAsync();
            PublishStatus();
        }
        catch (Exception error) { ReportListenerError(error); }
    }

    [RelayCommand]
    private async Task ForgetPhoneAsync()
    {
        if (_starting) return;
        try
        {
            _session.Forget();
            ClearQr();
            await _host.DisposeAsync();
            PublishStatus();
        }
        catch (Exception error) { ReportListenerError(error); }
    }

    [RelayCommand]
    private void CopyPairLink()
    {
        if (_link is null || _qrExpiresAt <= DateTimeOffset.UtcNow) return;
        try { System.Windows.Clipboard.SetText(_link); }
        catch (System.Runtime.InteropServices.ExternalException) { }
    }

    private async Task ConfigureAsync(bool pair)
    {
        if (_starting || _stopping || pair && IsPaired) return;
        var method = Method;
        var available = LanEndpoint.Available(method);
        var selected = Network is { } preferred
            ? available.FirstOrDefault(adapter => adapter.Name == preferred.Name && adapter.Address.Equals(preferred.Address))
              ?? available.FirstOrDefault()
            : available.FirstOrDefault();
        if (selected is null)
        {
            Error = NetworkError(method);
            return;
        }
        _options = new(method, selected.Name, true, _options.Port);
        try
        {
            _storage.SaveConnection(_options);
            if (!await EnsureListenerAsync(selected, pair) || _stopping) return;
            if (!pair || _session.Status.Paired) return;
            var pairing = _session.CreatePairing();
            _link = _host.PairingLink(pairing, method);
            _qrExpiresAt = pairing.ExpiresAt;
            Qr = CreateQr(_link);
            var fingerprint = _link.Split("pin=")[1].Split('&')[0];
            ManualDetails = string.Join(Environment.NewLine,
                $"{Text["Scanner_Host"]}  {selected.Address}",
                $"{Text["Scanner_Port"]}  {_host.Address?.Port}",
                $"{Text["Scanner_Fingerprint"]}  {fingerprint}",
                $"{Text["Scanner_PairCode"]}  {pairing.Code}");
        }
        catch (Exception error) { ReportListenerError(error); }
        finally { PublishStatus(); }
    }

    private async Task PollAsync()
    {
        if (_stopping || _starting) return;
        PublishStatus();
        if (!_options.Enabled || !_session.Status.Paired) return;
        var adapters = LanEndpoint.Available(_options.Method);
        var endpoint = adapters.FirstOrDefault(adapter => adapter.Name == _options.Adapter);
        if (endpoint is null)
        {
            if (_host.Address is not null)
            {
                _starting = true;
                try { await _host.DisposeAsync(); }
                catch (Exception error) { ReportListenerError(error); }
                finally { _starting = false; }
            }
            Error = NetworkError(_options.Method);
            return;
        }
        await EnsureListenerAsync(endpoint);
    }

    private async Task<bool> EnsureListenerAsync(LanEndpoint endpoint, bool allowAlternatePort = false)
    {
        if (_starting || _stopping) return false;
        if (_host.Address?.Host == endpoint.Address.ToString()) return true;
        _starting = true;
        try
        {
            Error = null;
            if (_host.Address is not null) await _host.DisposeAsync();
            if (_stopping) return false;
            await StartListenerAsync(endpoint, allowAlternatePort);
            if (_host.Address is { } address && address.Port != _options.Port)
            {
                _options = _options with { Port = address.Port };
                _storage.SaveConnection(_options);
            }
            return true;
        }
        catch (Exception error)
        {
            ReportListenerError(error);
            return false;
        }
        finally
        {
            _starting = false;
            PublishStatus();
        }
    }

    private async Task StartListenerAsync(LanEndpoint endpoint, bool allowAlternatePort)
    {
        var preferred = _options.Port is >= 1 and <= 65535 ? _options.Port : MobileScannerHost.DefaultPort;
        var ports = allowAlternatePort && !_session.Status.Paired
            ? new[] { preferred }.Concat(Enumerable.Range(MobileScannerHost.DefaultPort + 1, 4)).Distinct().ToArray()
            : new[] { preferred };
        for (var index = 0; index < ports.Length; index++)
        {
            try
            {
                await _host.StartAsync(endpoint, DispatchAsync, ports[index]);
                return;
            }
            catch (Exception error) when (index < ports.Length - 1 && IsPortInUse(error))
            {
                System.Diagnostics.Trace.TraceInformation("POS Scannar port {0} is busy.", ports[index]);
            }
        }
    }

    private static bool IsPortInUse(Exception? error)
    {
        for (; error is not null; error = error.InnerException)
            if (error is System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.AddressAlreadyInUse })
                return true;
        return false;
    }

    private void LoadAdapters()
    {
        var adapterName = Network?.Name ?? _options.Adapter;
        Networks = LanEndpoint.Available(Method);
        Network = Networks.FirstOrDefault(adapter => adapter.Name == adapterName) ?? Networks.FirstOrDefault();
    }

    private void PublishStatus()
    {
        var status = _session.Status;
        IsPaired = status.Paired;
        IsConnected = status.Connected;
        DeviceName = _session.DeviceName;
        if (IsPaired || _link is not null && _qrExpiresAt <= DateTimeOffset.UtcNow) ClearQr();
        Status = Text[status.Connected ? "Scanner_Connected"
            : !IsPaired ? "Scanner_Disconnected"
            : _options.Enabled ? "Scanner_Reconnecting" : "Scanner_ManualDisconnected"];
    }

    private void ClearQr()
    {
        Qr = null;
        _link = null;
        ManualDetails = null;
        ShowManual = false;
    }

    private static string NetworkError(ScanTransport method) => Text[method switch
    {
        ScanTransport.Bluetooth => "Scanner_NoBluetooth",
        ScanTransport.Usb => "Scanner_NoUsb",
        _ => "Scanner_NoNetwork"
    }];

    private void ReportListenerError(Exception error)
    {
        System.Diagnostics.Trace.TraceWarning("POS Scannar listener: {0}", error);
        Error = Text["Scanner_ListenError"];
    }

    private static BitmapSource CreateQr(string value)
    {
        const int size = 196;
        var matrix = new QRCodeWriter().encode(value, BarcodeFormat.QR_CODE, size, size,
            new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 3 });
        var pixels = new byte[size * size];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                pixels[y * size + x] = matrix[x, y] ? (byte)0 : (byte)255;
        var image = BitmapSource.Create(size, size, 96, 96, PixelFormats.Gray8, null, pixels, size);
        image.Freeze();
        return image;
    }
}
