using ClipDesk.Models;

namespace ClipDesk.Core;

public static class HistoryContent
{
    public static ClipboardItem ToItem(ClipboardHistoryEntry entry) => new()
    {
        Type = entry.Type,
        DisplayName = entry.Title,
        Text = entry.Text,
        Url = entry.Url,
        FilePaths = [.. entry.FilePaths],
        StoredFilePath = entry.StoredFilePath
    };

    // Compare the complete payload: previews can be identical for different items.
    public static bool AreEquivalent(ClipboardHistoryEntry a, ClipboardHistoryEntry b) =>
        a.Type == b.Type && a.Text == b.Text && a.Url == b.Url &&
        a.StoredFilePath == b.StoredFilePath && a.FilePaths.SequenceEqual(b.FilePaths);
}
