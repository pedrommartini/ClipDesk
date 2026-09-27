using System.Globalization;
using System.Text;
using System.Text.Json;
using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.Stopwatch;

public sealed class StopwatchModule : IClipDeskPluginModule
{
    public const string ModuleId = "clipdesk.stopwatch";
    public const int MaxLapsCount = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public string Id => ModuleId;
    public int StateVersion => 1;

    public PluginState CreateDefaultState() => new(new Dictionary<string, string>
    {
        [PluginStateKeys.SchemaVersion] = "1",
        [StopwatchStateKeys.Status] = StopwatchStatus.Idle,
        [StopwatchStateKeys.AccumulatedMs] = "0",
        [StopwatchStateKeys.StartUtc] = string.Empty,
        [StopwatchStateKeys.LastLapTotalMs] = "0",
        [StopwatchStateKeys.Laps] = "[]"
    });

    public PluginState NormalizeState(PluginState state)
    {
        var values = state.ToDictionary();
        values[PluginStateKeys.SchemaVersion] = "1";

        var status = values.TryGetValue(StopwatchStateKeys.Status, out var rawStatus) ? rawStatus : StopwatchStatus.Idle;
        if (status != StopwatchStatus.Idle && status != StopwatchStatus.Running && status != StopwatchStatus.Paused)
        {
            status = StopwatchStatus.Idle;
        }

        var accumulatedMs = GetAccumulatedMilliseconds(state);
        values[StopwatchStateKeys.AccumulatedMs] = accumulatedMs.ToString(CultureInfo.InvariantCulture);

        var startUtcStr = values.TryGetValue(StopwatchStateKeys.StartUtc, out var rawStartUtc) ? rawStartUtc : string.Empty;
        if (status == StopwatchStatus.Running)
        {
            if (!DateTimeOffset.TryParse(startUtcStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
            {
                // If invalid start timestamp while running, revert status to idle
                status = StopwatchStatus.Idle;
                startUtcStr = string.Empty;
            }
        }
        else
        {
            startUtcStr = string.Empty;
        }
        values[StopwatchStateKeys.Status] = status;
        values[StopwatchStateKeys.StartUtc] = startUtcStr;

        var lastLapTotalMs = GetLastLapTotalMilliseconds(state);
        values[StopwatchStateKeys.LastLapTotalMs] = lastLapTotalMs.ToString(CultureInfo.InvariantCulture);

        var rawLaps = values.TryGetValue(StopwatchStateKeys.Laps, out var rawLapsStr) ? rawLapsStr : null;
        var laps = ParseLaps(rawLaps);
        if (laps.Count > MaxLapsCount)
        {
            laps = laps.TakeLast(MaxLapsCount).ToList();
        }
        values[StopwatchStateKeys.Laps] = SerializeLaps(laps);

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
            StopwatchCommands.Start => ValueTask.FromResult(ExecuteStart(state, context)),
            StopwatchCommands.Pause => ValueTask.FromResult(ExecutePause(state, context)),
            StopwatchCommands.Resume => ValueTask.FromResult(ExecuteResume(state, context)),
            StopwatchCommands.Reset => ValueTask.FromResult(ExecuteReset(state)),
            StopwatchCommands.RecordLap => ValueTask.FromResult(ExecuteRecordLap(state, context)),
            StopwatchCommands.ClearLaps => ValueTask.FromResult(ExecuteClearLaps(state)),
            _ => ValueTask.FromResult(PluginCommandResult.Invalid(state, "Comando desconhecido."))
        };
    }

    public string? GetClipboardText(PluginState state)
    {
        state = NormalizeState(state);
        var laps = ParseLaps(state.GetString(StopwatchStateKeys.Laps));
        var elapsedMs = GetElapsedMilliseconds(state, DateTimeOffset.UtcNow);
        if (laps.Count > 0)
        {
            elapsedMs = Math.Max(elapsedMs, laps[^1].TotalMs);
        }

        var sb = new StringBuilder();
        sb.AppendLine("⏱️ Cronômetro ClipDesk");
        sb.AppendLine($"Tempo Total: {FormatTime(elapsedMs)}");

        if (laps.Count == 0)
        {
            sb.Append("Nenhuma volta gravada.");
            return sb.ToString();
        }

        sb.AppendLine($"Voltas Registradas ({laps.Count}):");
        var (bestNumber, worstNumber) = GetBestAndWorstLapNumbers(laps);

        for (int i = 0; i < laps.Count; i++)
        {
            var lap = laps[i];
            string badge = string.Empty;
            if (lap.Number == bestNumber)
            {
                badge = " [Melhor Volta]";
            }
            else if (lap.Number == worstNumber)
            {
                badge = " [Pior Volta]";
            }

            sb.Append($"  #{lap.Number}  {FormatTime(lap.SplitMs)}  (Total: {FormatTime(lap.TotalMs)}){badge}");
            if (i < laps.Count - 1)
            {
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static PluginCommandResult ExecuteStart(PluginState state, IPluginExecutionContext context)
    {
        var status = state.GetString(StopwatchStateKeys.Status);
        if (status == StopwatchStatus.Running)
        {
            return new PluginCommandResult(state);
        }

        if (status == StopwatchStatus.Paused)
        {
            return ExecuteResume(state, context);
        }

        var next = state.ToDictionary();
        next[StopwatchStateKeys.Status] = StopwatchStatus.Running;
        next[StopwatchStateKeys.StartUtc] = context.UtcNow.ToString("O");
        next[StopwatchStateKeys.AccumulatedMs] = "0";
        next[StopwatchStateKeys.LastLapTotalMs] = "0";
        next[StopwatchStateKeys.Laps] = "[]";
        return new PluginCommandResult(new PluginState(next));
    }

    private static PluginCommandResult ExecutePause(PluginState state, IPluginExecutionContext context)
    {
        var status = state.GetString(StopwatchStateKeys.Status);
        if (status != StopwatchStatus.Running)
        {
            return PluginCommandResult.Invalid(state, "O cronômetro não está em execução.");
        }

        var accumulated = GetAccumulatedMilliseconds(state);
        var startUtcStr = state.GetString(StopwatchStateKeys.StartUtc);
        long sessionMs = 0;
        if (DateTimeOffset.TryParse(startUtcStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var startUtc))
        {
            sessionMs = Math.Max(0, (long)(context.UtcNow - startUtc).TotalMilliseconds);
        }

        var newAccumulated = accumulated + sessionMs;
        var next = state.ToDictionary();
        next[StopwatchStateKeys.Status] = StopwatchStatus.Paused;
        next[StopwatchStateKeys.AccumulatedMs] = newAccumulated.ToString(CultureInfo.InvariantCulture);
        next[StopwatchStateKeys.StartUtc] = string.Empty;
        return new PluginCommandResult(new PluginState(next));
    }

    private static PluginCommandResult ExecuteResume(PluginState state, IPluginExecutionContext context)
    {
        var status = state.GetString(StopwatchStateKeys.Status);
        if (status == StopwatchStatus.Running)
        {
            return new PluginCommandResult(state);
        }

        if (status == StopwatchStatus.Idle)
        {
            return ExecuteStart(state, context);
        }

        var next = state.ToDictionary();
        next[StopwatchStateKeys.Status] = StopwatchStatus.Running;
        next[StopwatchStateKeys.StartUtc] = context.UtcNow.ToString("O");
        return new PluginCommandResult(new PluginState(next));
    }

    private static PluginCommandResult ExecuteReset(PluginState state)
    {
        var next = state.ToDictionary();
        next[StopwatchStateKeys.Status] = StopwatchStatus.Idle;
        next[StopwatchStateKeys.AccumulatedMs] = "0";
        next[StopwatchStateKeys.StartUtc] = string.Empty;
        next[StopwatchStateKeys.LastLapTotalMs] = "0";
        next[StopwatchStateKeys.Laps] = "[]";
        return new PluginCommandResult(new PluginState(next));
    }

    private static PluginCommandResult ExecuteRecordLap(PluginState state, IPluginExecutionContext context)
    {
        var status = state.GetString(StopwatchStateKeys.Status);
        if (status == StopwatchStatus.Idle)
        {
            return PluginCommandResult.Invalid(state, "Inicie o cronômetro antes de gravar voltas.");
        }

        var laps = ParseLaps(state.GetString(StopwatchStateKeys.Laps));
        if (laps.Count >= MaxLapsCount)
        {
            return PluginCommandResult.Invalid(state, $"Limite máximo de {MaxLapsCount} voltas atingido.");
        }

        var currentTotalMs = GetElapsedMilliseconds(state, context.UtcNow);
        var lastLapTotal = GetLastLapTotalMilliseconds(state);
        var splitMs = Math.Max(0, currentTotalMs - lastLapTotal);

        var lap = new StopwatchLap(
            Number: laps.Count + 1,
            SplitMs: splitMs,
            TotalMs: currentTotalMs,
            TimestampUtc: context.UtcNow.ToString("O"));

        laps.Add(lap);
        var next = state.ToDictionary();
        next[StopwatchStateKeys.LastLapTotalMs] = currentTotalMs.ToString(CultureInfo.InvariantCulture);
        next[StopwatchStateKeys.Laps] = SerializeLaps(laps);
        return new PluginCommandResult(new PluginState(next));
    }

    private static PluginCommandResult ExecuteClearLaps(PluginState state)
    {
        var next = state.ToDictionary();
        next[StopwatchStateKeys.Laps] = "[]";
        next[StopwatchStateKeys.LastLapTotalMs] = "0";
        return new PluginCommandResult(new PluginState(next));
    }

    public static long GetElapsedMilliseconds(PluginState state, DateTimeOffset nowUtc)
    {
        var accumulated = GetAccumulatedMilliseconds(state);
        var status = state.GetString(StopwatchStateKeys.Status);
        if (status != StopwatchStatus.Running)
        {
            return accumulated;
        }

        var startUtcStr = state.GetString(StopwatchStateKeys.StartUtc);
        if (DateTimeOffset.TryParse(startUtcStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var startUtc))
        {
            var sessionMs = (long)(nowUtc - startUtc).TotalMilliseconds;
            return accumulated + Math.Max(0, sessionMs);
        }

        return accumulated;
    }

    public static long GetAccumulatedMilliseconds(PluginState state)
    {
        var raw = state.GetString(StopwatchStateKeys.AccumulatedMs);
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val) && val >= 0
            ? val
            : 0;
    }

    public static long GetLastLapTotalMilliseconds(PluginState state)
    {
        var raw = state.GetString(StopwatchStateKeys.LastLapTotalMs);
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val) && val >= 0
            ? val
            : 0;
    }

    public static string FormatTime(long milliseconds)
    {
        if (milliseconds < 0) milliseconds = 0;
        var totalSeconds = milliseconds / 1000;
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;
        var centiseconds = (milliseconds % 1000) / 10;
        return $"{hours:D2}:{minutes:D2}:{seconds:D2}.{centiseconds:D2}";
    }

    public static (int? BestNumber, int? WorstNumber) GetBestAndWorstLapNumbers(IReadOnlyList<StopwatchLap> laps)
    {
        if (laps.Count < 2)
        {
            return (null, null);
        }

        long minSplit = long.MaxValue;
        long maxSplit = long.MinValue;

        foreach (var lap in laps)
        {
            if (lap.SplitMs < minSplit) minSplit = lap.SplitMs;
            if (lap.SplitMs > maxSplit) maxSplit = lap.SplitMs;
        }

        if (minSplit == maxSplit)
        {
            return (null, null);
        }

        int? bestNumber = null;
        int? worstNumber = null;

        foreach (var lap in laps)
        {
            if (bestNumber is null && lap.SplitMs == minSplit)
            {
                bestNumber = lap.Number;
            }

            if (worstNumber is null && lap.SplitMs == maxSplit)
            {
                worstNumber = lap.Number;
            }
        }

        return (bestNumber, worstNumber);
    }

    public static List<StopwatchLap> ParseLaps(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<StopwatchLap>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static string SerializeLaps(IEnumerable<StopwatchLap> laps)
    {
        return JsonSerializer.Serialize(laps, JsonOptions);
    }
}
