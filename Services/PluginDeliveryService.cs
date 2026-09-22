using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClipDesk.PluginSdk;

namespace ClipDesk.Services;

public sealed class PluginFeed
{
    public int SchemaVersion { get; init; } = 1;
    public IReadOnlyList<PluginFeedPackage> Packages { get; init; } = [];
}

public sealed class PluginFeedPackage
{
    public int ManifestVersion { get; init; } = 1;
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Version { get; init; } = "";
    public string Url { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public string Runtime { get; init; } = "legacy-wpf";
    public string MinimumHostVersion { get; init; } = "0.3.3";
    public string? MaximumHostVersion { get; init; }
    public int PluginApiVersion { get; init; }
    public string? EntryAssembly { get; init; }
    public string? EntryType { get; init; }
    public IReadOnlyList<string> Platforms { get; init; } = ["windows"];
    public string IconGlyph { get; init; } = "\uE87B";
    public string AccentColor { get; init; } = "#9B7DFF";
    public int SortOrder { get; init; }
    public PluginSize DefaultSize { get; init; } = new();
    public Dictionary<string, string> DefaultContent { get; init; } = [];
    public PluginEntryPoint? Module { get; init; }
    public IReadOnlyList<PluginRendererEntryPoint> Renderers { get; init; } = [];
    public int StateVersion { get; init; } = 1;
    public IReadOnlyList<string> Permissions { get; init; } = [];
    public IReadOnlyList<string> Capabilities { get; init; } = [PluginCapabilities.BoardWidget];
    public IReadOnlyList<string> NetworkHosts { get; init; } = [];

    public PluginManifest CompatibilityManifest() => new()
    {
        ManifestVersion = ManifestVersion, Id = Id, Name = Name, Description = Description, Version = Version, Runtime = Runtime,
        IconGlyph = IconGlyph, AccentColor = AccentColor, SortOrder = SortOrder,
        DefaultSize = DefaultSize ?? new PluginSize(), DefaultContent = DefaultContent ?? [],
        MinimumHostVersion = MinimumHostVersion,
        MaximumHostVersion = MaximumHostVersion, PluginApiVersion = PluginApiVersion,
        EntryAssembly = EntryAssembly, EntryType = EntryType, Platforms = Platforms,
        Module = Module, Renderers = Renderers ?? [], StateVersion = StateVersion, Permissions = Permissions ?? [],
        Capabilities = Capabilities ?? [], NetworkHosts = NetworkHosts ?? []
    };
}

/// <summary>Downloads first-party plugin releases independently of the application updater.</summary>
public sealed class PluginDeliveryService
{
    public const string ProductionFeedUrl = "https://raw.githubusercontent.com/pedrommartini/ClipDesk/main/PluginPackages/feed.json";
    public const string DevelopmentFeedUrl = "https://raw.githubusercontent.com/pedrommartini/ClipDesk/refs/heads/codex/clipdesk-dev/PluginPackages/feed.json";
#if CLIPDESK_DEV
    public const string DefaultFeedUrl = DevelopmentFeedUrl;
#else
    public const string DefaultFeedUrl = ProductionFeedUrl;
#endif
    private const long MaximumZipBytes = 25L * 1024 * 1024;
    private const long MaximumExtractedBytes = 80L * 1024 * 1024;
    private static readonly Regex Sha256Pattern = new("^[a-fA-F0-9]{64}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    private readonly PluginCatalogService _catalog;
    private readonly HttpClient _http;
    private readonly Uri _feedUri;
    private readonly bool _requireOfficialDownloads;

    public PluginDeliveryService(PluginCatalogService catalog, HttpClient? http = null, Uri? feedUri = null)
    {
        _catalog = catalog;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ClipDesk-Plugins/1.0");
        var configuredFeed = Environment.GetEnvironmentVariable("CLIPDESK_PLUGIN_FEED_URL");
        var hasConfiguredFeed = Uri.TryCreate(configuredFeed, UriKind.Absolute, out var configured)
            && (configured.Scheme == Uri.UriSchemeHttps
                || AppEnvironment.IsDevelopment && configured.IsLoopback && configured.Scheme == Uri.UriSchemeHttp);
        _feedUri = feedUri ?? (hasConfiguredFeed ? configured! : new Uri(DefaultFeedUrl));
        _requireOfficialDownloads = feedUri is null && !hasConfiguredFeed;
    }

    public async Task<PluginFeed?> FetchFeedAsync(CancellationToken cancellationToken = default)
    {
        if (AppEnvironment.IsTestClient) return null;
        using var response = await _http.GetAsync(_feedUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        var feed = await JsonSerializer.DeserializeAsync<PluginFeed>(body, Json, cancellationToken)
                   ?? throw new InvalidDataException("Catálogo de plugins vazio.");
        if (feed.SchemaVersion != 1) throw new InvalidDataException("Versão do catálogo de plugins não suportada.");
        return feed;
    }

    public async Task<IReadOnlyList<InstalledPluginPackage>> UpdateInstalledAsync(CancellationToken cancellationToken = default)
    {
        var feed = await FetchFeedAsync(cancellationToken);
        return feed is null ? [] : await UpdateInstalledAsync(feed, cancellationToken);
    }

    public async Task<IReadOnlyList<InstalledPluginPackage>> UpdateInstalledAsync(PluginFeed feed,
        CancellationToken cancellationToken = default)
    {
        var installed = _catalog.LoadCatalog().Where(entry => entry.IsInstalled).ToDictionary(entry => entry.Manifest.Id);
        var updates = SelectUpdates(feed, installed.Values, Version.Parse(UpdateService.CurrentVersion));
        var applied = new List<InstalledPluginPackage>();
        foreach (var package in updates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            applied.Add(await InstallRemoteAsync(package, cancellationToken));
        }
        return applied;
    }

    public async Task<InstalledPluginPackage> InstallRemoteAsync(PluginFeedPackage package,
        CancellationToken cancellationToken = default)
    {
        if (!PluginCompatibility.Supports(package.CompatibilityManifest(), Version.Parse(UpdateService.CurrentVersion),
                PluginCompatibility.WindowsApiVersion, "windows") || package.Sha256 is null
            || !Sha256Pattern.IsMatch(package.Sha256))
            throw new InvalidDataException("Versão do plugin incompatível com este ClipDesk.");
        var packageDirectory = await DownloadAndExtractAsync(package, cancellationToken);
        try
        {
            var manifest = PluginCatalogService.TryReadManifest(Path.Combine(packageDirectory, "manifest.json"));
            if (manifest is null || manifest.Id != package.Id || manifest.Version != package.Version
                || manifest.Runtime != package.Runtime || manifest.PluginApiVersion != package.PluginApiVersion
                || !PluginCompatibility.Supports(manifest, Version.Parse(UpdateService.CurrentVersion),
                    PluginCompatibility.WindowsApiVersion, "windows"))
                throw new InvalidDataException($"Pacote inválido para {package.Id}.");
            _catalog.Install(manifest, packageDirectory);
            return _catalog.GetInstalledPackage(package.Id)
                   ?? throw new InvalidDataException("O plugin não ficou disponível depois da instalação.");
        }
        finally { Directory.Delete(Path.GetDirectoryName(packageDirectory)!, recursive: true); }
    }

    public static IReadOnlyList<PluginCatalogEntry> AddRemoteEntries(PluginFeed feed,
        IReadOnlyList<PluginCatalogEntry> local, Version hostVersion)
    {
        var result = local.ToList();
        var known = local.Select(entry => entry.Manifest.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var package in (feed.Packages ?? [])
                     .Where(package => !string.IsNullOrWhiteSpace(package.Id)
                         && !string.IsNullOrWhiteSpace(package.Name)
                         && package.Sha256 is not null && Sha256Pattern.IsMatch(package.Sha256)
                         && PluginCompatibility.Supports(package.CompatibilityManifest(), hostVersion,
                             PluginCompatibility.WindowsApiVersion, "windows"))
                     .GroupBy(package => package.Id)
                     .Select(group => group.MaxBy(package => Version.Parse(package.Version))!))
        {
            if (known.Add(package.Id)) result.Add(new PluginCatalogEntry(package.CompatibilityManifest(), false, null, "", package));
        }
        return result.OrderBy(entry => entry.Manifest.SortOrder).ThenBy(entry => entry.Manifest.Name).ToArray();
    }

    public static IReadOnlyList<PluginFeedPackage> SelectUpdates(PluginFeed feed,
        IEnumerable<PluginCatalogEntry> installed, Version hostVersion)
    {
        var installedVersions = installed.Where(entry => entry.IsInstalled && Version.TryParse(entry.InstalledVersion, out _))
            .ToDictionary(entry => entry.Manifest.Id, entry => Version.Parse(entry.InstalledVersion!));
        return (feed.Packages ?? [])
            .Where(package => !string.IsNullOrWhiteSpace(package.Id)
                && installedVersions.TryGetValue(package.Id, out var current)
                && Version.TryParse(package.Version, out var candidate) && candidate > current
                && package.Sha256 is not null && Sha256Pattern.IsMatch(package.Sha256)
                && PluginCompatibility.Supports(package.CompatibilityManifest(), hostVersion,
                    PluginCompatibility.WindowsApiVersion, "windows"))
            .GroupBy(package => package.Id)
            .Select(group => group.MaxBy(package => Version.Parse(package.Version))!)
            .ToArray();
    }

    private async Task<string> DownloadAndExtractAsync(PluginFeedPackage package, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(package.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || _requireOfficialDownloads && (uri.Host != "github.com"
                || !uri.AbsolutePath.StartsWith("/pedrommartini/ClipDesk/releases/download/", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Endereço do pacote de plugin não autorizado.");

        var workDirectory = Path.Combine(Path.GetTempPath(), "ClipDesk", "PluginDownloads", Guid.NewGuid().ToString("N"));
        var packagePath = Path.Combine(workDirectory, "plugin.zip");
        var extractDirectory = Path.Combine(workDirectory, "contents");
        Directory.CreateDirectory(extractDirectory);
        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaximumZipBytes) throw new InvalidDataException("Pacote de plugin muito grande.");
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = File.Create(packagePath))
            {
                var buffer = new byte[64 * 1024];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    total += read;
                    if (total > MaximumZipBytes) throw new InvalidDataException("Pacote de plugin muito grande.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
            await using (var packageFile = File.OpenRead(packagePath))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(packageFile, cancellationToken));
                if (!hash.Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("A verificação de integridade do plugin não confere.");
            }
            ExtractSafely(packagePath, extractDirectory);
            return extractDirectory;
        }
        catch
        {
            if (Directory.Exists(workDirectory)) Directory.Delete(workDirectory, recursive: true);
            throw;
        }
    }

    private static void ExtractSafely(string zipPath, string destination)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count > 256) throw new InvalidDataException("Pacote de plugin com arquivos demais.");
        var destinationRoot = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        long expandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var path = Path.GetFullPath(Path.Combine(destination, relative));
            if (!path.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Caminho inválido dentro do pacote de plugin.");
            expandedBytes += entry.Length;
            if (expandedBytes > MaximumExtractedBytes) throw new InvalidDataException("Pacote de plugin descompactado muito grande.");
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(path); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path, overwrite: false);
        }
    }
}
