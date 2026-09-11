using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using ClipDesk.Models;

namespace ClipDesk.Services;

public sealed class ClipboardService : IDisposable
{
    private static readonly Dictionary<string, int> ImageNameCounters = new(StringComparer.OrdinalIgnoreCase);
    private readonly ImageRecognitionService _imageRecognition = new();
    public void Dispose() => _imageRecognition.Dispose();

    public List<ClipboardItem> CaptureClipboard(StorageService storageService)
    {
        // File-drop data can also be present when copying folders from Explorer.
        if (Clipboard.ContainsFileDropList())
        {
            var files = Clipboard.GetFileDropList().Cast<string>();
            return CreateFileItems(files);
        }

        if (Clipboard.ContainsImage())
        {
            var image = Clipboard.GetImage();
            if (image is null)
            {
                return [];
            }

            var path = storageService.SaveBitmap(image);
            var imageName = NextImageName(_imageRecognition.Describe(image) ?? "Imagem");
            return
            [
                new ClipboardItem
                {
                    Type = ClipboardItemType.Image,
                    DisplayName = imageName,
                    StoredFilePath = path
                }
            ];
        }

        if (Clipboard.ContainsText())
        {
            var text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text))
            {
                return [];
            }

            if (LooksLikeUrl(text))
            {
                return
                [
                    new ClipboardItem
                    {
                        Type = ClipboardItemType.Link,
                        DisplayName = BuildDisplayName(text, "Link"),
                        Url = text.Trim()
                    }
                ];
            }

            return
            [
                new ClipboardItem
                {
                    Type = ClipboardItemType.Text,
                    DisplayName = BuildDisplayName(text, "Texto"),
                    Text = text
                }
            ];
        }

        return [];
    }

    public List<ClipboardItem> CreateFileItems(IEnumerable<string> paths)
    {
        return paths
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Select(CreateFileItem)
            .ToList();
    }

    private ClipboardItem CreateFileItem(string path)
    {
        var type = Directory.Exists(path) ? ClipboardItemType.Folder : ClipboardItemType.File;
        var displayName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (type == ClipboardItemType.File && IsImageFile(path))
        {
            var guessed = _imageRecognition.Describe(path);
            if (!string.IsNullOrWhiteSpace(guessed))
            {
                displayName = NextImageName(guessed);
            }
        }

        return new ClipboardItem
        {
            Type = type,
            DisplayName = displayName,
            FilePaths = [path]
        };
    }

    public ClipboardItem Duplicate(ClipboardItem item, StorageService storageService)
    {
        var duplicate = new ClipboardItem
        {
            Type = item.Type,
            DisplayName = $"{item.DisplayName} copia",
            Text = item.Text,
            Url = item.Url,
            FilePaths = [.. item.FilePaths],
            StoredFilePath = item.StoredFilePath,
            Children = item.Children.Select(Clone).ToList(),
            X = item.X + 28,
            Y = item.Y + 28,
            Width = item.Width,
            Height = item.Height
        };

        if (item.Type == ClipboardItemType.Image && File.Exists(item.StoredFilePath))
        {
            var newPath = Path.Combine(storageService.AssetsDirectory, $"{Guid.NewGuid():N}.png");
            File.Copy(item.StoredFilePath, newPath);
            duplicate.StoredFilePath = newPath;
        }

        return duplicate;
    }

    public void CopyToClipboard(ClipboardItem item, StorageService storageService)
    {
        // Keep Windows-native clipboard formats so pasted files/images work in other apps.
        switch (item.Type)
        {
            case ClipboardItemType.Text:
                Clipboard.SetText(item.Text ?? string.Empty);
                break;
            case ClipboardItemType.Link:
                Clipboard.SetText(item.Url ?? string.Empty);
                break;
            case ClipboardItemType.Image:
                var image = storageService.LoadBitmap(item.StoredFilePath);
                if (image is not null)
                {
                    Clipboard.SetImage(image);
                }
                else throw new IOException("A imagem não está mais disponível.");
                break;
            case ClipboardItemType.File:
            case ClipboardItemType.Folder:
                var collection = new StringCollection();
                collection.AddRange(item.FilePaths.Where(path => File.Exists(path) || Directory.Exists(path)).ToArray());
                if (collection.Count > 0)
                {
                    Clipboard.SetFileDropList(collection);
                }
                else throw new IOException("Os arquivos não estão mais disponíveis.");
                break;
            case ClipboardItemType.AppFolder:
                CopyFolderToClipboard(item, storageService);
                break;
        }
    }

    private static void CopyFolderToClipboard(ClipboardItem folder, StorageService storageService)
    {
        var children = FlattenFolder(folder).ToList();
        if (children.Count == 0)
        {
            Clipboard.SetText(string.Empty);
            return;
        }

        if (children.Count == 1)
        {
            CopySingleToClipboard(children[0], storageService);
            return;
        }

        var data = new DataObject();
        var textParts = children
            .Select(ToTextClipboardValue)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        var filePaths = children
            .SelectMany(child => child.FilePaths)
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var firstImage = children
            .Where(child => child.Type == ClipboardItemType.Image)
            .Select(child => storageService.LoadBitmap(child.StoredFilePath))
            .FirstOrDefault(image => image is not null);

        if (textParts.Count > 0)
        {
            data.SetText(string.Join(Environment.NewLine, textParts));
        }

        if (filePaths.Length > 0)
        {
            var collection = new StringCollection();
            collection.AddRange(filePaths);
            data.SetFileDropList(collection);
        }

        if (firstImage is not null)
        {
            data.SetImage(firstImage);
        }

        if (textParts.Count == 0 && filePaths.Length == 0 && firstImage is null)
        {
            data.SetText(string.Join(Environment.NewLine, children.Select(child => child.DisplayName)));
        }

        Clipboard.SetDataObject(data, true);
    }

    private static void CopySingleToClipboard(ClipboardItem item, StorageService storageService)
    {
        switch (item.Type)
        {
            case ClipboardItemType.Text:
                Clipboard.SetText(item.Text ?? string.Empty);
                break;
            case ClipboardItemType.Link:
                Clipboard.SetText(item.Url ?? string.Empty);
                break;
            case ClipboardItemType.Image:
                var image = storageService.LoadBitmap(item.StoredFilePath);
                if (image is not null)
                {
                    Clipboard.SetImage(image);
                }
                break;
            case ClipboardItemType.File:
            case ClipboardItemType.Folder:
                var collection = new StringCollection();
                collection.AddRange(item.FilePaths.Where(path => File.Exists(path) || Directory.Exists(path)).ToArray());
                if (collection.Count > 0)
                {
                    Clipboard.SetFileDropList(collection);
                }
                break;
        }
    }

    private static IEnumerable<ClipboardItem> FlattenFolder(ClipboardItem folder)
    {
        foreach (var child in folder.Children)
        {
            if (child.Type == ClipboardItemType.AppFolder)
            {
                foreach (var nested in FlattenFolder(child))
                {
                    yield return nested;
                }

                continue;
            }

            yield return child;
        }
    }

    private static string? ToTextClipboardValue(ClipboardItem item)
    {
        return item.Type switch
        {
            ClipboardItemType.Text => item.Text,
            ClipboardItemType.Link => item.Url,
            ClipboardItemType.File or ClipboardItemType.Folder => item.FilePaths.Count == 0
                ? null
                : string.Join(Environment.NewLine, item.FilePaths),
            ClipboardItemType.Image => item.StoredFilePath,
            _ => null
        };
    }

    private static ClipboardItem Clone(ClipboardItem item)
    {
        return new ClipboardItem
        {
            Type = item.Type,
            DisplayName = item.DisplayName,
            Text = item.Text,
            Url = item.Url,
            FilePaths = [.. item.FilePaths],
            StoredFilePath = item.StoredFilePath,
            Children = item.Children.Select(Clone).ToList(),
            X = item.X,
            Y = item.Y,
            Width = item.Width,
            Height = item.Height,
            CreatedAt = item.CreatedAt
        };
    }

    private static bool LooksLikeUrl(string value)
    {
        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https";
    }

    private static string BuildDisplayName(string value, string fallback)
    {
        var firstLine = value.Trim().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return fallback;
        }

        return firstLine.Length <= 24 ? firstLine : $"{firstLine[..24]}...";
    }

    private static bool IsImageFile(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp";
    }

    private static string NextImageName(string category)
    {
        ImageNameCounters.TryGetValue(category, out var count);
        count++;
        ImageNameCounters[category] = count;
        return $"{category} {count}";
    }
}
