namespace ClipDesk.Plugin.ImageCompressor.Models;

/// <summary>
/// Presets de qualidade para compressão JPEG e equilíbrio de compressão.
/// </summary>
public enum QualityPreset
{
    Economy = 55,
    Balanced = 75,
    Fidelity = 90,
    Custom = 0
}

/// <summary>
/// Formato de saída para o processamento de imagens.
/// </summary>
public enum OutputFormat
{
    Original,
    Jpeg,
    Png
}

/// <summary>
/// Limite de dimensão máxima (largura ou altura) para redimensionamento proporcional.
/// </summary>
public enum MaxDimension
{
    Original = 0,
    FullHd = 1920,
    Hd = 1280
}

/// <summary>
/// Métodos auxiliares e de conversão para QualityPreset.
/// </summary>
public static class QualityPresetExtensions
{
    public static int GetQuality(this QualityPreset preset, int customQuality = 75) => preset switch
    {
        QualityPreset.Economy => 55,
        QualityPreset.Balanced => 75,
        QualityPreset.Fidelity => 90,
        QualityPreset.Custom => Math.Clamp(customQuality, 10, 100),
        _ => 75
    };

    public static string ToWireString(this QualityPreset preset) => preset switch
    {
        QualityPreset.Economy => "economy",
        QualityPreset.Balanced => "balanced",
        QualityPreset.Fidelity => "fidelity",
        QualityPreset.Custom => "custom",
        _ => "balanced"
    };

    public static QualityPreset FromWireString(string? value) => (value?.Trim().ToLowerInvariant()) switch
    {
        "economy" => QualityPreset.Economy,
        "balanced" => QualityPreset.Balanced,
        "fidelity" => QualityPreset.Fidelity,
        "custom" => QualityPreset.Custom,
        _ => QualityPreset.Balanced
    };

    public static string GetDisplayName(this QualityPreset preset) => preset switch
    {
        QualityPreset.Economy => "Máxima Economia (55%)",
        QualityPreset.Balanced => "Equilibrado (75%)",
        QualityPreset.Fidelity => "Alta Fidelidade (90%)",
        QualityPreset.Custom => "Personalizado",
        _ => "Equilibrado (75%)"
    };
}

/// <summary>
/// Métodos auxiliares e de conversão para OutputFormat.
/// </summary>
public static class OutputFormatExtensions
{
    public static string ToWireString(this OutputFormat format) => format switch
    {
        OutputFormat.Original => "original",
        OutputFormat.Jpeg => "jpeg",
        OutputFormat.Png => "png",
        _ => "original"
    };

    public static OutputFormat FromWireString(string? value) => (value?.Trim().ToLowerInvariant()) switch
    {
        "original" => OutputFormat.Original,
        "jpeg" or "jpg" => OutputFormat.Jpeg,
        "png" => OutputFormat.Png,
        _ => OutputFormat.Original
    };

    public static string GetDisplayName(this OutputFormat format) => format switch
    {
        OutputFormat.Original => "Manter Original",
        OutputFormat.Jpeg => "JPEG (.jpg)",
        OutputFormat.Png => "PNG (.png)",
        _ => "Manter Original"
    };
}

/// <summary>
/// Métodos auxiliares e de conversão para MaxDimension.
/// </summary>
public static class MaxDimensionExtensions
{
    public static int GetPixelLimit(this MaxDimension dimension) => (int)dimension;

    public static string ToWireString(this MaxDimension dimension) => dimension switch
    {
        MaxDimension.Original => "original",
        MaxDimension.FullHd => "1920",
        MaxDimension.Hd => "1280",
        _ => "original"
    };

    public static MaxDimension FromWireString(string? value) => (value?.Trim().ToLowerInvariant()) switch
    {
        "original" or "0" => MaxDimension.Original,
        "1920" or "fullhd" or "fhd" => MaxDimension.FullHd,
        "1280" or "hd" or "720p" => MaxDimension.Hd,
        _ => MaxDimension.Original
    };

    public static string GetDisplayName(this MaxDimension dimension) => dimension switch
    {
        MaxDimension.Original => "Resolução Original",
        MaxDimension.FullHd => "Full HD (1920px)",
        MaxDimension.Hd => "HD (1280px)",
        _ => "Resolução Original"
    };
}
