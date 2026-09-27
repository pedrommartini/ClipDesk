using System.Globalization;
using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.AudioRecorder;

public sealed class AudioRecorderModule : IClipDeskPluginModule
{
    public string Id => "clipdesk.audiorecorder";
    public int StateVersion => 2;

    public PluginState CreateDefaultState() => new(new Dictionary<string, string>
    {
        [PluginStateKeys.SchemaVersion] = "2",
        ["micChannelMode"] = "mono",
        ["sysChannelMode"] = "stereo",
        ["micEnabled"] = "true",
        ["micVolume"] = "100",
        ["systemEnabled"] = "true",
        ["systemVolume"] = "100",
        ["status"] = "idle",
        ["duration"] = "0",
        ["audioAssetId"] = string.Empty,
        ["audioFileName"] = string.Empty,
        ["captureSession"] = string.Empty,
        ["captureUpdatedUtc"] = string.Empty
    });

    public PluginState NormalizeState(PluginState state)
    {
        var values = state.ToDictionary();
        values[PluginStateKeys.SchemaVersion] = "2";

        string micMode = (state.GetString("micChannelMode") ?? "mono").ToLowerInvariant();
        values["micChannelMode"] = micMode == "stereo" ? "stereo" : "mono";

        string sysMode = (state.GetString("sysChannelMode") ?? "stereo").ToLowerInvariant();
        values["sysChannelMode"] = sysMode == "mono" ? "mono" : "stereo";

        values["micEnabled"] = string.Equals(state.GetString("micEnabled"), "true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
        values["systemEnabled"] = string.Equals(state.GetString("systemEnabled"), "true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";

        int micVol = Math.Clamp(state.GetInt32("micVolume", 100), 0, 100);
        int sysVol = Math.Clamp(state.GetInt32("systemVolume", 100), 0, 100);
        values["micVolume"] = micVol.ToString(CultureInfo.InvariantCulture);
        values["systemVolume"] = sysVol.ToString(CultureInfo.InvariantCulture);

        string status = (state.GetString("status") ?? "idle").ToLowerInvariant();
        if (status is not ("idle" or "recording" or "paused" or "preview"))
            status = "idle";
        values["status"] = status;

        int duration = Math.Max(0, state.GetInt32("duration", 0));
        values["duration"] = duration.ToString(CultureInfo.InvariantCulture);

        values.Remove("lastFile");
        values["audioAssetId"] = Guid.TryParse(state.GetString("audioAssetId"), out var audioId) ? audioId.ToString("N") : "";
        values["audioFileName"] = Path.GetFileName(state.GetString("audioFileName") ?? "");
        values["captureSession"] = Guid.TryParse(state.GetString("captureSession"), out var session) ? session.ToString("N") : "";
        values["captureUpdatedUtc"] = DateTimeOffset.TryParse(state.GetString("captureUpdatedUtc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updated) ? updated.ToString("O") : "";

        return new PluginState(values);
    }

    public ValueTask<PluginCommandResult> ExecuteAsync(
        PluginState state,
        PluginCommand command,
        IPluginExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state = NormalizeState(state);

        return command.Name switch
        {
            "configure-sources" => ValueTask.FromResult(ConfigureSources(state, command)),
            "set-state" => ValueTask.FromResult(SetState(state, command)),
            "reset" => ValueTask.FromResult(new PluginCommandResult(state
                .With("status", "idle")
                .With("duration", "0"))),
            _ => ValueTask.FromResult(PluginCommandResult.Invalid(state, "Comando desconhecido."))
        };
    }

    public string? GetClipboardText(PluginState state)
    {
        var norm = NormalizeState(state);
        string file = norm.GetString("audioFileName") ?? string.Empty;
        return string.IsNullOrWhiteSpace(file) ? null : file;
    }

    private static PluginCommandResult ConfigureSources(PluginState state, PluginCommand command)
    {
        var next = state.ToDictionary();
        foreach (var key in new[] { "micChannelMode", "sysChannelMode" })
            if (command.Argument(key) is { } mode) next[key] = mode == "stereo" ? "stereo" : "mono";

        if (command.Argument("micEnabled") is { } micEn)
            next["micEnabled"] = string.Equals(micEn, "true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";

        if (command.Argument("micVolume") is { } micV && int.TryParse(micV, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mv))
            next["micVolume"] = Math.Clamp(mv, 0, 100).ToString(CultureInfo.InvariantCulture);

        if (command.Argument("systemEnabled") is { } sysEn)
            next["systemEnabled"] = string.Equals(sysEn, "true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";

        if (command.Argument("systemVolume") is { } sysV && int.TryParse(sysV, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sv))
            next["systemVolume"] = Math.Clamp(sv, 0, 100).ToString(CultureInfo.InvariantCulture);

        return new PluginCommandResult(new PluginState(next));
    }

    private static PluginCommandResult SetState(PluginState state, PluginCommand command)
    {
        var next = ConfigureSources(state, command).State.ToDictionary();

        if (command.Argument("status") is { } st)
        {
            string s = st.ToLowerInvariant();
            if (s is "idle" or "recording" or "paused" or "preview")
                next["status"] = s;
        }

        if (command.Argument("duration") is { } dur && int.TryParse(dur, NumberStyles.Integer, CultureInfo.InvariantCulture, out int d))
            next["duration"] = Math.Max(0, d).ToString(CultureInfo.InvariantCulture);

        if (command.Argument("audioAssetId") is { } id)
            next["audioAssetId"] = Guid.TryParse(id, out var parsed) ? parsed.ToString("N") : "";
        if (command.Argument("audioFileName") is { } name) next["audioFileName"] = Path.GetFileName(name);
        if (command.Argument("captureSession") is { } session)
            next["captureSession"] = Guid.TryParse(session, out var parsedSession) ? parsedSession.ToString("N") : "";
        if (command.Argument("captureUpdatedUtc") is { } updated && DateTimeOffset.TryParse(updated, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
            next["captureUpdatedUtc"] = timestamp.ToString("O");

        return new PluginCommandResult(new PluginState(next));
    }
}
