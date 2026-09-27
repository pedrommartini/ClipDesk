using System.Globalization;
using ClipDesk.Plugin.ImageCompressor.Models;
using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.ImageCompressor;

public static class ImageCompressorStateKeys
{
    public const string SchemaVersion = "$schema";
    public const string Quality = "quality";
    public const string Preset = "preset";
    public const string MaxDimension = "maxDimension";
    public const string OutputFormat = "outputFormat";
    public const string StripMetadata = "stripMetadata";
    public const string TotalFilesCompressed = "totalFilesCompressed";
    public const string TotalBytesSaved = "totalBytesSaved";
    public const string LastMessage = "lastMessage";
}

public sealed class ImageCompressorState
{
    public const string SchemaVersion = ImageCompressorStateKeys.SchemaVersion;
    public const string Quality = ImageCompressorStateKeys.Quality;
    public const string Preset = ImageCompressorStateKeys.Preset;
    public const string MaxDimension = ImageCompressorStateKeys.MaxDimension;
    public const string OutputFormat = ImageCompressorStateKeys.OutputFormat;
    public const string StripMetadata = ImageCompressorStateKeys.StripMetadata;
    public const string TotalFilesCompressed = ImageCompressorStateKeys.TotalFilesCompressed;
    public const string TotalBytesSaved = ImageCompressorStateKeys.TotalBytesSaved;
    public const string LastMessage = ImageCompressorStateKeys.LastMessage;

    public const string CurrentSchemaVersion = "2";
    public const int DefaultQuality = 75;
    public const int MinQuality = 10;
    public const int MaxQuality = 100;

    public const string DefaultQualityString = "75";
    public const string DefaultPreset = "balanced";
    public const string DefaultMaxDimension = "original";
    public const string DefaultOutputFormat = "original";
    public const string DefaultStripMetadata = "true";
    public const string DefaultTotalFilesCompressed = "0";
    public const string DefaultTotalBytesSaved = "0";
    public const string DefaultLastMessage = "Pronto para comprimir";

    private readonly PluginState _state;

    public ImageCompressorState(PluginState? state = null)
    {
        _state = Normalize(state ?? CreateDefault());
    }

    public PluginState RawState => _state;
    public int QualityLevel => GetQuality(_state);
    public QualityPreset PresetLevel => GetPreset(_state);
    public OutputFormat Format => GetOutputFormat(_state);
    public MaxDimension DimensionLimit => GetMaxDimension(_state);
    public bool StripMetadataEnabled => GetStripMetadata(_state);
    public long FilesCompressedCount => GetTotalFilesCompressed(_state);
    public long BytesSavedCount => GetTotalBytesSaved(_state);
    public string StatusMessage => GetLastMessage(_state);

    public static PluginState CreateDefault() => new(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [SchemaVersion] = CurrentSchemaVersion,
        [Quality] = DefaultQualityString,
        [Preset] = DefaultPreset,
        [MaxDimension] = DefaultMaxDimension,
        [OutputFormat] = DefaultOutputFormat,
        [StripMetadata] = DefaultStripMetadata,
        [TotalFilesCompressed] = DefaultTotalFilesCompressed,
        [TotalBytesSaved] = DefaultTotalBytesSaved,
        [LastMessage] = DefaultLastMessage,
        [CompressorSharedState.Single] = "[]",
        [CompressorSharedState.Batch] = "[]",
        [CompressorSharedState.BatchMode] = "false"
    });

    public static PluginState Normalize(PluginState? state)
    {
        var values = state?.ToDictionary() ?? new Dictionary<string, string>(StringComparer.Ordinal);

        // 1. O schema é sempre fixado em "1"
        values[SchemaVersion] = CurrentSchemaVersion;

        // 2. Qualidade restrita a [10, 100], com fallback para 75
        int quality = DefaultQuality;
        if (values.TryGetValue(Quality, out var rawQuality) &&
            int.TryParse(rawQuality, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedQuality))
        {
            quality = Math.Clamp(parsedQuality, MinQuality, MaxQuality);
        }
        values[Quality] = quality.ToString(CultureInfo.InvariantCulture);

        // 3. Preset validado contra os valores suportados
        var presetStr = values.TryGetValue(Preset, out var rawPreset) ? rawPreset?.Trim().ToLowerInvariant() : null;
        if (presetStr is not ("economy" or "balanced" or "fidelity" or "custom"))
        {
            presetStr = DefaultPreset;
        }
        values[Preset] = presetStr;

        // 4. Formato de saída normalizado
        var formatStr = values.TryGetValue(OutputFormat, out var rawFormat) ? rawFormat?.Trim().ToLowerInvariant() : null;
        if (formatStr is "jpeg" or "jpg")
        {
            formatStr = "jpeg";
        }
        else if (formatStr is "png")
        {
            formatStr = "png";
        }
        else
        {
            formatStr = DefaultOutputFormat;
        }
        values[OutputFormat] = formatStr;

        // 5. Dimensão máxima normalizada
        var dimStr = values.TryGetValue(MaxDimension, out var rawDim) ? rawDim?.Trim().ToLowerInvariant() : null;
        if (dimStr is "1920" or "fullhd" or "fhd")
        {
            dimStr = "1920";
        }
        else if (dimStr is "1280" or "hd" or "720p")
        {
            dimStr = "1280";
        }
        else
        {
            dimStr = DefaultMaxDimension;
        }
        values[MaxDimension] = dimStr;

        // 6. StripMetadata: "false" se expressamente falso, caso contrário "true"
        var stripStr = values.TryGetValue(StripMetadata, out var rawStrip) ? rawStrip : null;
        values[StripMetadata] = string.Equals(stripStr, "false", StringComparison.OrdinalIgnoreCase) ? "false" : "true";

        // 7. TotalFilesCompressed: protegido contra valores negativos ou corrupção
        long totalFiles = 0;
        if (values.TryGetValue(TotalFilesCompressed, out var rawFiles) &&
            long.TryParse(rawFiles, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedFiles) &&
            parsedFiles >= 0)
        {
            totalFiles = parsedFiles;
        }
        values[TotalFilesCompressed] = totalFiles.ToString(CultureInfo.InvariantCulture);

        // 8. TotalBytesSaved: protegido contra valores negativos ou corrupção
        long totalBytes = 0;
        if (values.TryGetValue(TotalBytesSaved, out var rawBytes) &&
            long.TryParse(rawBytes, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedBytes) &&
            parsedBytes >= 0)
        {
            totalBytes = parsedBytes;
        }
        values[TotalBytesSaved] = totalBytes.ToString(CultureInfo.InvariantCulture);

        // 9. LastMessage: preservado ou preenchido com mensagem inicial
        var msgStr = values.TryGetValue(LastMessage, out var rawMsg) ? rawMsg?.Trim() : null;
        values[LastMessage] = string.IsNullOrEmpty(msgStr) ? DefaultLastMessage : msgStr;

        var normalized = new PluginState(values);
        return CompressorSharedState.WithItems(normalized, CompressorSharedState.ReadSingle(normalized), CompressorSharedState.ReadBatch(normalized), normalized.GetString(CompressorSharedState.BatchMode) == "true");
    }

    public static int GetQuality(PluginState state)
    {
        var raw = state.GetString(Quality);
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var q))
        {
            return Math.Clamp(q, MinQuality, MaxQuality);
        }
        return DefaultQuality;
    }

    public static QualityPreset GetPreset(PluginState state) =>
        QualityPresetExtensions.FromWireString(state.GetString(Preset));

    public static OutputFormat GetOutputFormat(PluginState state) =>
        OutputFormatExtensions.FromWireString(state.GetString(OutputFormat));

    public static MaxDimension GetMaxDimension(PluginState state) =>
        MaxDimensionExtensions.FromWireString(state.GetString(MaxDimension));

    public static bool GetStripMetadata(PluginState state) =>
        !string.Equals(state.GetString(StripMetadata), "false", StringComparison.OrdinalIgnoreCase);

    public static long GetTotalFilesCompressed(PluginState state)
    {
        var raw = state.GetString(TotalFilesCompressed);
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val) && val >= 0
            ? val
            : 0;
    }

    public static long GetTotalBytesSaved(PluginState state)
    {
        var raw = state.GetString(TotalBytesSaved);
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val) && val >= 0
            ? val
            : 0;
    }

    public static string GetLastMessage(PluginState state) =>
        state.GetString(LastMessage) ?? DefaultLastMessage;

    public static implicit operator PluginState(ImageCompressorState typed) => typed.RawState;
    public static implicit operator ImageCompressorState(PluginState state) => new(state);
}

public static class ImageCompressorStateExtensions
{
    public static ImageCompressorState AsImageCompressor(this PluginState state) => new(state);
}
