namespace ClipDesk.Plugin.ImageUpscaler.Models;

public enum UpscaleFactor
{
    Scale2x = 2,
    Scale4x = 4,
    Scale8x = 8
}

public enum UpscaleModel
{
    Balanced,
    Photo,
    Illustration,
    CrispText
}

public enum UpscaleEngine
{
    LocalNative,
    CloudAi
}

public enum DenoiseLevel
{
    Off,
    Low,
    Medium,
    High
}

public enum OutputFormat
{
    Original,
    Png,
    Jpeg,
    Webp
}

public static class EnumExtensions
{
    public static string ToWireString(this UpscaleFactor factor) => factor switch
    {
        UpscaleFactor.Scale2x => "2x",
        UpscaleFactor.Scale4x => "4x",
        UpscaleFactor.Scale8x => "8x",
        _ => "4x"
    };

    public static UpscaleFactor ParseUpscaleFactor(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "2x" or "2" => UpscaleFactor.Scale2x,
        "4x" or "4" => UpscaleFactor.Scale4x,
        "8x" or "8" => UpscaleFactor.Scale8x,
        _ => UpscaleFactor.Scale4x
    };

    public static string ToWireString(this UpscaleModel model) => model switch
    {
        UpscaleModel.Balanced => "balanced",
        UpscaleModel.Photo => "photo",
        UpscaleModel.Illustration => "illustration",
        UpscaleModel.CrispText => "crisp-text",
        _ => "balanced"
    };

    public static UpscaleModel ParseUpscaleModel(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "photo" or "portrait" => UpscaleModel.Photo,
        "illustration" or "anime" or "art" => UpscaleModel.Illustration,
        "crisp-text" or "text" or "document" => UpscaleModel.CrispText,
        _ => UpscaleModel.Balanced
    };

    public static string ToWireString(this UpscaleEngine engine) => engine switch
    {
        UpscaleEngine.CloudAi => "cloud",
        _ => "local"
    };

    public static UpscaleEngine ParseUpscaleEngine(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "cloud" or "ai" or "cloudai" => UpscaleEngine.CloudAi,
        _ => UpscaleEngine.LocalNative
    };

    public static string ToWireString(this DenoiseLevel level) => level switch
    {
        DenoiseLevel.Off => "off",
        DenoiseLevel.Low => "low",
        DenoiseLevel.Medium => "medium",
        DenoiseLevel.High => "high",
        _ => "low"
    };

    public static DenoiseLevel ParseDenoiseLevel(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "off" or "none" or "0" => DenoiseLevel.Off,
        "low" or "1" => DenoiseLevel.Low,
        "medium" or "med" or "2" => DenoiseLevel.Medium,
        "high" or "3" => DenoiseLevel.High,
        _ => DenoiseLevel.Low
    };

    public static string ToWireString(this OutputFormat format) => format switch
    {
        OutputFormat.Png => "png",
        OutputFormat.Jpeg => "jpeg",
        OutputFormat.Webp => "webp",
        _ => "original"
    };

    public static OutputFormat ParseOutputFormat(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "png" => OutputFormat.Png,
        "jpeg" or "jpg" => OutputFormat.Jpeg,
        "webp" => OutputFormat.Webp,
        _ => OutputFormat.Original
    };
}
