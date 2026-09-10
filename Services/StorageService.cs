using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;
using System.Security.Cryptography;
using ClipDesk.Models;

namespace ClipDesk.Services;

public sealed class StorageService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public string BaseDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipDesk");

    public string AssetsDirectory => Path.Combine(BaseDirectory, "Assets");
    public string DataPath => Path.Combine(BaseDirectory, "items.json");
    public string WorkspacesPath => Path.Combine(BaseDirectory, "workspaces.json");
    public string SettingsPath => Path.Combine(BaseDirectory, "settings.json");

    public AppSettings LoadSettings()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new()
                : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void SaveSettings(AppSettings settings) => WriteAtomically(SettingsPath, JsonSerializer.Serialize(settings, _jsonOptions));

    private static void WriteAtomically(string path, string json)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }

    public StorageService()
    {
        // All user data stays local and offline under the current Windows profile.
        Directory.CreateDirectory(BaseDirectory);
        Directory.CreateDirectory(AssetsDirectory);
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public List<ClipboardItem> LoadItems()
    {
        if (!File.Exists(DataPath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(DataPath);
            return JsonSerializer.Deserialize<List<ClipboardItem>>(json, _jsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void SaveItems(IEnumerable<ClipboardItem> items)
    {
        var json = JsonSerializer.Serialize(items, _jsonOptions);
        WriteAtomically(DataPath, json);
    }

    public List<WorkspaceBoard> LoadWorkspaces()
    {
        try
        {
            if (File.Exists(WorkspacesPath))
            {
                var boards = JsonSerializer.Deserialize<List<WorkspaceBoard>>(File.ReadAllText(WorkspacesPath), _jsonOptions);
                if (boards is { Count: > 0 }) return boards;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A mesa principal abaixo mantém o app utilizável caso um arquivo seja interrompido externamente.
        }

        return [new WorkspaceBoard { Name = "Mesa principal", Items = LoadItems() }];
    }

    public void SaveWorkspaces(IEnumerable<WorkspaceBoard> boards) =>
        WriteAtomically(WorkspacesPath, JsonSerializer.Serialize(boards, _jsonOptions));

    public string SaveBitmap(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        var bytes = stream.ToArray();
        var path = Path.Combine(AssetsDirectory, $"{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}.png");
        if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
        return path;
    }

    public BitmapImage? LoadBitmap(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
