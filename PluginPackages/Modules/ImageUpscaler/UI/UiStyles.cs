using System.Collections.Concurrent;
using System.Windows.Media;

namespace ClipDesk.Plugin.ImageUpscaler.UI;

public static class UiStyles
{
    private static readonly ConcurrentDictionary<string, SolidColorBrush> BrushCache = new(StringComparer.OrdinalIgnoreCase);

    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol, Arial");
    public static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI, sans-serif");

    public static SolidColorBrush GetBrush(string hexColor, string fallbackHex = "#6366F1")
    {
        if (string.IsNullOrWhiteSpace(hexColor)) hexColor = fallbackHex;
        return BrushCache.GetOrAdd(hexColor, hex =>
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch
            {
                var color = (Color)ColorConverter.ConvertFromString(fallbackHex);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
        });
    }

    public static SolidColorBrush GetContrastBrush(string hexColor)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hexColor);
            var luminance = (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;
            return luminance > 0.55 ? GetBrush("#0F172A") : GetBrush("#FFFFFF");
        }
        catch
        {
            return GetBrush("#FFFFFF");
        }
    }
}
