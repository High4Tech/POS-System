using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SupermarketPos.Domain.Entities;

namespace SupermarketPos.Wpf.Common;

public sealed class ProductTextFontConverter:IValueConverter
{
    private static readonly FontFamily Latin=new("Segoe UI");
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>value is string text&&text.Any(c=>c is >= '\u0600' and <= '\u08ff' or >= '\ufb50' and <= '\ufeff')?Localization.LocalizationManager.Instance.FontFamily:Latin;
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}

/// <summary>Deterministically maps an integer (category id/index) to one of the vibrant hues.</summary>
public sealed class CategoryColorConverter : IValueConverter
{
    public static readonly Color[] Palette =
    {
        Color.FromRgb(0xEF, 0x44, 0x44), // red
        Color.FromRgb(0x8B, 0x5C, 0xF6), // violet
        Color.FromRgb(0xEC, 0x48, 0x99), // pink
        Color.FromRgb(0xF5, 0x9E, 0x0B), // amber
        Color.FromRgb(0x10, 0xB9, 0x81), // emerald
        Color.FromRgb(0x3B, 0x82, 0xF6), // blue
        Color.FromRgb(0x14, 0xB8, 0xA6), // teal
        Color.FromRgb(0xF9, 0x73, 0x16), // orange
    };

    public static SolidColorBrush Brush(int key) => Brush(null, key);

    public static SolidColorBrush Brush(string? hex, int fallback = 0)
    {
        var color = hex is null
            ? Palette[((fallback % Palette.Length) + Palette.Length) % Palette.Length]
            : (Color)ColorConverter.ConvertFromString(hex);
        var b = new SolidColorBrush(color);
        b.Freeze();
        return b;
    }

    public static SolidColorBrush TileBrush(string? hex, int fallback)
    {
        var brush = Brush(hex, fallback);
        var color = brush.Color;
        var luminance = 0.2126f * color.ScR + 0.7152f * color.ScG + 0.0722f * color.ScB;
        const float whiteTextLimit = 0.175f;
        if (luminance <= whiteTextLimit) return brush;

        // Keep the chosen hue and lower its luminance enough for white labels.
        var scale = whiteTextLimit / luminance;
        var dark = new SolidColorBrush(Color.FromScRgb(1f, color.ScR * scale, color.ScG * scale, color.ScB * scale));
        dark.Freeze();
        return dark;
    }

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            Category category => Brush(category.ColorHex, category.Id),
            string color => Brush(color),
            int index => Brush(index),
            _ => Brush(0)
        };

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Loads an ImageSource from a file path; returns null (no image) on missing/invalid path.</summary>
public sealed class PathToImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var path = PosStorage.ResolveImagePath(value as string);
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return null;
        try
        {
            using var stream = System.IO.File.OpenRead(path);
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.StreamSource = stream;
            bmp.DecodePixelWidth = parameter is string width && int.TryParse(width, out var pixels) ? pixels : 220;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RegisterTileWidthConverter : IMultiValueConverter
{
    public double StandardWidth { get; set; }
    public double Gap { get; set; }

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values[1] is not double preferred) return StandardWidth;
        if (values[0] is not double viewport || viewport <= Gap || !double.IsFinite(viewport)) return preferred;
        if (preferred <= StandardWidth) return Math.Min(preferred, viewport - Gap);

        var standardColumns = (int)(viewport / (StandardWidth + Gap));
        var preferredColumns = (int)Math.Round(viewport / (preferred + Gap));
        var columns = Math.Max(1, Math.Max(preferredColumns, Math.Min(5, standardColumns)));
        var fittedWidth = Math.Floor(viewport / columns) - Gap;
        return columns < 3 ? Math.Min(preferred, fittedWidth) : fittedWidth;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True when there is a usable image at the given path (for placeholder toggling).</summary>
public sealed class HasImageToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        System.IO.File.Exists(PosStorage.ResolveImagePath(value as string))
            ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Up to two uppercase initials from a name, for avatar placeholders.</summary>
public sealed class InitialsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = (value as string ?? string.Empty).Trim();
        if (name.Length == 0) return "?";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var s = parts.Length >= 2 ? $"{parts[0][0]}{parts[1][0]}" : name.Substring(0, Math.Min(2, name.Length));
        return s.ToUpper(CultureInfo.CurrentCulture);
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True -> Collapsed, False -> Visible (used to show the login screen while signed out).</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}

/// <summary>Non-empty string -> Visible, empty/null -> Collapsed.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
