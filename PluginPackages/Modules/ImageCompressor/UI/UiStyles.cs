using System.Windows.Media;

namespace ClipDesk.Plugin.ImageCompressor.UI;

/// <summary>
/// Provedor central de estilos, paletas, contraste dinâmico e recursos visuais para a interface do plugin.
/// </summary>
public static class UiStyles
{
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol");
    public static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI");

    public static SolidColorBrush GetBrush(string? hex, string fallback = "#0EA5E9")
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return CreateFrozenBrush(fallback);
        }

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex.Trim());
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return CreateFrozenBrush(fallback);
        }
    }

    private static SolidColorBrush CreateFrozenBrush(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static SolidColorBrush GetContrastBrush(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Brushes.White;

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex.Trim());
            // Fórmula padrão ITU-R BT.601 para luminância perceptível
            double luminance = ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255.0;
            var contrastColor = luminance > 0.55 ? (Color)ColorConverter.ConvertFromString("#0F172A") : Colors.White;
            var brush = new SolidColorBrush(contrastColor);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return Brushes.White;
        }
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB"];
        int unitIndex = 0;
        double size = bytes;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }
        return unitIndex == 0 ? $"{size:0} {units[unitIndex]}" : $"{size:0.##} {units[unitIndex]}";
    }
}
