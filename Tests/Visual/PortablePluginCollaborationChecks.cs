using ClipDesk.Plugin.Clock;
using ClipDesk.Plugin.QrCode;
using ClipDesk.Plugin.Stopwatch;
using ClipDesk.Plugin.Timer;
using ClipDesk.Plugin.Timezone;
using ClipDesk.PluginSdk;

internal static class PortablePluginCollaborationChecks
{
    public static void Run()
    {
        var now = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
        var execution = new PluginExecutionContext(new OfflineNetwork(), clock: () => now);
        var clock = new ClockModule();
        var clockState = Command(clock, clock.CreateDefaultState(), "set-display-mode", execution, ("mode", "digital"));
        clockState = Command(clock, clockState, "add-world-clock", execution,
            ("id", "mex"), ("city", "Cidade do México"), ("zone", "America/Mexico_City"));
        VerifyRoundtrip(clock, clockState, "worldClocks", "mex");
        if (clock.NormalizeState(clockState).GetString("displayMode") != "digital") throw new Exception("Clock mode was lost.");

        var stopwatch = new StopwatchModule();
        var stopwatchState = Command(stopwatch, stopwatch.CreateDefaultState(), StopwatchCommands.Start, execution);
        if (stopwatchState.GetString(StopwatchStateKeys.StartUtc) != now.ToString("O"))
            throw new Exception("Stopwatch started with a local-only timestamp.");
        VerifyRoundtrip(stopwatch, stopwatchState, StopwatchStateKeys.StartUtc, now.ToString("O"));
        now = now.AddSeconds(5);
        var lap = Command(stopwatch, stopwatchState, StopwatchCommands.RecordLap, execution);
        VerifyRoundtrip(stopwatch, lap, StopwatchStateKeys.Laps, "5000");

        var timer = new TimerModule();
        var timerState = Command(timer, timer.CreateDefaultState(), "set-duration", execution, ("seconds", "120"));
        timerState = Command(timer, timerState, "start", execution);
        VerifyRoundtrip(timer, timerState, "targetUtc", now.AddSeconds(120).ToString("O"));
        if (TimerModule.CalculateRemainingSeconds(timerState, now.AddSeconds(30)) != 90)
            throw new Exception("Late timer client did not derive remaining time from a shared target.");

        var timezone = new TimezoneModule();
        var timezoneState = Command(timezone, timezone.CreateDefaultState(), "set-reference-time", execution,
            ("minuteOfDay", "525"));
        VerifyRoundtrip(timezone, timezoneState, TimezoneModule.KeySelectedMinuteOfDay, "525");
        timezoneState = Command(timezone, timezoneState, "swap-reference", execution, ("id", "lon"));
        VerifyRoundtrip(timezone, timezoneState, TimezoneModule.KeyReferenceZone, "Europe/London");

        var qr = new QrCodeModule();
        var qrState = Command(qr, qr.CreateDefaultState(), "set-text", execution, ("value", "Mesa compartilhada"));
        qrState = Command(qr, qrState, "set-ecc", execution, ("value", "H"));
        VerifyRoundtrip(qr, qrState, "text", "Mesa compartilhada");
        if (qr.NormalizeState(qrState).GetString("ecc") != "H") throw new Exception("QR correction setting was lost.");
        Console.WriteLine("PASS: clock, stopwatch, timer, timezone and QR state reconstruct from portable commands/snapshots.");
    }
    private static PluginState Command(IClipDeskPluginModule module, PluginState state, string name,
        IPluginExecutionContext execution, params (string Key, string Value)[] args)
    {
        var result = module.ExecuteAsync(state, new PluginCommand(name, args.ToDictionary(x => x.Key, x => x.Value)), execution).Result;
        if (!result.Succeeded) throw new Exception($"{module.Id}/{name} failed: {result.Message}");
        return result.State;
    }
    private static void VerifyRoundtrip(IClipDeskPluginModule module, PluginState state, string key, string expected)
    {
        var receiver = module.NormalizeState(new PluginState(state.ToDictionary()));
        if (!receiver.GetString(key)!.Contains(expected, StringComparison.Ordinal))
            throw new Exception($"{module.Id} lost {key} on a second client.");
    }
    private sealed class OfflineNetwork : IPluginNetworkClient
    {
        public Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken = default) =>
            throw new Exception("A portable state reconstruction tried to access the network.");
    }
}
