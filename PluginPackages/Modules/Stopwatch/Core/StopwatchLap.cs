using System.Text.Json.Serialization;

namespace ClipDesk.Plugin.Stopwatch;

public sealed record StopwatchLap(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("splitMs")] long SplitMs,
    [property: JsonPropertyName("totalMs")] long TotalMs,
    [property: JsonPropertyName("timestampUtc")] string? TimestampUtc = null);
