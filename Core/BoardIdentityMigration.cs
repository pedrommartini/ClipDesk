using ClipDesk.Models;

namespace ClipDesk.Core;

/// <summary>Keep copied local boards distinct without discarding either board.</summary>
public static class BoardIdentityMigration
{
    public static bool EnsureUniqueBoardIds(IList<WorkspaceBoard> boards)
    {
        var seen = new HashSet<Guid>();
        var changed = false;
        foreach (var board in boards)
        {
            if (seen.Add(board.Id)) continue;

            // A duplicate cannot safely represent the same remote workspace.
            // Keep its content as a separate local board until the owner chooses
            // to share it explicitly.
            do { board.Id = Guid.NewGuid(); } while (!seen.Add(board.Id));
            board.SyncMode = WorkspaceSyncMode.Local;
            board.OwnerId = null;
            var remappedNodes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in Flatten(board.Items))
            {
                var previousId = item.Id;
                item.Id = Guid.NewGuid().ToString("N");
                remappedNodes["card:" + previousId] = "card:" + item.Id;
                remappedNodes[previousId] = item.Id;
            }
            foreach (var obj in board.Objects)
            {
                var previousId = obj.Id;
                obj.Id = Guid.NewGuid().ToString("N");
                obj.WorkspaceId = board.Id.ToString("N");
                remappedNodes["object:" + previousId] = "object:" + obj.Id;
            }
            foreach (var connector in board.Objects.Where(obj => obj.Kind == BoardObjectKind.Connector))
                foreach (var key in new[] { "nodeIds", "cardIds" })
                    if (connector.Content.TryGetValue(key, out var ids))
                        connector.Content[key] = string.Join(';', ids.Split(';').Select(id => remappedNodes.GetValueOrDefault(id, id)));
            changed = true;
        }
        return changed;
    }

    private static IEnumerable<ClipboardItem> Flatten(IEnumerable<ClipboardItem> items) =>
        items.SelectMany(item => new[] { item }.Concat(Flatten(item.Children)));
}
