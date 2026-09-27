namespace ClipDesk.Plugin.ImageUpscaler.Models;

public sealed record UpscaleResult(
    string SourcePath,
    string OutputPath,
    int OriginalWidth,
    int OriginalHeight,
    int UpscaledWidth,
    int UpscaledHeight,
    long OriginalSizeBytes,
    long UpscaledSizeBytes,
    TimeSpan Elapsed,
    UpscaleFactor Factor,
    UpscaleModel Model,
    string EngineUsed,
    bool Succeeded,
    string? ErrorMessage = null)
{
    public double ScaleMultiplier => (double)UpscaledWidth / Math.Max(1, OriginalWidth);
    public double MegapixelsOriginal => (double)OriginalWidth * OriginalHeight / 1_000_000.0;
    public double MegapixelsUpscaled => (double)UpscaledWidth * UpscaledHeight / 1_000_000.0;
}
