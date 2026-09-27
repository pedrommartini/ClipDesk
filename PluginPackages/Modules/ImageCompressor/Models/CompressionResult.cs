namespace ClipDesk.Plugin.ImageCompressor.Models;

/// <summary>
/// Resultado detalhado da compressão de uma imagem individual.
/// </summary>
public sealed class CompressionResult
{
    public bool Success { get; init; }
    public string SourcePath { get; init; } = string.Empty;
    public string? OutputPath { get; init; }
    public long OriginalBytes { get; init; }
    public long CompressedBytes { get; init; }
    public int OriginalWidth { get; init; }
    public int OriginalHeight { get; init; }
    public int OutputWidth { get; init; }
    public int OutputHeight { get; init; }
    public string? ErrorMessage { get; init; }

    public long SavedBytes => Math.Max(0, OriginalBytes - CompressedBytes);

    public double ReductionPercentage => OriginalBytes > 0
        ? Math.Clamp((OriginalBytes - CompressedBytes) / (double)OriginalBytes * 100.0, 0.0, 100.0)
        : 0.0;

    public static CompressionResult Succeeded(
        string sourcePath,
        string outputPath,
        long originalBytes,
        long compressedBytes,
        int originalWidth,
        int originalHeight,
        int outputWidth,
        int outputHeight) => new()
    {
        Success = true,
        SourcePath = sourcePath,
        OutputPath = outputPath,
        OriginalBytes = originalBytes,
        CompressedBytes = compressedBytes,
        OriginalWidth = originalWidth,
        OriginalHeight = originalHeight,
        OutputWidth = outputWidth,
        OutputHeight = outputHeight
    };

    public static CompressionResult Failed(string sourcePath, string errorMessage) => new()
    {
        Success = false,
        SourcePath = sourcePath,
        ErrorMessage = errorMessage
    };
}
