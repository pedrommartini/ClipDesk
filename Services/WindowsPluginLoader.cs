using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Windows;
using ClipDesk.PluginSdk.Windows;
using ClipDesk.PluginSdk;

namespace ClipDesk.Services;

/// <summary>Loads each plugin version from its own package directory.</summary>
public sealed class WindowsPluginLoader
{
    public sealed record PluginLoadFailure(string IncidentId, string PluginId, string? PluginVersion,
        string HostVersion, string SdkVersion, string Code, string Stage, string ExceptionType,
        DateTimeOffset OccurredAt);

    private readonly PluginCatalogService _catalog;
    private readonly Dictionary<string, PluginLoadFailure> _failures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IWindowsPlugin> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (IClipDeskPluginModule Module, IWindowsPluginRenderer Renderer)> _loadedV2 = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (InstalledPluginPackage? Package, DateTimeOffset CheckedAt)> _active =
        new(StringComparer.Ordinal);

    public WindowsPluginLoader(PluginCatalogService? catalog = null) => _catalog = catalog ?? new PluginCatalogService();

    public string? ActiveVersion(string pluginId) => Resolve(pluginId)?.Manifest.Version;
    public ClipDesk.PluginSdk.PluginManifest? Manifest(string pluginId)
    {
        var package = Resolve(pluginId);
        if (package is null) RecordUnavailable(pluginId);
        return package?.Manifest;
    }
    public PluginLoadFailure? LastFailure(string pluginId) => _failures.GetValueOrDefault(pluginId);
    public void Invalidate(string pluginId) { _active.Remove(pluginId); _failures.Remove(pluginId); }

    private void RecordFailure(string pluginId, string? version, string code, string stage, Exception? exception = null)
    {
        if (_failures.TryGetValue(pluginId, out var previous) && previous.Code == code
            && previous.Stage == stage && DateTimeOffset.UtcNow - previous.OccurredAt < TimeSpan.FromMinutes(1)) return;
        var failure = new PluginLoadFailure(Guid.NewGuid().ToString("N"), pluginId, version,
            UpdateService.CurrentVersion,
            typeof(IClipDeskPluginModule).Assembly.GetName().Version?.ToString() ?? "unknown",
            code, stage, exception?.GetType().Name ?? "None", DateTimeOffset.UtcNow);
        _failures[pluginId] = failure;
        try
        {
            Directory.CreateDirectory(AppEnvironment.DataRoot);
            File.AppendAllText(Path.Combine(AppEnvironment.DataRoot, "plugin-load-failures.jsonl"),
                JsonSerializer.Serialize(failure) + Environment.NewLine);
        }
        catch (IOException) { /* The in-memory report remains available to the user. */ }
        catch (UnauthorizedAccessException) { }
        Trace.WriteLine($"Plugin load failure {failure.IncidentId}: {failure.Code} ({failure.Stage})");
    }

    private void RecordUnavailable(string pluginId)
    {
        var diagnosis = _catalog.DiagnoseUnavailable(pluginId);
        RecordFailure(pluginId, diagnosis.Version, diagnosis.Code, "resolve");
    }

    private InstalledPluginPackage? Resolve(string pluginId)
    {
        var now = DateTimeOffset.UtcNow;
        if (_active.TryGetValue(pluginId, out var cached) && now - cached.CheckedAt < TimeSpan.FromSeconds(1))
            return cached.Package;
        var package = _catalog.GetInstalledPackage(pluginId);
        _active[pluginId] = (package, now);
        return package;
    }

    public FrameworkElement? CreateBody(string pluginId, PluginViewContext context)
    {
        string? version = null;
        var stage = "resolve";
        try
        {
            var package = Resolve(pluginId);
            if (package is null) { RecordUnavailable(pluginId); return null; }
            if (package.Manifest.Runtime != "wpf-v1") return null;
            version = package.Manifest.Version;
            var assemblyPath = Path.Combine(package.Directory, package.Manifest.EntryAssembly!);
            stage = "assembly";
            if (!File.Exists(assemblyPath)) throw new FileNotFoundException("Plugin assembly missing.");
            if (!_loaded.TryGetValue(assemblyPath, out var module))
            {
                var contextLoader = new PluginAssemblyContext(assemblyPath);
                using var moduleStream = File.OpenRead(assemblyPath);
                var assembly = contextLoader.LoadFromStream(moduleStream);
                var type = assembly.GetType(package.Manifest.EntryType!, throwOnError: true)!;
                if (!typeof(IWindowsPlugin).IsAssignableFrom(type))
                    throw new InvalidDataException("O módulo não implementa o contrato de plugins do Windows.");
                module = (IWindowsPlugin)Activator.CreateInstance(type)!;
                _loaded[assemblyPath] = module;
            }
            stage = "renderer";
            var body = module.CreateBody(context);
            _failures.Remove(pluginId);
            return body;
        }
        catch (Exception ex)
        {
            RecordFailure(pluginId, version, ex is FileNotFoundException ? "assembly_missing" : "load_failed", stage, ex);
            return null;
        }
    }

    public FrameworkElement? CreateBody(string pluginId, PluginState state, bool isDarkMode, bool isEditing,
        double width, double height, double scale, string accentColor, Action beforeChange, Action<PluginState, bool> changed,
        Action<PluginHostAction> hostAction, Action<WindowsPluginViewContext>? contextReady = null, double viewportZoom = 1,
        IPluginSharedAssets? sharedAssets = null)
    {
        string? version = null;
        var stage = "resolve";
        try
        {
            var package = Resolve(pluginId);
            if (package is null) { RecordUnavailable(pluginId); return null; }
            version = package.Manifest.Version;
            if (package.Manifest.Runtime != PluginRuntimes.PortableV2)
            {
                if (package.Manifest.Runtime != PluginRuntimes.LegacyWpf)
                    RecordFailure(pluginId, version, "unsupported_runtime", "manifest");
                return null;
            }
            var rendererEntry = package.Manifest.RendererFor(PluginPlatforms.Windows);
            if (package.Manifest.Module is null || rendererEntry is null)
            {
                RecordFailure(pluginId, version, "manifest_invalid", "manifest");
                return null;
            }
            var key = Path.Combine(package.Directory, package.Manifest.Version);
            stage = "assembly";
            if (!_loadedV2.TryGetValue(key, out var loaded))
            {
                var modulePath = Path.Combine(package.Directory, package.Manifest.Module.Assembly);
                var rendererPath = Path.Combine(package.Directory, rendererEntry.Assembly);
                if (!File.Exists(modulePath) || !File.Exists(rendererPath)) throw new FileNotFoundException("Plugin assembly missing.");
                var loader = new PluginAssemblyContext(rendererPath);
                var moduleAssembly = loader.LoadFromAssemblyPath(Path.GetFullPath(modulePath));
                var rendererAssembly = loader.LoadFromAssemblyPath(Path.GetFullPath(rendererPath));
                var moduleType = moduleAssembly.GetType(package.Manifest.Module.Type, throwOnError: true)!;
                var rendererType = rendererAssembly.GetType(rendererEntry.Type, throwOnError: true)!;
                if (!typeof(IClipDeskPluginModule).IsAssignableFrom(moduleType)
                    || !typeof(IWindowsPluginRenderer).IsAssignableFrom(rendererType))
                    throw new InvalidDataException("O pacote não implementa os contratos v2 declarados.");
                loaded = ((IClipDeskPluginModule)Activator.CreateInstance(moduleType)!,
                    (IWindowsPluginRenderer)Activator.CreateInstance(rendererType)!);
                if (!loaded.Module.Id.Equals(package.Manifest.Id, StringComparison.OrdinalIgnoreCase)
                    || loaded.Module.StateVersion != package.Manifest.StateVersion)
                    throw new InvalidDataException("A identidade ou a versão de estado do módulo não corresponde ao manifesto.");
                _loadedV2[key] = loaded;
            }
            stage = "state";
            if (state.GetInt32(PluginStateKeys.SchemaVersion) > loaded.Module.StateVersion)
            {
                RecordFailure(pluginId, version, "state_too_new", stage);
                return null;
            }
            var execution = new PluginExecutionContext(new PluginNetworkClient(package.Manifest.NetworkHosts ?? []), package.Manifest.Permissions ?? []);
            var viewContext = new WindowsPluginViewContext(loaded.Module, state, execution, isDarkMode, isEditing,
                width, height, scale, accentColor, beforeChange, changed, hostAction);
            if ((package.Manifest.Permissions ?? []).Contains(PluginPermissions.SharedAssets, StringComparer.OrdinalIgnoreCase))
                viewContext.SharedAssets = sharedAssets;
            viewContext.UpdateViewportZoom(viewportZoom);
            stage = "renderer";
            var body = loaded.Renderer.CreateBody(viewContext);
            contextReady?.Invoke(viewContext);
            _failures.Remove(pluginId);
            return body;
        }
        catch (Exception ex)
        {
            RecordFailure(pluginId, version, ex is FileNotFoundException ? "assembly_missing" : "load_failed", stage, ex);
            return null;
        }
    }

    private sealed class PluginAssemblyContext(string assemblyPath) : AssemblyLoadContext(
        $"ClipDesk.Plugin.{Path.GetFileNameWithoutExtension(assemblyPath)}.{Guid.NewGuid():N}", isCollectible: false)
    {
        private readonly AssemblyDependencyResolver _resolver = new(assemblyPath);
        private readonly string _packageDirectory = Path.GetDirectoryName(Path.GetFullPath(assemblyPath))!;

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is "ClipDesk.PluginSdk" or "ClipDesk.PluginSdk.Windows") return null;
            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            // Package ZIPs flatten published files. RID-specific entries in a
            // .deps.json may therefore not resolve even though the DLL exists.
            if (path is null && assemblyName.Name is { Length: > 0 } name
                && name == Path.GetFileName(name))
            {
                var flatPath = Path.Combine(_packageDirectory, name + ".dll");
                if (File.Exists(flatPath)) path = flatPath;
            }
            return path is null ? null : LoadFromAssemblyPath(path);
        }
    }
}
