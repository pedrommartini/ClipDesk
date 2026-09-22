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
            ClipboardItemType.Text => new FileVisual("\uE8A5", "TEXTO", "#3B82F6", true),
            ClipboardItemType.Image => new FileVisual("\uE91B", "IMAGEM", "#06B6D4", true),
            ClipboardItemType.Link => new FileVisual("\uE71B", "LINK", "#8B5CF6", true),
            ClipboardItemType.AppFolder => new FileVisual("\uE8B7", $"{item.Children.Count} ITENS", "#8B5CF6", true),
            ClipboardItemType.Folder => new FileVisual("\uE8B7", "PASTA", "#A855F7", true),
            ClipboardItemType.File => GetFileVisual(item.FilePaths.FirstOrDefault() ?? item.Attachments.FirstOrDefault()?.Name ?? item.DisplayName),
            _ => new FileVisual("\uE7C3", "ARQ", "#667085", true)
        };
    }

    private static FileVisual GetFileVisual(string? path)
    {
        var extension = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => new FileVisual("PDF", "PDF", "#F43F5E", false),
            ".txt" or ".md" or ".rtf" => new FileVisual("T", "TXT", "#3B82F6", false),
            ".doc" or ".docx" => new FileVisual("W", "DOC", "#2563D8", false),
            ".xls" or ".xlsx" or ".csv" => new FileVisual("X", "PLAN", "#239B57", false),
            ".ppt" or ".pptx" => new FileVisual("P", "PPT", "#F59E0B", false),
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg" => new FileVisual("\uE91B", "IMG", "#06B6D4", true),
            ".mp4" or ".mov" or ".avi" or ".mkv" or ".webm" or ".wmv" or ".m4v" => new FileVisual("\uE714", "VÍDEO", "#F97316", true),
            ".mp3" or ".wav" or ".m4a" or ".aac" or ".ogg" or ".flac" or ".wma" => new FileVisual("\uE8D6", "ÁUDIO", "#EC4899", true),
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => new FileVisual("\uE7B8", "ZIP", "#D97706", true),
            ".exe" or ".com" or ".bat" or ".cmd" => new FileVisual("\uE756", "APP", "#6366F1", true),
            ".msi" or ".msix" or ".appx" => new FileVisual("\uE896", "INST", "#8B5CF6", true),
            ".html" or ".htm" or ".css" or ".js" or ".ts" or ".jsx" or ".tsx" or ".json" or ".xml" or ".cs" or ".py" or ".java" or ".cpp" or ".h" or ".sql" or ".ps1" => new FileVisual("\uE943", "CÓD", "#0EA5E9", true),
            ".ttf" or ".otf" or ".woff" or ".woff2" => new FileVisual("Aa", "FONTE", "#14B8A6", false),
            ".iso" or ".img" => new FileVisual("\uE958", "DISCO", "#64748B", true),
            ".lnk" or ".url" => new FileVisual("\uE71B", "ATALHO", "#8B5CF6", true),
            _ => new FileVisual("\uE7C3", "ARQ", "#667085", true)
        };
    }
}
