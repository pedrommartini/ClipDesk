using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using ClipDesk.Models;
using ClipDesk.Core;

namespace ClipDesk.Services;

public sealed class StorageService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false
    };

    private string _profile = "local";
    private readonly string _dataRoot = AppEnvironment.DataRoot;
    public string Profile => _profile;
    public string BaseDirectory => Path.Combine(_dataRoot, "Profiles", _profile);
    public LocalDatabase Database { get; private set; } = null!;

    public string AssetsDirectory => Path.Combine(BaseDirectory, "Assets");
    public string DataPath => Path.Combine(BaseDirectory, "items.json");
    public string WorkspacesPath => Path.Combine(BaseDirectory, "workspaces.json");
    public string SettingsPath => Path.Combine(BaseDirectory, "settings.json");
    public string HistoryPath => Path.Combine(BaseDirectory, "clipboard-history.json");
    public static string DownloadsDirectory => AppEnvironment.IsTestClient
        ? Path.Combine(AppEnvironment.DataRoot, "Downloads")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ClipDesk", "Downloads");
    public static bool IsUserDownload(string? path)
    {
        if(string.IsNullOrWhiteSpace(path)) return false;
        var root=Path.GetFullPath(DownloadsDirectory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root,StringComparison.OrdinalIgnoreCase);
    }

    public AppSettings LoadSettings()
    {
        try
        {
            var json = ReadDocument(SettingsPath);
            return json is not null
                ? JsonSerializer.Deserialize<AppSettings>(json) ?? new()
                : new();
        }
        catch (Exception ex) when (IsRecoverableReadFailure(ex))
        {
            return new();
        }
    }

    public void SaveSettings(AppSettings settings) => WriteDocument(SettingsPath, JsonSerializer.Serialize(settings, _jsonOptions));

    private static void WriteAtomically(string path, string json)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }

    public StorageService()
    {
        var profilePath = Path.Combine(_dataRoot, "active-profile.txt");
        if (File.Exists(profilePath))
        {
            var saved = File.ReadAllText(profilePath).Trim();
            if (TryNormalizeProfile(saved, out var normalized)) _profile = normalized;
        }
        Directory.CreateDirectory(BaseDirectory);
        Directory.CreateDirectory(AssetsDirectory);
        Database = new LocalDatabase(Path.Combine(BaseDirectory, "clipdesk.db"));
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public void SwitchProfile(string profile)
    {
        if (!TryNormalizeProfile(profile, out var normalized)) throw new ArgumentException("Perfil inválido.");
        _profile = normalized;
        Directory.CreateDirectory(AssetsDirectory);
        Database = new LocalDatabase(Path.Combine(BaseDirectory, "clipdesk.db"));
        WriteAtomically(Path.Combine(_dataRoot, "active-profile.txt"), normalized);
    }

    // Supabase returns standard UUIDs with hyphens. Keep profile folders in the
    // compact canonical form while accepting both safe UUID representations.
    private static bool TryNormalizeProfile(string value, out string normalized)
    {
        if (string.Equals(value, "local", StringComparison.Ordinal)) { normalized = "local"; return true; }
        if (Guid.TryParse(value, out var id)) { normalized = id.ToString("N"); return true; }
        normalized = "local";
        return false;
    }

    private string? ReadDocument(string path)
    {
        var key = Path.GetFileName(path);
        var json = Database.Read(key);
        if (json is not null) return json;
        // Import in place without modifying the original JSON. DEV never reads production data.
        var legacy = File.Exists(path) ? path : Path.Combine(_dataRoot, key);
        if (_profile != "local" || !File.Exists(legacy)) return null;
        json = File.ReadAllText(legacy);
        using var valid = JsonDocument.Parse(json);
        Database.Write(key, json);
        return json;
    }

    private void WriteDocument(string path, string json) => Database.Write(Path.GetFileName(path), json);

    public List<ClipboardItem> LoadItems()
    {
        var saved = ReadDocument(DataPath);
        if (saved is null)
        {
            return [];
        }

        try
        {
            var json = saved;
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
        WriteDocument(DataPath, json);
    }

    public List<WorkspaceBoard> LoadWorkspaces()
    {
        try
        {
            var saved = ReadDocument(WorkspacesPath);
            if (saved is not null)
            {
                var boards = JsonSerializer.Deserialize<List<WorkspaceBoard>>(saved, _jsonOptions);
                if (boards is { Count: > 0 })
                {
                    var migrated = false;
                    foreach (var board in boards) migrated |= BoardMigration.Normalize(board);
                    if (BoardIdentityMigration.EnsureUniqueBoardIds(boards)) migrated = true;
                    if (migrated) SaveWorkspaces(boards);
                    return boards;
                }
            }
        }
        catch (Exception ex) when (IsRecoverableReadFailure(ex))
        {
            // A mesa principal abaixo mantém o app utilizável caso um arquivo seja interrompido externamente.
        }

        var fallbackBoard = new WorkspaceBoard { Name = "Mesa principal", Items = LoadItems() };
        BoardMigration.Normalize(fallbackBoard);
        return [fallbackBoard];
    }

    public void SaveWorkspaces(IEnumerable<WorkspaceBoard> boards)
    {
        var list = boards as IList<WorkspaceBoard> ?? boards.ToList();
        BoardIdentityMigration.EnsureUniqueBoardIds(list);
        WriteDocument(WorkspacesPath, JsonSerializer.Serialize(list, _jsonOptions));
    }

    public List<ClipboardHistoryEntry> LoadHistory()
    {
        try
        {
            var saved = ReadDocument(HistoryPath);
            if (saved is null) return [];
            return JsonSerializer.Deserialize<List<ClipboardHistoryEntry>>(saved, _jsonOptions) ?? [];
        }
        catch (Exception ex) when (IsRecoverableReadFailure(ex))
        {
            return [];
        }
    }

    private static bool IsRecoverableReadFailure(Exception exception) =>
        exception is IOException or JsonException or UnauthorizedAccessException or SqliteException;

    public void SaveHistory(IEnumerable<ClipboardHistoryEntry> entries)
    {
        var retained = entries
            .OrderByDescending(entry => entry.CapturedAt)
            .Take(80)
            .ToList();
        WriteDocument(HistoryPath, JsonSerializer.Serialize(retained, _jsonOptions));
    }

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
