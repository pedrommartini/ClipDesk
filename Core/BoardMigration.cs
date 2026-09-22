using ClipDesk.Models;

namespace ClipDesk.Core;

/// <summary>Normalizes older workspace documents before they are displayed in the bounded board.</summary>
public static class BoardMigration
{
    public static bool Normalize(WorkspaceBoard board)
    {
        var changed = false;
        if (!double.IsFinite(board.WorldWidth) || board.WorldWidth < 1) { board.WorldWidth = BoardSpace.DefaultWidth; changed = true; }
        if (!double.IsFinite(board.WorldHeight) || board.WorldHeight < 1) { board.WorldHeight = BoardSpace.DefaultHeight; changed = true; }
        if (board.SchemaVersion < BoardSpace.SchemaVersion) { board.SchemaVersion = BoardSpace.SchemaVersion; changed = true; }

        foreach (var item in Flatten(board.Items))
        {
            var width = item.Width > 0 ? item.Width : 340;
            var height = item.Height > 0 ? item.Height : 220;
            var x = Math.Clamp(double.IsFinite(item.X) ? item.X : 0, 0, Math.Max(0, board.WorldWidth - width));
            var y = Math.Clamp(double.IsFinite(item.Y) ? item.Y : 0, 0, Math.Max(0, board.WorldHeight - height));
            if (item.X != x || item.Y != y) { item.X = x; item.Y = y; changed = true; }
        }
        foreach (var obj in board.Objects)
            if (BoardPluginIdentity.Normalize(obj)) changed = true;
        return changed;
    }

    private static IEnumerable<ClipboardItem> Flatten(IEnumerable<ClipboardItem> items) =>
        items.SelectMany(item => new[] { item }.Concat(Flatten(item.Children)));
}
