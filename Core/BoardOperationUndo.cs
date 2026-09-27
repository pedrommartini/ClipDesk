using System.Text.Json;
using ClipDesk.Models;

namespace ClipDesk.Core;

/// <summary>Reverts only entities changed by one local action, while keeping unrelated remote edits.</summary>
public static class BoardOperationUndo
{
    public sealed record Snapshot(List<ClipboardItem> Items, List<BoardObject> Objects);
    public sealed record Result(Snapshot Snapshot, int Applied, int Conflicted);

    public static Snapshot Capture(WorkspaceBoard board) => Clone(new Snapshot(board.Items, board.Objects));
    public static Snapshot Clone(Snapshot snapshot) => JsonSerializer.Deserialize<Snapshot>(
        JsonSerializer.Serialize(snapshot, CloudRules.Json), CloudRules.Json)!;
    public static bool Differs(Snapshot first, Snapshot second) =>
        !string.Equals(CloudRules.Serialize(first), CloudRules.Serialize(second), StringComparison.Ordinal);

    public static Result Apply(Snapshot before, Snapshot after, Snapshot current, bool undo)
    {
        var result = Clone(current);
        int applied = 0, conflicted = 0;
        ApplyList(before.Items, after.Items, result.Items, item => item.Id, undo, ref applied, ref conflicted);
        ApplyList(before.Objects, after.Objects, result.Objects, obj => obj.Id, undo, ref applied, ref conflicted);
        return new Result(result, applied, conflicted);
    }

    private static void ApplyList<T>(IReadOnlyList<T> before, IReadOnlyList<T> after, List<T> current,
        Func<T, string> id, bool undo, ref int applied, ref int conflicted) where T : class
    {
        var old = before.ToDictionary(id, StringComparer.Ordinal);
        var newer = after.ToDictionary(id, StringComparer.Ordinal);
        foreach (var key in old.Keys.Union(newer.Keys, StringComparer.Ordinal))
        {
            old.TryGetValue(key, out var prior);
            newer.TryGetValue(key, out var later);
            if (Same(prior, later)) continue;
            var expected = undo ? later : prior;
            var replacement = undo ? prior : later;
            var index = current.FindIndex(value => id(value) == key);
            var present = index < 0 ? null : current[index];
            if (!Same(present, expected)) { conflicted++; continue; }
            if (index >= 0) current.RemoveAt(index);
            if (replacement is not null)
                current.Insert(Math.Min(index < 0 ? current.Count : index, current.Count), replacement);
            applied++;
        }
    }

    private static bool Same<T>(T? left, T? right) where T : class =>
        left is null || right is null ? left is null && right is null
            : string.Equals(CloudRules.Serialize(left), CloudRules.Serialize(right), StringComparison.Ordinal);
}
