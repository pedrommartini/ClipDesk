using ClipDesk.Plugin.ImageCompressor.Models;
using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.ImageCompressor;

public sealed record PluginCommandContext(
    PluginState State,
    PluginCommand Command,
    IPluginExecutionContext? ExecutionContext = null,
    CancellationToken CancellationToken = default);

public sealed class ImageCompressorModule : IClipDeskPluginModule
{
    public const string PluginId = "clipdesk.imagecompressor";
    public const int CurrentStateVersion = 2;

    public string Id => PluginId;
    public int StateVersion => CurrentStateVersion;

    public PluginState CreateDefaultState() => ImageCompressorState.CreateDefault();

    public PluginState NormalizeState(PluginState state) => ImageCompressorState.Normalize(state);

    public ValueTask<PluginCommandResult> ExecuteAsync(
        PluginState state,
        PluginCommand command,
        IPluginExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ImageCompressorCommands.ExecuteAsync(state, command, context, cancellationToken);
    }

    public Task<PluginCommandResult> ExecuteAsync(PluginCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ExecuteAsync(context.State, context.Command, context.ExecutionContext!, context.CancellationToken).AsTask();
    }

    public string? GetClipboardText(PluginState state)
    {
        var normalized = NormalizeState(state);
        var count = ImageCompressorState.GetTotalFilesCompressed(normalized);
        var bytes = ImageCompressorState.GetTotalBytesSaved(normalized);
        var quality = ImageCompressorState.GetQuality(normalized);
        var preset = ImageCompressorState.GetPreset(normalized);
        var format = ImageCompressorState.GetOutputFormat(normalized);
        var dim = ImageCompressorState.GetMaxDimension(normalized);

        var dimStr = dim switch
        {
            MaxDimension.FullHd => "Full HD (1920px)",
            MaxDimension.Hd => "HD (1280px)",
            _ => "Original"
        };

        var formatStr = format switch
        {
            OutputFormat.Jpeg => "JPEG",
            OutputFormat.Png => "PNG",
            _ => "Original"
        };

        var presetStr = preset switch
        {
            QualityPreset.Economy => "Máxima Economia (55%)",
            QualityPreset.Balanced => "Equilibrado (75%)",
            QualityPreset.Fidelity => "Alta Fidelidade (90%)",
            _ => $"Personalizado ({quality}%)"
        };

        return $"🖼️ Compressor de Imagens ClipDesk\n" +
               $"• Arquivos Otimizados: {count:N0}\n" +
               $"• Economia Total: {ImageCompressorCommands.FormatBytes(bytes)}\n" +
               $"• Configuração: {presetStr} | Formato: {formatStr} | Resolução: {dimStr}";
    }
}
