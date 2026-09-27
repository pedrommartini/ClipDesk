using System.Globalization;
using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.Timer;

public sealed class TimerModule : IClipDeskPluginModule
{
    public const string PluginId = "clipdesk.timer";
    public const int CurrentStateVersion = 1;
    public const int DefaultDuration = 1500; // 25m Pomodoro
    public const int MinimumDuration = 1; // 1s
    public const int MaximumDuration = 86400; // 24h

    public static readonly IReadOnlyDictionary<string, (int Seconds, string Label)> Presets =
        new Dictionary<string, (int Seconds, string Label)>(StringComparer.OrdinalIgnoreCase)
        {
            ["5m"] = (300, "Pausa 5m"),
            ["10m"] = (600, "Pausa 10m"),
            ["15m"] = (900, "Pausa 15m"),
            ["25m"] = (1500, "Pomodoro"),
            ["45m"] = (2700, "Foco 45m"),
            ["60m"] = (3600, "Hora de Foco")
        };

    public string Id => PluginId;
    public int StateVersion => CurrentStateVersion;

    public PluginState CreateDefaultState() => new(new Dictionary<string, string>
    {
        [PluginStateKeys.SchemaVersion] = "1",
        ["status"] = "idle",
        ["durationSeconds"] = DefaultDuration.ToString(CultureInfo.InvariantCulture),
        ["remainingSeconds"] = DefaultDuration.ToString(CultureInfo.InvariantCulture),
        ["targetUtc"] = "",
        ["selectedPreset"] = "25m",
        ["label"] = "Pomodoro"
    });

    public PluginState NormalizeState(PluginState state)
    {
        var values = state.ToDictionary();
        values[PluginStateKeys.SchemaVersion] = "1";

        var duration = state.GetInt32("durationSeconds", DefaultDuration);
        duration = Math.Clamp(duration, MinimumDuration, MaximumDuration);
        values["durationSeconds"] = duration.ToString(CultureInfo.InvariantCulture);

        var status = state.GetString("status") ?? "idle";
        if (status is not ("idle" or "running" or "paused" or "completed"))
        {
            status = "idle";
        }
        values["status"] = status;

        var remaining = state.GetInt32("remainingSeconds", duration);
        if (status == "idle")
        {
            remaining = duration;
        }
        else if (status == "completed")
        {
            remaining = 0;
        }
        else
        {
            remaining = Math.Clamp(remaining, 0, duration);
        }
        values["remainingSeconds"] = remaining.ToString(CultureInfo.InvariantCulture);

        var targetUtc = state.GetString("targetUtc") ?? "";
        if (status == "running")
        {
            if (DateTimeOffset.TryParse(targetUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
            {
                values["targetUtc"] = targetUtc;
            }
            else
            {
                values["targetUtc"] = "";
                values["status"] = "paused";
            }
        }
        else
        {
            values["targetUtc"] = "";
        }

        var selectedPreset = state.GetString("selectedPreset") ?? "";
        if (!string.IsNullOrEmpty(selectedPreset) && !Presets.ContainsKey(selectedPreset))
        {
            selectedPreset = "";
        }
        values["selectedPreset"] = selectedPreset;

        var label = (state.GetString("label") ?? "").Trim();
        if (string.IsNullOrEmpty(label))
        {
            if (!string.IsNullOrEmpty(selectedPreset) && Presets.TryGetValue(selectedPreset, out var p))
                label = p.Label;
            else
                label = "Temporizador";
        }
        values["label"] = label;

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
            "start" => ValueTask.FromResult(Start(state, context)),
            "pause" => ValueTask.FromResult(Pause(state, context)),
            "resume" => ValueTask.FromResult(Resume(state, context)),
            "reset" => ValueTask.FromResult(Reset(state)),
            "set-duration" => ValueTask.FromResult(SetDuration(state, command.Argument("seconds"))),
            "select-preset" => ValueTask.FromResult(SelectPreset(state, command.Argument("preset"))),
            "check-completion" or "tick" => ValueTask.FromResult(CheckCompletion(state, context)),
            _ => ValueTask.FromResult(PluginCommandResult.Invalid(state, "Comando desconhecido."))
        };
    }

    public string? GetClipboardText(PluginState state)
    {
        var normalized = NormalizeState(state);
        var status = normalized.GetString("status") ?? "idle";
        var duration = normalized.GetInt32("durationSeconds", DefaultDuration);
        var remaining = normalized.GetInt32("remainingSeconds", duration);
        var label = normalized.GetString("label") ?? "Temporizador";

        var statusDisplay = status switch
        {
            "running" => "Em execução",
            "paused" => "Pausado",
            "completed" => "Concluído",
            _ => "Pronto"
        };

        return $"⏱️ Temporizador ClipDesk: {FormatTime(remaining)} / {FormatTime(duration)} ({statusDisplay} - {label})";
    }

    public static int CalculateRemainingSeconds(PluginState state, DateTimeOffset now)
    {
        var status = state.GetString("status") ?? "idle";
        var duration = state.GetInt32("durationSeconds", DefaultDuration);

        if (status == "completed")
            return 0;

        if (status == "running")
        {
            var targetStr = state.GetString("targetUtc");
            if (DateTimeOffset.TryParse(targetStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var target))
            {
                var remaining = (int)Math.Max(0, Math.Ceiling((target - now).TotalSeconds));
                return Math.Clamp(remaining, 0, duration);
            }
        }

        return Math.Clamp(state.GetInt32("remainingSeconds", duration), 0, duration);
    }

    public static string FormatTime(int totalSeconds)
    {
        if (totalSeconds < 0) totalSeconds = 0;
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;

        return hours > 0
            ? $"{hours:D2}:{minutes:D2}:{seconds:D2}"
            : $"{minutes:D2}:{seconds:D2}";
    }

    private static PluginCommandResult Start(PluginState state, IPluginExecutionContext context)
    {
        var status = state.GetString("status") ?? "idle";
        var duration = state.GetInt32("durationSeconds", DefaultDuration);

        if (duration < MinimumDuration)
            return PluginCommandResult.Invalid(state, "A duração deve ser de pelo menos 1 segundo.");

        if (status == "running")
            return new PluginCommandResult(state);

        var remaining = (status == "paused")
            ? state.GetInt32("remainingSeconds", duration)
            : duration;

        if (remaining <= 0)
            remaining = duration;

        var targetUtc = context.UtcNow.AddSeconds(remaining);

        var next = state
            .With("status", "running")
            .With("targetUtc", targetUtc.ToString("O"))
            .With("remainingSeconds", remaining.ToString(CultureInfo.InvariantCulture));

        return new PluginCommandResult(next);
    }

    private static PluginCommandResult Pause(PluginState state, IPluginExecutionContext context)
    {
        var status = state.GetString("status") ?? "idle";

        if (status is "paused" or "idle")
            return PluginCommandResult.Invalid(state, "O timer não está em execução.");

        if (status == "completed")
            return PluginCommandResult.Invalid(state, "O timer já foi concluído.");

        var remaining = CalculateRemainingSeconds(state, context.UtcNow);

        if (remaining <= 0)
        {
            return new PluginCommandResult(state
                .With("status", "completed")
                .With("remainingSeconds", "0")
                .With("targetUtc", ""));
        }

        var next = state
            .With("status", "paused")
            .With("remainingSeconds", remaining.ToString(CultureInfo.InvariantCulture))
            .With("targetUtc", "");

        return new PluginCommandResult(next);
    }

    private static PluginCommandResult Resume(PluginState state, IPluginExecutionContext context)
    {
        var status = state.GetString("status") ?? "idle";

        if (status != "paused")
            return PluginCommandResult.Invalid(state, "O timer não está pausado.");

        var duration = state.GetInt32("durationSeconds", DefaultDuration);
        var remaining = state.GetInt32("remainingSeconds", duration);

        if (remaining <= 0)
        {
            return new PluginCommandResult(state
                .With("status", "completed")
                .With("remainingSeconds", "0")
                .With("targetUtc", ""));
        }

        var targetUtc = context.UtcNow.AddSeconds(remaining);

        var next = state
            .With("status", "running")
            .With("targetUtc", targetUtc.ToString("O"))
            .With("remainingSeconds", remaining.ToString(CultureInfo.InvariantCulture));

        return new PluginCommandResult(next);
    }

    private static PluginCommandResult Reset(PluginState state)
    {
        var duration = state.GetInt32("durationSeconds", DefaultDuration);

        var next = state
            .With("status", "idle")
            .With("remainingSeconds", duration.ToString(CultureInfo.InvariantCulture))
            .With("targetUtc", "");

        return new PluginCommandResult(next);
    }

    private static PluginCommandResult SetDuration(PluginState state, string? secondsArg)
    {
        if (!int.TryParse(secondsArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) || seconds < MinimumDuration)
            return PluginCommandResult.Invalid(state, "A duração deve ser de pelo menos 1 segundo.");

        if (seconds > MaximumDuration)
            return PluginCommandResult.Invalid(state, "A duração máxima permitida é de 24 horas.");

        var next = state
            .With("durationSeconds", seconds.ToString(CultureInfo.InvariantCulture))
            .With("remainingSeconds", seconds.ToString(CultureInfo.InvariantCulture))
            .With("status", "idle")
            .With("targetUtc", "")
            .With("selectedPreset", "")
            .With("label", "Personalizado");

        return new PluginCommandResult(next);
    }

    private static PluginCommandResult SelectPreset(PluginState state, string? presetKey)
    {
        if (string.IsNullOrWhiteSpace(presetKey) || !Presets.TryGetValue(presetKey, out var preset))
            return PluginCommandResult.Invalid(state, "Preset não reconhecido.");

        var next = state
            .With("durationSeconds", preset.Seconds.ToString(CultureInfo.InvariantCulture))
            .With("remainingSeconds", preset.Seconds.ToString(CultureInfo.InvariantCulture))
            .With("status", "idle")
            .With("targetUtc", "")
            .With("selectedPreset", presetKey)
            .With("label", preset.Label);

        return new PluginCommandResult(next);
    }

    private static PluginCommandResult CheckCompletion(PluginState state, IPluginExecutionContext context)
    {
        var status = state.GetString("status") ?? "idle";
        if (status != "running")
            return new PluginCommandResult(state);

        var remaining = CalculateRemainingSeconds(state, context.UtcNow);

        if (remaining <= 0)
        {
            var completedState = state
                .With("status", "completed")
                .With("remainingSeconds", "0")
                .With("targetUtc", "");
            return new PluginCommandResult(completedState);
        }

        return new PluginCommandResult(state.With("remainingSeconds", remaining.ToString(CultureInfo.InvariantCulture)));
    }
}
