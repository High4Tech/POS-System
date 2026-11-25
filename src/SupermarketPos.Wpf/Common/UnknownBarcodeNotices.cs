namespace SupermarketPos.Wpf.Common;

internal sealed class UnknownBarcodeNotices
{
    private readonly Queue<(string Barcode, bool IsGs1)> _pending = new();
    private readonly HashSet<string> _codes = new(StringComparer.Ordinal);

    public string? Current => _pending.TryPeek(out var item) ? item.Barcode : null;
    public bool CurrentIsGs1 => _pending.TryPeek(out var item) && item.IsGs1;
    public int Remaining => Math.Max(0, _pending.Count - 1);

    public bool Add(string barcode, bool isGs1 = false)
    {
        if (!_codes.Add(barcode)) return false;
        _pending.Enqueue((barcode, isGs1));
        return true;
    }

    public void Dismiss()
    {
        if (_pending.TryDequeue(out var item)) _codes.Remove(item.Barcode);
    }
}
