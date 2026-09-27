namespace ClipDesk.Plugin.Stopwatch;

public static class StopwatchStateKeys
{
    public const string Status = "status";
    public const string AccumulatedMs = "accumulatedMs";
    public const string StartUtc = "startUtc";
    public const string LastLapTotalMs = "lastLapTotalMs";
    public const string Laps = "laps";
}

public static class StopwatchStatus
{
    public const string Idle = "idle";
    public const string Running = "running";
    public const string Paused = "paused";
}

public static class StopwatchCommands
{
    public const string Start = "start";
    public const string Pause = "pause";
    public const string Resume = "resume";
    public const string Reset = "reset";
    public const string RecordLap = "record-lap";
    public const string ClearLaps = "clear-laps";
}
