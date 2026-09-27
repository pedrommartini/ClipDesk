using System.Text.Json;
using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.ImageCompressor;

public sealed record CompressorSharedItem(string InputAssetId, string OutputAssetId = "", int OutputWidth = 0, int OutputHeight = 0);

public static class CompressorSharedState
{
    public const string Single = "singleInput";
    public const string Batch = "batchInputs";
    public const string BatchMode = "batchMode";
    public const int MaximumBatchItems = 100;

    public static CompressorSharedItem? ReadSingle(PluginState state) => Read(state.GetString(Single)).FirstOrDefault();
    public static List<CompressorSharedItem> ReadBatch(PluginState state) => Read(state.GetString(Batch));
    public static List<CompressorSharedItem> Read(string? json)
    {
        try
        {
            var entries = JsonSerializer.Deserialize<List<CompressorSharedItem>>(json ?? "") ?? [];
            return entries.Take(MaximumBatchItems).Where(e => e is not null && Guid.TryParse(e.InputAssetId, out _))
                .Select(e => e with { InputAssetId = Guid.Parse(e.InputAssetId).ToString("N"),
                    OutputAssetId = Guid.TryParse(e.OutputAssetId, out var output) ? output.ToString("N") : "",
                    OutputWidth = Math.Max(0, e.OutputWidth), OutputHeight = Math.Max(0, e.OutputHeight) })
                .DistinctBy(e => e.InputAssetId).ToList();
        }
        catch (JsonException) { return []; }
    }
    public static PluginState WithItems(PluginState state, CompressorSharedItem? single, IEnumerable<CompressorSharedItem> batch, bool batchMode) =>
        state.With(Single, JsonSerializer.Serialize(single is null ? new List<CompressorSharedItem>() : [single]))
            .With(Batch, JsonSerializer.Serialize(batch.Take(MaximumBatchItems)))
            .With(BatchMode, batchMode ? "true" : "false");
}
