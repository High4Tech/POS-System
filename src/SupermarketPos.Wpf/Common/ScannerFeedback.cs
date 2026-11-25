using System.IO;
using System.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupermarketPos.Infrastructure.Scanning;

namespace SupermarketPos.Wpf.Common;

public sealed partial class ScannerFeedback : ObservableObject
{
    private readonly ScannerStorage _storage;
    // POS Scannar uses Android's TONE_PROP_BEEP and a 140 ms TONE_PROP_NACK.
    private readonly SoundPlayer _scanTone = CreateTone(40, 400, 1200);
    private readonly SoundPlayer _warningTone = CreateTone(140, 300, 400, 500);
    private long _lastWarning = -400;

    public ScannerFeedback(ScannerStorage storage)
    {
        _storage = storage;
        var options = storage.LoadSound();
        _enabled = options.Enabled;
        _success = options.Success;
        _warning = options.Warning;
    }

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private bool _success;
    [ObservableProperty] private bool _warning;

    partial void OnEnabledChanged(bool value) => Save();
    partial void OnSuccessChanged(bool value) => Save();
    partial void OnWarningChanged(bool value) => Save();

    public void PlayScanResult(ScanOutcome result, decimal lowStockThreshold)
    {
        switch (result.Outcome)
        {
            case "added" when result.RemainingStock <= lowStockThreshold:
            case "unknown":
            case "stock":
            case "inactive":
            case "duplicate":
            case "batch_required":
            case "batch_unknown":
            case "batch_discrepancy":
            case "expired":
            case "held":
            case "serial_duplicate":
            case "invalid":
            case "error":
                PlayWarning();
                break;
            case "added":
            case "purchase_added":
            case "captured":
            case "location_selected":
                if (Enabled && Success) _scanTone.Play();
                break;
        }
    }

    public void PlayWarning()
    {
        if (!Enabled || !Warning) return;
        var now = Environment.TickCount64;
        if (now - _lastWarning < 400) return;
        _lastWarning = now;
        _warningTone.Play();
    }

    [RelayCommand]
    private void TestSound() => _scanTone.Play();

    private static SoundPlayer CreateTone(int durationMs, params int[] frequencies)
    {
        const int sampleRate = 24000;
        const int rampSamples = sampleRate / 500;
        var sampleCount = sampleRate * durationMs / 1000;
        var dataSize = sampleCount * sizeof(short);
        var wave = new MemoryStream(44 + dataSize);
        using (var writer = new BinaryWriter(wave, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + dataSize);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write((short)1); // Mono
            writer.Write(sampleRate);
            writer.Write(sampleRate * sizeof(short));
            writer.Write((short)sizeof(short));
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(dataSize);

            for (var index = 0; index < sampleCount; index++)
            {
                var sample = 0.0;
                foreach (var frequency in frequencies)
                    sample += Math.Sin(Math.Tau * frequency * index / sampleRate);
                // Fade the ends to avoid a click when the tone starts or stops.
                var envelope = Math.Min(1.0, Math.Min(index, sampleCount - 1 - index) / (double)rampSamples);
                writer.Write((short)(sample * envelope * 12000 / frequencies.Length));
            }
        }
        wave.Position = 0;
        var player = new SoundPlayer(wave);
        player.Load();
        return player;
    }

    private void Save() => _storage.SaveSound(new ScannerSoundOptions(Enabled, Success, Warning));
}
