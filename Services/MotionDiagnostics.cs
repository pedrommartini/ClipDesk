using System.IO;
using System.Diagnostics;
using System.Text.Json;

namespace ClipDesk.Services;

// Local counters and same-host timing for the two review clients. No positions,
// content, account identifiers or tokens are written; nothing is uploaded.
internal static class MotionDiagnostics
{
    internal enum Stage { Input, Queued, Sent, Received, Rejected, Applied, RenderFrames, SendFailed }
    private static readonly long[] Counts = new long[Enum.GetValues<Stage>().Length];
    private static readonly long[] Previous = new long[Counts.Length];
    private static readonly DateTimeOffset Started = DateTimeOffset.UtcNow;
    private static readonly object LatencyGate = new();
    private static readonly Queue<double> PresenceLatencies = new();
    private static long _presenceLatencyCount;
    private static int _writing;
    private static readonly Lazy<System.Threading.Timer> Writer = new(() =>
        new System.Threading.Timer(_ => Write(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)));

    internal static void Record(Stage stage)
    {
        if (!AppEnvironment.IsTestClient) return;
        Interlocked.Increment(ref Counts[(int)stage]);
        _ = Writer.Value;
    }

    // Stopwatch ticks are comparable across processes on the same machine. The
    // two isolated review clients use this to measure input-to-UI latency.
    internal static void RecordPresenceLatency(long originTicks)
    {
        if (!AppEnvironment.IsTestClient || originTicks <= 0) return;
        var now = Stopwatch.GetTimestamp();
        if (originTicks > now) return;
        var elapsed = now - originTicks;
        if (elapsed > Stopwatch.Frequency * 30L) return;
        lock (LatencyGate)
        {
            if (PresenceLatencies.Count == 4096) PresenceLatencies.Dequeue();
            PresenceLatencies.Enqueue(elapsed * 1000d / Stopwatch.Frequency);
            _presenceLatencyCount++;
        }
    }

    private static void Write()
    {
        if (Interlocked.Exchange(ref _writing, 1) != 0) return;
        try
        {
            var totals = new Dictionary<string,long>();
            var interval = new Dictionary<string,long>();
            foreach (var stage in Enum.GetValues<Stage>())
            {
                var count = Interlocked.Read(ref Counts[(int)stage]);
                totals[stage.ToString()] = count;
                interval[stage.ToString()] = count - Previous[(int)stage];
                Previous[(int)stage] = count;
            }
            double[] samples;
            long sampleCount;
            lock (LatencyGate)
            {
                samples = PresenceLatencies.Order().ToArray();
                sampleCount = _presenceLatencyCount;
            }
            static double? Percentile(double[] values, double percentile) => values.Length == 0 ? null
                : Math.Round(values[Math.Max(0, (int)Math.Ceiling(values.Length * percentile) - 1)], 1);
            var latency = new { sampleCount, windowSize = samples.Length,
                p50Ms = Percentile(samples, .50), p95Ms = Percentile(samples, .95),
                maxMs = Percentile(samples, 1) };
            var json = JsonSerializer.Serialize(new { startedUtc = Started, sampledUtc = DateTimeOffset.UtcNow,
                processId = Environment.ProcessId, intervalSeconds = 5, totals, lastInterval = interval,
                sameHostPresenceLatency = latency });
            Directory.CreateDirectory(AppEnvironment.DataRoot);
            var path = Path.Combine(AppEnvironment.DataRoot, "motion-diagnostics.json");
            File.WriteAllText(path + ".tmp", json);
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        finally { Volatile.Write(ref _writing, 0); }
    }
}
