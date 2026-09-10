using System.IO;
using ClipDesk.Models;

namespace ClipDesk.Services;

public sealed record FileVisual(string Glyph, string Label, string Accent, bool UseSymbolFont);

public sealed class FileIconService
{
    public FileVisual GetVisual(ClipboardItem item)
    {
        return item.Type switch
        {
            ClipboardItemType.Text => new FileVisual("T", "TXT", "#5D6B82", false),
            ClipboardItemType.Image => new FileVisual("\uE91B", "IMG", "#7C55D9", true),
            ClipboardItemType.Link => new FileVisual("\uE71B", "URL", "#2C8C7C", true),
            ClipboardItemType.AppFolder => new FileVisual("\uE8B7", $"{item.Children.Count}", "#2477F2", true),
            ClipboardItemType.Folder => new FileVisual("\uE8B7", "PASTA", "#2477F2", true),
            ClipboardItemType.File => GetFileVisual(item.FilePaths.FirstOrDefault()),
            _ => new FileVisual("\uE7C3", "ARQ", "#667085", true)
        };
    }

    private static FileVisual GetFileVisual(string? path)
    {
        var extension = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => new FileVisual("PDF", "PDF", "#E23A3A", false),
            ".txt" or ".md" or ".rtf" => new FileVisual("T", "TXT", "#5D6B82", false),
            ".doc" or ".docx" => new FileVisual("W", "DOC", "#2563D8", false),
            ".xls" or ".xlsx" or ".csv" => new FileVisual("X", "PLAN", "#239B57", false),
            ".ppt" or ".pptx" => new FileVisual("P", "PPT", "#F59E0B", false),
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" => new FileVisual("\uE91B", "IMG", "#7C55D9", true),
            ".zip" or ".rar" or ".7z" => new FileVisual("\uE7B8", "ZIP", "#8A6B2E", true),
            _ => new FileVisual("\uE7C3", "ARQ", "#667085", true)
        };
    }
}
