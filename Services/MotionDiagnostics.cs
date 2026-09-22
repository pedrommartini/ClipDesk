using System.IO;
using System.Text.Json;

namespace ClipDesk.Services;

// Local aggregate counters for the two review clients. No positions, content,
// account identifiers or tokens are written; no diagnostics are uploaded.
internal static class MotionDiagnostics
{
    internal enum Stage { Input, Queued, Sent, Received, Rejected, Applied, RenderFrames, SendFailed }
    private static readonly long[] Counts = new long[Enum.GetValues<Stage>().Length];
    private static readonly long[] Previous = new long[Counts.Length];
    private static readonly DateTimeOffset Started = DateTimeOffset.UtcNow;
    private static int _writing;
    private static readonly Lazy<System.Threading.Timer> Writer = new(() =>
        new System.Threading.Timer(_ => Write(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)));

    internal static void Record(Stage stage)
    {
        if (!AppEnvironment.IsTestClient) return;
        Interlocked.Increment(ref Counts[(int)stage]);
        _ = Writer.Value;
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
            var json = JsonSerializer.Serialize(new { startedUtc = Started, sampledUtc = DateTimeOffset.UtcNow,
                processId = Environment.ProcessId, intervalSeconds = 5, totals, lastInterval = interval });
            Directory.CreateDirectory(AppEnvironment.DataRoot);
            var path = Path.Combine(AppEnvironment.DataRoot, "motion-diagnostics.json");
            File.WriteAllText(path + ".tmp", json);
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        finally { Volatile.Write(ref _writing, 0); }
    }
}
