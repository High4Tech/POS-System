using System.Globalization;
using System.IO;

namespace SupermarketPos.Wpf.Common;

public sealed class DesktopTextSize(PosStorage storage)
{
    public static IReadOnlyList<int> Options { get; } = Array.AsReadOnly(new[] { 85, 100, 115, 130, 150, 175, 200 });
    private static readonly int[] BaseSizes = [12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 26, 28, 32, 44];
    private readonly string _path = Path.Combine(Path.GetDirectoryName(storage.DatabasePath)!, "desktop-text-size.txt");

    public int Percent { get; private set; } = 100;

    public void Load()
    {
        try
        {
            if (File.Exists(_path) && int.TryParse(File.ReadAllText(_path), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var saved) && Options.Contains(saved))
                Percent = saved;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        Apply();
    }

    public void Save(int percent)
    {
        if (!Options.Contains(percent)) throw new ArgumentOutOfRangeException(nameof(percent));
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, percent.ToString(CultureInfo.InvariantCulture));
            File.Move(temporary, _path, overwrite: true);
        }
        finally { File.Delete(temporary); }
        Percent = percent;
        Apply();
    }

    private void Apply()
    {
        var resources = System.Windows.Application.Current.Resources;
        var scale = Percent / 100d;
        foreach (int size in BaseSizes)
            resources[$"TextSize{size}"] = size * scale;
        resources["TableTextSize"] = 17 * scale;
        resources["TableHeadingTextSize"] = 16 * scale;
        resources["TableSecondaryTextSize"] = 15 * scale;
        resources["TableRowHeight"] = Math.Max(48d, 52 * scale);
        resources["RegisterCartMinimumHeight"] = 160 * scale;
        resources["TableLabelHeight"] = Math.Max(38d, 46 * scale);
        resources["InventoryThumbnailSize"] = 36 * scale;
        var dialogGrowth = Math.Max(0, scale - 1);
        resources["ProductDialogWidth"] = 836 + 180 * dialogGrowth;
        resources["PaymentDialogWidth"] = 640 + 180 * dialogGrowth;
        resources["CompactDialogWidth"] = 564 + 156 * dialogGrowth;
        resources["SetupDialogWidth"] = 1060 + 420 * dialogGrowth;
        resources["BusinessSettingsCardWidth"]=1180+400*dialogGrowth;resources["BusinessSettingsContentWidth"]=1100+400*dialogGrowth;resources["BusinessSettingsTabHeight"]=650+200*dialogGrowth;
        var tileScale = Math.Min(scale, 1.35);
        resources["CategoryCardWidth"] = 162 * tileScale;
        resources["CategoryImageHeight"] = 78 * tileScale;
        resources["CategoryLabelHeight"] = 42 * tileScale;
        resources["ProductCardWidth"] = 186 * tileScale;
        resources["ProductImageHeight"] = 92 * tileScale;
        resources["ProductLabelHeight"] = 54 * tileScale;
        resources["ProductQuantityButtonSize"] = 34 * tileScale;
        resources["RegisterTileTextSize"] = 14 * tileScale;
        resources["RegisterTileIconSize"] = 20 * tileScale;
        resources["ProductTileInitialsSize"] = 28 * tileScale;
    }
}
