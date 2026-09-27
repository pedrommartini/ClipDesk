using System.Globalization;
using ClipDesk.Plugin.ImageUpscaler.Models;
using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.ImageUpscaler;

public static class ImageUpscalerStateKeys
{
    public const string SchemaVersion = "$schema";
    public const string ScaleFactor = "scaleFactor";
    public const string Model = "model";
    public const string Engine = "engine";
    public const string Sharpness = "sharpness";
    public const string Denoise = "denoise";
    public const string OutputFormat = "outputFormat";
    public const string FaceEnhance = "faceEnhance";
    public const string CloudEndpoint = "cloudEndpoint";
    public const string TotalImagesUpscaled = "totalImagesUpscaled";
    public const string TotalPixelsGenerated = "totalPixelsGenerated";
    public const string LastMessage = "lastMessage";
}

public sealed class ImageUpscalerState
{
    public const string SchemaVersion = ImageUpscalerStateKeys.SchemaVersion;
    public const string ScaleFactor = ImageUpscalerStateKeys.ScaleFactor;
    public const string Model = ImageUpscalerStateKeys.Model;
    public const string Engine = ImageUpscalerStateKeys.Engine;
    public const string Sharpness = ImageUpscalerStateKeys.Sharpness;
    public const string Denoise = ImageUpscalerStateKeys.Denoise;
    public const string OutputFormat = ImageUpscalerStateKeys.OutputFormat;
    public const string FaceEnhance = ImageUpscalerStateKeys.FaceEnhance;
    public const string CloudEndpoint = ImageUpscalerStateKeys.CloudEndpoint;
    public const string TotalImagesUpscaled = ImageUpscalerStateKeys.TotalImagesUpscaled;
    public const string TotalPixelsGenerated = ImageUpscalerStateKeys.TotalPixelsGenerated;
    public const string LastMessage = ImageUpscalerStateKeys.LastMessage;

    public const string CurrentSchemaVersion = "2";
    public const string DefaultScaleFactor = "4x";
    public const string DefaultModel = "balanced";
    public const string DefaultEngine = "local";
    public const int DefaultSharpness = 65;
    public const string DefaultSharpnessString = "65";
    public const string DefaultDenoise = "low";
    public const string DefaultOutputFormat = "original";
    public const string DefaultFaceEnhance = "false";
    public const string DefaultCloudEndpoint = "";
    public const string DefaultTotalImagesUpscaled = "0";
    public const string DefaultTotalPixelsGenerated = "0";
    public const string DefaultLastMessage = "Pronto para ampliar";

    public const int MinSharpness = 0;
    public const int MaxSharpness = 100;

    private readonly PluginState _state;

    public ImageUpscalerState(PluginState? state = null)
    {
        _state = Normalize(state ?? CreateDefault());
    }

    public PluginState RawState => _state;
    public UpscaleFactor Factor => EnumExtensions.ParseUpscaleFactor(_state.GetString(ScaleFactor));
    public UpscaleModel ModelType => EnumExtensions.ParseUpscaleModel(_state.GetString(Model));
    public UpscaleEngine EngineType => EnumExtensions.ParseUpscaleEngine(_state.GetString(Engine));
    public int SharpnessLevel => GetSharpness(_state);
    public DenoiseLevel DenoiseSetting => EnumExtensions.ParseDenoiseLevel(_state.GetString(Denoise));
    public OutputFormat Format => EnumExtensions.ParseOutputFormat(_state.GetString(OutputFormat));
    public bool FaceEnhanceEnabled => string.Equals(_state.GetString(FaceEnhance), "true", StringComparison.OrdinalIgnoreCase);
    public string CloudApiEndpoint => _state.GetString(CloudEndpoint) ?? "";
    public long ImagesUpscaledCount => GetTotalImagesUpscaled(_state);
    public long PixelsGeneratedCount => GetTotalPixelsGenerated(_state);
    public string StatusMessage => _state.GetString(LastMessage) ?? DefaultLastMessage;

    public static PluginState CreateDefault() => new(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [SchemaVersion] = CurrentSchemaVersion,
        [ScaleFactor] = DefaultScaleFactor,
        [Model] = DefaultModel,
        [Engine] = DefaultEngine,
        [Sharpness] = DefaultSharpnessString,
        [Denoise] = DefaultDenoise,
        [OutputFormat] = DefaultOutputFormat,
        [FaceEnhance] = DefaultFaceEnhance,
        [CloudEndpoint] = DefaultCloudEndpoint,
        [TotalImagesUpscaled] = DefaultTotalImagesUpscaled,
        [TotalPixelsGenerated] = DefaultTotalPixelsGenerated,
        [LastMessage] = DefaultLastMessage,
        [UpscaleSharedState.InputAsset] = "",
        [UpscaleSharedState.OutputAsset] = "",
        [UpscaleSharedState.OutputInfo] = ""
    });

    public static PluginState Normalize(PluginState? state)
    {
        var values = state?.ToDictionary() ?? new Dictionary<string, string>(StringComparer.Ordinal);

        values[SchemaVersion] = CurrentSchemaVersion;
        values[UpscaleSharedState.InputAsset] = UpscaleSharedState.AssetId(state?.GetString(UpscaleSharedState.InputAsset));
        values[UpscaleSharedState.OutputAsset] = UpscaleSharedState.AssetId(state?.GetString(UpscaleSharedState.OutputAsset));
        var output = state is null ? null : UpscaleSharedState.ReadOutput(state);
        values[UpscaleSharedState.OutputInfo] = output is null ? "" : System.Text.Json.JsonSerializer.Serialize(output);

        // Fator de escala
        var factor = EnumExtensions.ParseUpscaleFactor(values.TryGetValue(ScaleFactor, out var sf) ? sf : null);
        values[ScaleFactor] = factor.ToWireString();

        // Modelo
        var model = EnumExtensions.ParseUpscaleModel(values.TryGetValue(Model, out var m) ? m : null);
        values[Model] = model.ToWireString();

        // Engine
        var engine = EnumExtensions.ParseUpscaleEngine(values.TryGetValue(Engine, out var eng) ? eng : null);
        values[Engine] = engine.ToWireString();

        // Nitidez (0 a 100)
        int sharpness = DefaultSharpness;
        if (values.TryGetValue(Sharpness, out var rawSharpness) &&
            int.TryParse(rawSharpness, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSharpness))
        {
            sharpness = Math.Clamp(parsedSharpness, MinSharpness, MaxSharpness);
        }
        values[Sharpness] = sharpness.ToString(CultureInfo.InvariantCulture);

        // Denoise
        var denoise = EnumExtensions.ParseDenoiseLevel(values.TryGetValue(Denoise, out var d) ? d : null);
        values[Denoise] = denoise.ToWireString();

        // Formato
        var format = EnumExtensions.ParseOutputFormat(values.TryGetValue(OutputFormat, out var fmt) ? fmt : null);
        values[OutputFormat] = format.ToWireString();

        // Face enhance
        var face = values.TryGetValue(FaceEnhance, out var f) && string.Equals(f, "true", StringComparison.OrdinalIgnoreCase);
        values[FaceEnhance] = face ? "true" : "false";

        // Cloud endpoint
        if (!values.ContainsKey(CloudEndpoint)) values[CloudEndpoint] = DefaultCloudEndpoint;

        // Métricas acumuladas
        if (!values.TryGetValue(TotalImagesUpscaled, out var rawCount) ||
            !long.TryParse(rawCount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count < 0)
        {
            values[TotalImagesUpscaled] = DefaultTotalImagesUpscaled;
        }

        if (!values.TryGetValue(TotalPixelsGenerated, out var rawPixels) ||
            !long.TryParse(rawPixels, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pixels) || pixels < 0)
        {
            values[TotalPixelsGenerated] = DefaultTotalPixelsGenerated;
        }

        if (!values.ContainsKey(LastMessage) || string.IsNullOrWhiteSpace(values[LastMessage]))
        {
            values[LastMessage] = DefaultLastMessage;
        }

        return new PluginState(values);
    }

    public static int GetSharpness(PluginState state)
    {
        var raw = state.GetString(Sharpness);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val)
            ? Math.Clamp(val, MinSharpness, MaxSharpness)
            : DefaultSharpness;
    }

    public static long GetTotalImagesUpscaled(PluginState state)
    {
        var raw = state.GetString(TotalImagesUpscaled);
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val) && val >= 0
            ? val
            : 0;
    }

    public static long GetTotalPixelsGenerated(PluginState state)
    {
        var raw = state.GetString(TotalPixelsGenerated);
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val) && val >= 0
            ? val
            : 0;
    }
}
