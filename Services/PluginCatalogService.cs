using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClipDesk.PluginSdk;

namespace ClipDesk.Services;

public sealed record PluginCatalogEntry(PluginManifest Manifest, bool IsInstalled, string? InstalledVersion,
    string PackageDirectory, PluginFeedPackage? RemotePackage = null);
public sealed record InstalledPluginPackage(PluginManifest Manifest, string Directory);

/// <summary>Reads built-ins beside the app, optional installs from Documents, and built-in updates from a private cache.</summary>
public sealed class PluginCatalogService
{
    private static readonly Regex SafeId = new("^[a-z0-9]+(?:[.-][a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _bundledRoot;
    private readonly string _optionalRoot;
    private readonly string _bundledUpdateRoot;
    private readonly Version _hostVersion;

    public PluginCatalogService(string? bundledRoot = null, string? installedRoot = null, Version? hostVersion = null,
        string? bundledUpdateRoot = null)
    {
        _bundledRoot = bundledRoot ?? Path.Combine(AppContext.BaseDirectory, "Plugins", "Bundled");
        _optionalRoot = installedRoot ?? AppEnvironment.UserPluginsRoot;
        _bundledUpdateRoot = bundledUpdateRoot ?? Path.Combine(AppEnvironment.DataRoot, "Plugins");
        _hostVersion = hostVersion ?? Version.Parse(UpdateService.CurrentVersion);
    }

    public string InstalledRoot => _optionalRoot;
    public string BundledUpdateRoot => _bundledUpdateRoot;

    public IReadOnlyList<PluginCatalogEntry> LoadCatalog()
    {
        var bundled = ReadManifests(_bundledRoot).OrderBy(item => item.Manifest.SortOrder).ToArray();
        MigrateLegacyOptionalPackages(bundled.Where(item => item.Manifest.InstallByDefault)
            .Select(item => item.Manifest.Id).ToHashSet(StringComparer.OrdinalIgnoreCase));

        var catalog = bundled.Select(item =>
        {
            var installed = GetInstalledPackage(item.Manifest.Id);
            var available = ResolveAvailablePackage(item.Manifest.Id)
                            ?? new InstalledPluginPackage(item.Manifest, item.Directory);
            return installed is null
                ? new PluginCatalogEntry(available.Manifest, false, null, available.Directory)
                : new PluginCatalogEntry(installed.Manifest, true, installed.Manifest.Version, installed.Directory);
        }).ToList();

        foreach (var id in EnumeratePluginIds(_optionalRoot).Concat(EnumeratePluginIds(_bundledUpdateRoot))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (catalog.Any(item => item.Manifest.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) continue;
            var installed = GetInstalledPackage(id);
            var available = installed ?? ResolveAvailablePackage(id);
            if (available is not null)
                catalog.Add(new PluginCatalogEntry(available.Manifest, installed is not null,
                    installed?.Manifest.Version, available.Directory));
        }
        return catalog.OrderBy(item => item.Manifest.SortOrder).ThenBy(item => item.Manifest.Name).ToArray();
    }

    public InstalledPluginPackage? GetInstalledPackage(string pluginId)
    {
        if (!SafeId.IsMatch(pluginId)) return null;
        if (IsDisabled(pluginId)) return null;
        var bundled = ReadBundledPackage(pluginId);
        var optional = ReadStoredPackage(_optionalRoot, pluginId);
        var privateUpdate = ReadStoredPackage(_bundledUpdateRoot, pluginId);
        if (bundled is null || !bundled.Manifest.InstallByDefault) return optional ?? privateUpdate;
        return SelectNewest(pluginId, bundled, (_bundledUpdateRoot, privateUpdate), (_optionalRoot, optional));
    }

    private InstalledPluginPackage? ResolveAvailablePackage(string pluginId)
    {
        var bundled = ReadBundledPackage(pluginId);
        var optional = ReadStoredPackage(_optionalRoot, pluginId);
        var privateUpdate = ReadStoredPackage(_bundledUpdateRoot, pluginId);
        if (bundled is null) return optional ?? privateUpdate;
        if (!bundled.Manifest.InstallByDefault) return optional ?? privateUpdate ?? bundled;
        return SelectNewest(pluginId, bundled, (_bundledUpdateRoot, privateUpdate), (_optionalRoot, optional));
    }

    private InstalledPluginPackage SelectNewest(string pluginId, InstalledPluginPackage bundled,
        params (string Root, InstalledPluginPackage? Package)[] candidates)
    {
        var selected = bundled;
        foreach (var (root, candidate) in candidates)
            if (candidate is not null
                && (Version.Parse(candidate.Manifest.Version) > Version.Parse(selected.Manifest.Version)
                    || Version.Parse(candidate.Manifest.Version) == Version.Parse(selected.Manifest.Version)
                    && IsExplicitPackageActive(root, pluginId, candidate.Directory)))
                selected = candidate;
        return selected;
    }

    private static bool IsExplicitPackageActive(string root, string pluginId, string directory)
    {
        try
        {
            var pointer = Path.Combine(root, pluginId, "current-package.txt");
            return File.Exists(pointer) && Path.GetFileName(directory)
                .Equals(File.ReadAllText(pointer).Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private InstalledPluginPackage? ReadBundledPackage(string pluginId)
    {
        var directory = SafeChild(_bundledRoot, pluginId);
        var manifest = TryReadManifest(Path.Combine(directory, "manifest.json"));
        return manifest is not null && manifest.Id == pluginId
               && PluginCompatibility.Supports(manifest, _hostVersion, PluginCompatibility.WindowsApiVersion, "windows")
               && EntryExists(manifest, directory)
            ? new InstalledPluginPackage(manifest, directory) : null;
    }

    private InstalledPluginPackage? ReadStoredPackage(string root, string pluginId)
    {
        var pluginRoot = SafeChild(root, pluginId);
        if (!Directory.Exists(pluginRoot)) return null;

        var packagePointer = Path.Combine(pluginRoot, "current-package.txt");
        var versionPointer = Path.Combine(pluginRoot, "current-version.txt");
        var candidates = new List<string>();
        try
        {
            if (File.Exists(packagePointer)) candidates.Add(File.ReadAllText(packagePointer).Trim());
            if (File.Exists(versionPointer)) candidates.Add(File.ReadAllText(versionPointer).Trim());
            candidates.AddRange(Directory.EnumerateDirectories(pluginRoot).Select(Path.GetFileName).OfType<string>()
                .Where(version => Version.TryParse(version, out _)).OrderByDescending(Version.Parse));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }

        foreach (var slot in candidates.Where(IsSafeSlot).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var directory = SafeChild(pluginRoot, slot);
            var manifest = TryReadManifest(Path.Combine(directory, "manifest.json"));
            if (manifest is null || manifest.Id != pluginId || !Version.TryParse(manifest.Version, out _)
                || !PluginCompatibility.Supports(manifest, _hostVersion, PluginCompatibility.WindowsApiVersion, "windows")
                || !EntryExists(manifest, directory)) continue;
            return new InstalledPluginPackage(manifest, directory);
        }
        return null;
    }

    private static IEnumerable<string> EnumeratePluginIds(string root) => Directory.Exists(root)
        ? Directory.EnumerateDirectories(root).Select(Path.GetFileName).OfType<string>() : [];

    private void MigrateLegacyOptionalPackages(HashSet<string> bundledIds)
    {
        if (Path.GetFullPath(_optionalRoot).Equals(Path.GetFullPath(_bundledUpdateRoot), StringComparison.OrdinalIgnoreCase)) return;
        foreach (var id in EnumeratePluginIds(_bundledUpdateRoot))
        {
            if (bundledIds.Contains(id) || !SafeId.IsMatch(id) || ReadStoredPackage(_optionalRoot, id) is not null) continue;
            if (ReadStoredPackage(_bundledUpdateRoot, id) is not { } legacy) continue;
            try { Install(legacy.Manifest, legacy.Directory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // Keep the old active package readable if Documents is unavailable.
                Trace.WriteLine($"Não foi possível migrar o plugin {id} para Documentos: {ex}");
            }
        }
    }

    public PluginCatalogEntry Install(PluginManifest manifest, string packageDirectory)
        => InstallCore(manifest, packageDirectory, NormalizeVersion(manifest.Version), reuseExisting: true);

    public PluginCatalogEntry Repair(PluginManifest manifest, string packageDirectory)
        => InstallCore(manifest, packageDirectory,
            $"{NormalizeVersion(manifest.Version)}-repair-{Guid.NewGuid():N}", reuseExisting: false);

    public PluginCatalogEntry Enable(string pluginId)
    {
        if (!SafeId.IsMatch(pluginId) || ResolveAvailablePackage(pluginId) is not { } available)
            throw new InvalidDataException("O plugin não possui um pacote disponível para ativação.");
        var bundled = ReadBundledPackage(pluginId);
        if (bundled is not null && !bundled.Manifest.InstallByDefault
            && ReadStoredPackage(_optionalRoot, pluginId) is null
            && ReadStoredPackage(_bundledUpdateRoot, pluginId) is null)
            return Install(available.Manifest, available.Directory);
        var marker = DisabledMarker(pluginId);
        if (File.Exists(marker)) File.Delete(marker);
        return new PluginCatalogEntry(available.Manifest, true, available.Manifest.Version, available.Directory);
    }

    public bool Uninstall(string pluginId)
    {
        if (!SafeId.IsMatch(pluginId) || ResolveAvailablePackage(pluginId) is null) return false;
        var marker = DisabledMarker(pluginId);
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        var temporary = marker + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, DateTimeOffset.UtcNow.ToString("O"));
        File.Move(temporary, marker, overwrite: true);
        return true;
    }

    private PluginCatalogEntry InstallCore(PluginManifest manifest, string packageDirectory, string slot,
        bool reuseExisting)
    {
        Validate(manifest);
        if (!PluginCompatibility.Supports(manifest, _hostVersion, PluginCompatibility.WindowsApiVersion, "windows"))
            throw new InvalidDataException($"{manifest.Name} não é compatível com esta versão do ClipDesk.");

        var version = NormalizeVersion(manifest.Version);
        var destinationRoot = ReadBundledPackage(manifest.Id)?.Manifest.InstallByDefault == true
            ? _bundledUpdateRoot : _optionalRoot;
        var pluginRoot = SafeChild(destinationRoot, manifest.Id);
        Directory.CreateDirectory(pluginRoot);
        var destination = SafeChild(pluginRoot, slot);
        if (reuseExisting && Directory.Exists(destination))
        {
            var existing = TryReadManifest(Path.Combine(destination, "manifest.json"));
            if (existing is null || existing.Id != manifest.Id || existing.Version != manifest.Version
                || !EntryExists(existing, destination))
                throw new InvalidDataException("Já existe um pacote incompleto para esta versão do plugin.");
        }
        else
        {
            var staging = SafeChild(pluginRoot, $".staging-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(staging);
                CopyPackage(packageDirectory, staging);
                var copied = TryReadManifest(Path.Combine(staging, "manifest.json"));
                if (copied is null || copied.Id != manifest.Id || copied.Version != manifest.Version
                    || !EntryExists(copied, staging))
                    throw new InvalidDataException("O pacote do plugin não corresponde ao manifesto anunciado.");
                Directory.Move(staging, destination);
            }
            finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
        }

        WritePointer(pluginRoot, "current-package.txt", slot);
        WritePointer(pluginRoot, "current-version.txt", version);
        var disabled = Path.Combine(pluginRoot, "disabled.txt");
        if (File.Exists(disabled)) File.Delete(disabled);
        return new PluginCatalogEntry(manifest, true, version, destination);
    }

    private string DisabledMarker(string pluginId)
    {
        var root = ReadBundledPackage(pluginId)?.Manifest.InstallByDefault == true
            ? _bundledUpdateRoot : _optionalRoot;
        return Path.Combine(SafeChild(root, pluginId), "disabled.txt");
    }

    private bool IsDisabled(string pluginId) => File.Exists(DisabledMarker(pluginId));

    private static void WritePointer(string pluginRoot, string fileName, string value)
    {
        var pointer = Path.Combine(pluginRoot, fileName);
        var temporary = Path.Combine(pluginRoot, $".{fileName}-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temporary, value);
        File.Move(temporary, pointer, overwrite: true);
    }

    private static bool IsSafeSlot(string slot) => !string.IsNullOrWhiteSpace(slot)
        && slot == Path.GetFileName(slot) && slot is not "." and not "..";

    private IEnumerable<(PluginManifest Manifest, string Directory)> ReadManifests(string root)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (var manifestPath in Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories))
        {
            var manifest = TryReadManifest(manifestPath);
            if (manifest is null || !PluginCompatibility.Supports(manifest, _hostVersion, PluginCompatibility.WindowsApiVersion, "windows")) continue;
            Validate(manifest);
            yield return (manifest, Path.GetDirectoryName(manifestPath)!);
        }
    }

    public static PluginManifest? TryReadManifest(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(path), Json) : null; }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    private static void Validate(PluginManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id) || !SafeId.IsMatch(manifest.Id))
            throw new InvalidDataException("Identificador de plugin inválido.");
        if (string.IsNullOrWhiteSpace(manifest.Name)) throw new InvalidDataException($"O plugin {manifest.Id} não possui nome.");
        _ = NormalizeVersion(manifest.Version);
        if (manifest.ManifestVersion is < 1 or > 2) throw new InvalidDataException($"Manifesto não suportado: {manifest.ManifestVersion}.");
        if (manifest.ManifestVersion == 1 && manifest.Runtime == PluginRuntimes.WpfV1 && !IsSafeFileName(manifest.EntryAssembly))
            throw new InvalidDataException("Montagem de plugin inválida.");
        if (manifest.ManifestVersion == 2)
        {
            if (!IsSafeFileName(manifest.Module?.Assembly)
                || (manifest.Renderers ?? []).Any(renderer => !IsSafeFileName(renderer.Assembly)))
                throw new InvalidDataException("Uma montagem v2 é inválida.");
            if ((manifest.NetworkHosts ?? []).Any(host => Uri.CheckHostName(host) == UriHostNameType.Unknown
                                                  || host.Contains('/') || host.Contains(':')))
                throw new InvalidDataException("A lista de hosts de rede contém um endereço inválido.");
            if ((manifest.NetworkHosts?.Count ?? 0) > 0 && !(manifest.Permissions ?? []).Contains(PluginPermissions.Network, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("O plugin declara hosts sem solicitar a permissão de rede.");
            if ((manifest.Capabilities ?? []).Contains(PluginCapabilities.AddFilesToBoard, StringComparer.OrdinalIgnoreCase)
                && !(manifest.Permissions ?? []).Contains(PluginPermissions.FileRead, StringComparer.OrdinalIgnoreCase)
                && !(manifest.Permissions ?? []).Contains(PluginPermissions.FileWrite, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("O plugin declara arquivos na mesa sem solicitar acesso a arquivos.");
        }
    }

    private static bool EntryExists(PluginManifest manifest, string directory)
    {
        if (manifest.ManifestVersion == 1) return manifest.Runtime != PluginRuntimes.WpfV1
            || IsSafeFileName(manifest.EntryAssembly) && File.Exists(Path.Combine(directory, manifest.EntryAssembly!));
        var renderer = manifest.RendererFor(PluginPlatforms.Windows);
        return IsSafeFileName(manifest.Module?.Assembly) && File.Exists(Path.Combine(directory, manifest.Module!.Assembly))
            && renderer is not null && IsSafeFileName(renderer.Assembly) && File.Exists(Path.Combine(directory, renderer.Assembly));
    }

    private static bool IsSafeFileName(string? value) => !string.IsNullOrWhiteSpace(value)
        && value == Path.GetFileName(value) && value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeVersion(string version)
    {
        if (!Version.TryParse(version, out var parsed)) throw new InvalidDataException("Versão de plugin inválida.");
        return parsed.ToString();
    }

    private static string SafeChild(string parent, string child)
    {
        var root = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var result = Path.GetFullPath(Path.Combine(parent, child));
        if (!result.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Caminho de plugin inválido.");
        return result;
    }

    private static void CopyPackage(string source, string destination)
    {
        var sourceRoot = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destinationRoot = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.GetFullPath(Path.Combine(destination, relative));
            if (!Path.GetFullPath(file).StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase)
                || !target.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Conteúdo de plugin fora do pacote.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
