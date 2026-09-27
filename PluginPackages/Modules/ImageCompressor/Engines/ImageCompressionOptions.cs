using System.IO;
using ClipDesk.Plugin.ImageCompressor.Models;

namespace ClipDesk.Plugin.ImageCompressor.Engines;

/// <summary>
/// Opções configuráveis para execução do motor de compressão WIC.
/// </summary>
public sealed class ImageCompressionOptions
{
    private int _quality = 75;

    public int Quality
    {
        get => _quality;
        set => _quality = Math.Clamp(value, 10, 100);
    }

    public QualityPreset Preset { get; set; } = QualityPreset.Balanced;

    public OutputFormat Format { get; set; } = OutputFormat.Original;

    public MaxDimension Dimension { get; set; } = MaxDimension.Original;

    public bool StripMetadata { get; set; } = true;

    public static string DefaultOutputDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Clipdesk", "Image Compressor");

    public string OutputDirectory { get; set; } = DefaultOutputDirectory;

    public static ImageCompressionOptions FromState(ImageCompressorState state)
    {
        return new ImageCompressionOptions
        {
            Quality = state.QualityLevel,
            Preset = state.PresetLevel,
            Format = state.Format,
            Dimension = state.DimensionLimit,
            StripMetadata = state.StripMetadataEnabled
        };
    }
}
