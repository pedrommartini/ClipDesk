using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.AudioNotes;

/// <summary>Portable example: the host owns microphone and temporary files.</summary>
public sealed class AudioNotesModule : IClipDeskPluginModuleV3
{
    public string Id => "clipdesk.audio-notes";
    public int StateVersion => 1;
    public PluginState CreateDefaultState() => new PluginState().With(PluginStateKeys.SchemaVersion, "1");
    public PluginState NormalizeState(PluginState state) => state.With(PluginStateKeys.SchemaVersion, "1");

    public async ValueTask<PluginCommandResult> ExecuteAsync(PluginState state, PluginCommand command,
        IPluginCapabilityProvider capabilities, CancellationToken cancellationToken = default)
    {
        if (command.Name != "record") return PluginCommandResult.Invalid(state, "Comando desconhecido.");
        var microphone = capabilities.Resolve<IPluginAudioInput>(PluginHostCapabilityIds.AudioInput, new(1, 0, 0));
        var files = capabilities.Resolve<IPluginFileWrite>(PluginHostCapabilityIds.FileWrite, new(1, 0, 0));
        if (!microphone.Available || !files.Available)
            return new(state, PluginCommandStatus.Unavailable,
                $"Gravação indisponível: áudio={microphone.Status}, arquivos={files.Status}.");

        var format = new PluginAudioFormat(48000, 1, "pcm-s16le");
        const int maxBytes = 8 * 1024 * 1024;
        var total = 0;
        var selected = await files.Value!.PickSaveAsync("audio-note.pcm", cancellationToken);
        if (selected is null) return new(state, PluginCommandStatus.Unavailable, "O usuário cancelou a gravação.");
        await using var destination = selected;
        await using (var target = await destination.OpenWriteAsync(cancellationToken))
        {
            await foreach (var chunk in microphone.Value!.CaptureAsync(format, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (total + chunk.Data.Length > maxBytes) break;
                await target.WriteAsync(chunk.Data, cancellationToken);
                total += chunk.Data.Length;
            }
        }
        return new(state.With("lastRecording", destination.DisplayName),
            Data: new Dictionary<string, string> { ["bytes"] = total.ToString(System.Globalization.CultureInfo.InvariantCulture) });
    }
}
