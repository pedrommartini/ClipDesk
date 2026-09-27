using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.ImageUpscaler;

public sealed class ImageUpscalerModule : IClipDeskPluginModule
{
    public const string ModuleId = "clipdesk.imageupscaler";
    public const int SchemaVersion = 2;

    public string Id => ModuleId;
    public int StateVersion => SchemaVersion;

    public PluginState CreateDefaultState() => ImageUpscalerState.CreateDefault();

    public PluginState NormalizeState(PluginState state) => ImageUpscalerState.Normalize(state);

    public ValueTask<PluginCommandResult> ExecuteAsync(
        PluginState state,
        PluginCommand command,
        IPluginExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        return ImageUpscalerCommands.ExecuteAsync(state, command, context, cancellationToken);
    }

    public string? GetClipboardText(PluginState state)
    {
        var normalized = ImageUpscalerState.Normalize(state);
        var factor = normalized.GetString(ImageUpscalerState.ScaleFactor) ?? "4x";
        var model = normalized.GetString(ImageUpscalerState.Model) ?? "balanced";
        var count = ImageUpscalerState.GetTotalImagesUpscaled(normalized);

        return $"Upscale de Imagem ClipDesk: Fator {factor}, Perfil {model}. Total ampliado: {count} imagens.";
    }
}
