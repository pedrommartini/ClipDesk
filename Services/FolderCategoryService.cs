using System.IO;
using ClipDesk.Models;

namespace ClipDesk.Services;

public sealed record FolderCategory(string Key, string Name, string Glyph, string Accent, IReadOnlyList<ClipboardItem> Items);

public static class FolderCategoryService
{
    private static readonly string[] VideoExtensions = [".mp4", ".mov", ".avi", ".mkv", ".webm", ".wmv", ".m4v"];
    private static readonly string[] AudioExtensions = [".mp3", ".wav", ".m4a", ".aac", ".ogg", ".flac", ".wma"];
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg"];

    private static readonly (string Key, string Name, string Glyph, string Accent)[] Definitions =
    [
        ("images", "Imagens", "\uE91B", "#06B6D4"),
        ("videos", "Vídeos", "\uE714", "#F97316"),
        ("audio", "Áudios", "\uE8D6", "#EC4899"),
        ("links", "Links", "\uE71B", "#8B5CF6"),
        ("texts", "Textos", "\uE8A5", "#3B82F6"),
        ("files", "Outros", "\uE7C3", "#64748B")
    ];

    public static IReadOnlyList<FolderCategory> GetCategories(IEnumerable<ClipboardItem> items)
    {
        var grouped = items.GroupBy(GetCategoryKey).ToDictionary(group => group.Key, group => (IReadOnlyList<ClipboardItem>)group.ToList());
        return Definitions
            .Where(definition => grouped.TryGetValue(definition.Key, out var children) && children.Count > 0)
            .Select(definition => new FolderCategory(definition.Key, definition.Name, definition.Glyph, definition.Accent, grouped[definition.Key]))
            .ToList();
    }

    public static FolderCategory? GetCategory(IEnumerable<ClipboardItem> items, string key) =>
        GetCategories(items).FirstOrDefault(category => category.Key == key);

    private static string GetCategoryKey(ClipboardItem item)
    {
        if (item.Type == ClipboardItemType.Image) return "images";
        if (item.Type == ClipboardItemType.Link) return "links";
        if (item.Type == ClipboardItemType.Text) return "texts";

        var extension = Path.GetExtension(item.FilePaths.FirstOrDefault() ?? string.Empty).ToLowerInvariant();
        if (ImageExtensions.Contains(extension)) return "images";
        if (VideoExtensions.Contains(extension)) return "videos";
        if (AudioExtensions.Contains(extension)) return "audio";
        return "files";
    }
}
