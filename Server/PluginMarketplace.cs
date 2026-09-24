using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ClipDesk.Core;

namespace ClipDesk.Server;

/// <summary>Small password-gated package registry served by the ClipDesk backend.</summary>
public sealed class PluginMarketplace(IConfiguration configuration, IWebHostEnvironment environment)
{
    private const long MaximumZipBytes = 25L * 1024 * 1024;
    private static readonly Regex PluginId = new("^[a-z0-9]+(?:[.-][a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly object _gate = new();
    private readonly string _root = Path.Combine(environment.ContentRootPath, "data", "plugin-store");
    private string PackagesRoot => Path.Combine(_root, "packages");
    private string AuthorsPath => Path.Combine(_root, "authors.json");

    public object Feed(Uri publicBase)
    {
        lock (_gate)
        {
            var authors = ReadAuthors();
            var packages = Directory.Exists(PackagesRoot)
                ? Directory.EnumerateFiles(PackagesRoot, "*.zip", SearchOption.TopDirectoryOnly)
                    .Select(path => FeedEntry(path, publicBase, authors))
                    .Where(entry => entry is not null)
                    .OrderBy(entry => entry!["sortOrder"]?.GetValue<int>() ?? 0)
                    .ThenBy(entry => entry!["name"]?.GetValue<string>())
                    .Cast<JsonObject>()
                    .ToArray()
                : [];
            return new JsonObject { ["schemaVersion"] = 1, ["packages"] = new JsonArray(packages) };
        }
    }

    public PluginMarketplacePublication Publish(IFormFile package, string publisher, string password)
    {
        if (!CloudRules.IsUsernameValid(publisher)) throw new ArgumentException("Faça login e use um username válido para publicar.");
        VerifyPassword(password);
        if (package.Length is <= 0 or > MaximumZipBytes) throw new ArgumentException("Pacote de plugin muito grande ou vazio.");
        if (!package.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Envie um pacote .zip.");

        var staging = Path.Combine(Path.GetTempPath(), "ClipDesk", "PluginDeploy", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            var archive = Path.Combine(staging, "plugin.zip");
            using (var output = File.Create(archive)) package.CopyTo(output);
            var manifest = ReadManifest(archive);
            var id = manifest["id"]?.GetValue<string>() ?? throw new ArgumentException("O manifesto não contém o identificador do plugin.");
            var name = manifest["name"]?.GetValue<string>() ?? throw new ArgumentException("O manifesto não contém o nome do plugin.");
            var version = manifest["version"]?.GetValue<string>() ?? throw new ArgumentException("O manifesto não contém a versão do plugin.");
            if (!PluginId.IsMatch(id) || !Version.TryParse(version, out _) || string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("O manifesto do plugin é inválido.");

            lock (_gate)
            {
                Directory.CreateDirectory(PackagesRoot);
                var destination = Path.Combine(PackagesRoot, $"{id}-{version}.zip");
                if (File.Exists(destination)) throw new InvalidOperationException("Esta versão já está na loja. Publique uma versão maior.");
                StampPublisher(archive, publisher);
                File.Move(archive, destination);
                var authors = ReadAuthors(); authors[$"{id}@{version}"] = publisher;
                WriteAuthors(authors);
            }
            return new PluginMarketplacePublication(id, name, version, publisher);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
    }

    public string PackagePath(string fileName)
    {
        if (Path.GetFileName(fileName) != fileName || !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Pacote inválido.");
        var path = Path.Combine(PackagesRoot, fileName);
        if (!File.Exists(path)) throw new FileNotFoundException();
        return path;
    }

    private void VerifyPassword(string password)
    {
        var hash = configuration["PluginStore:DeployPasswordHash"];
        if (string.IsNullOrWhiteSpace(hash) || !PasswordHash.Verify(password, hash))
            throw new UnauthorizedAccessException("Senha de publicação inválida.");
    }

    private static JsonObject ReadManifest(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count is 0 or > 256) throw new ArgumentException("Pacote de plugin inválido.");
        if (archive.Entries.Sum(entry => entry.Length) > 80L * 1024 * 1024) throw new ArgumentException("Pacote de plugin muito grande.");
        var manifest = archive.GetEntry("manifest.json") ?? throw new ArgumentException("O pacote não contém manifest.json.");
        using var reader = new StreamReader(manifest.Open());
        return JsonNode.Parse(reader.ReadToEnd())?.AsObject() ?? throw new ArgumentException("manifest.json inválido.");
    }

    private static void StampPublisher(string archivePath, string publisher)
    {
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update);
        var manifest = archive.GetEntry("manifest.json") ?? throw new ArgumentException("O pacote não contém manifest.json.");
        JsonObject json;
        using (var reader = new StreamReader(manifest.Open()))
            json = JsonNode.Parse(reader.ReadToEnd())?.AsObject() ?? throw new ArgumentException("manifest.json inválido.");
        manifest.Delete();
        json["publisher"] = publisher;
        var replacement = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
        using var writer = new StreamWriter(replacement.Open());
        writer.Write(json.ToJsonString(Json));
    }

    private JsonObject? FeedEntry(string archivePath, Uri publicBase, Dictionary<string, string> authors)
    {
        try
        {
            var entry = ReadManifest(archivePath);
            var id = entry["id"]?.GetValue<string>();
            var version = entry["version"]?.GetValue<string>();
            if (id is null || version is null || !PluginId.IsMatch(id) || !Version.TryParse(version, out _)) return null;
            entry["publisher"] = authors.GetValueOrDefault($"{id}@{version}", "ClipDesk");
            entry["url"] = new Uri(publicBase, "plugin-store/packages/" + Uri.EscapeDataString(Path.GetFileName(archivePath))).AbsoluteUri;
            using var stream = File.OpenRead(archivePath);
            entry["sha256"] = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            return entry;
        }
        catch (Exception) { return null; }
    }

    private Dictionary<string, string> ReadAuthors()
    {
        try { return File.Exists(AuthorsPath) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(AuthorsPath), Json) ?? [] : []; }
        catch (JsonException) { return []; }
    }

    private void WriteAuthors(Dictionary<string, string> authors)
    {
        Directory.CreateDirectory(_root);
        var temporary = AuthorsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(authors, Json));
        File.Move(temporary, AuthorsPath, overwrite: true);
    }
}

public sealed record PluginMarketplacePublication(string Id, string Name, string Version, string Publisher);

public static class PasswordHash
{
    public static bool Verify(string password, string encoded)
    {
        var parts = encoded.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], out var rounds)) return false;
        try
        {
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(parts[2]), rounds, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String(parts[3]));
        }
        catch (FormatException) { return false; }
    }
}
