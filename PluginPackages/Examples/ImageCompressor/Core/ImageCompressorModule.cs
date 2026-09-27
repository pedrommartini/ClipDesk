using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.ImageCompressor;

/// <summary>Portable file/image workflow; codec and file access belong to the host.</summary>
public sealed class ImageCompressorModule : IClipDeskPluginModuleV3
{
    public string Id => "clipdesk.image-compressor-example";
    public int StateVersion => 1;
    public PluginState CreateDefaultState() => new PluginState().With(PluginStateKeys.SchemaVersion, "1");
    public PluginState NormalizeState(PluginState state) => state.With(PluginStateKeys.SchemaVersion, "1");

    public async ValueTask<PluginCommandResult> ExecuteAsync(PluginState state, PluginCommand command,
        IPluginCapabilityProvider capabilities, CancellationToken cancellationToken = default)
    {
        if (command.Name != "compress") return PluginCommandResult.Invalid(state, "Comando desconhecido.");
        var reads = capabilities.Resolve<IPluginFileRead>(PluginHostCapabilityIds.FileRead, new(1, 0, 0));
        var writes = capabilities.Resolve<IPluginFileWrite>(PluginHostCapabilityIds.FileWrite, new(1, 0, 0));
        var images = capabilities.Resolve<IPluginImageProcessing>(PluginHostCapabilityIds.ImageProcessing, new(1, 0, 0));
        if (!reads.Available || !writes.Available || !images.Available)
            return new(state, PluginCommandStatus.Unavailable, "Arquivos ou processamento de imagem indisponíveis neste host.");

        var selection = await reads.Value!.PickOpenAsync(false, cancellationToken);
        if (selection.Count == 0) return new(state, PluginCommandStatus.Unavailable, "Nenhuma imagem selecionada.");
        await using var sourceHandle = selection[0];
        if (sourceHandle.ContentType is not ("image/png" or "image/jpeg" or "image/webp"))
            return PluginCommandResult.Invalid(state, "Selecione PNG, JPEG ou WebP.");
        var destinationHandle = await writes.Value!.PickSaveAsync("imagem-comprimida.webp", cancellationToken);
        if (destinationHandle is null) return new(state, PluginCommandStatus.Unavailable, "Salvamento cancelado.");
        await using var destination = destinationHandle;
        await using var input = await sourceHandle.OpenReadAsync(cancellationToken);
        await using var output = await destination.OpenWriteAsync(cancellationToken);
        await images.Value!.TransformAsync(input, output, sourceHandle.ContentType,
            new Dictionary<string, string> { ["format"] = "webp", ["quality"] = "80" }, cancellationToken);
        return new(state.With("lastOutput", destination.DisplayName));
    }
}
