using System.Text.Json;
using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.ImageUpscaler;

/// <summary>Portable references and result metadata; Windows paths never enter the document.</summary>
public static class UpscaleSharedState
{
    public const string InputAsset = "inputAssetId";
    public const string OutputAsset = "outputAssetId";
    public const string OutputInfo = "outputInfo";

    public static string AssetId(string? value) => Guid.TryParse(value, out var id) ? id.ToString("N") : "";
    public static UpscaleOutputInfo? ReadOutput(PluginState state)
    {
        try
        {
            var info = JsonSerializer.Deserialize<UpscaleOutputInfo>(state.GetString(OutputInfo) ?? "");
            return info is { Width: > 0, Height: > 0, ElapsedMs: >= 0 } && double.IsFinite(info.ElapsedMs) ? info : null;
        }
        catch (JsonException) { return null; }
    }
}

public sealed record UpscaleOutputInfo(int Width, int Height, double ElapsedMs, string Factor, string Model, string Engine);
