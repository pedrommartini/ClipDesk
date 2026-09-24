using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows;
using ClipDesk.PluginSdk.Windows;
using ClipDesk.PluginSdk;

namespace ClipDesk.Services;

/// <summary>Loads each plugin version from its own package directory.</summary>
public sealed class WindowsPluginLoader
{
    private readonly PluginCatalogService _catalog;
    private readonly Dictionary<string, IWindowsPlugin> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (IClipDeskPluginModule Module, IWindowsPluginRenderer Renderer)> _loadedV2 = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (InstalledPluginPackage? Package, DateTimeOffset CheckedAt)> _active =
        new(StringComparer.Ordinal);

    public WindowsPluginLoader(PluginCatalogService? catalog = null) => _catalog = catalog ?? new PluginCatalogService();

    public string? ActiveVersion(string pluginId) => Resolve(pluginId)?.Manifest.Version;
    public ClipDesk.PluginSdk.PluginManifest? Manifest(string pluginId) => Resolve(pluginId)?.Manifest;
    public void Invalidate(string pluginId) => _active.Remove(pluginId);

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
        try
        {
            var package = Resolve(pluginId);
            if (package is null || package.Manifest.Runtime != "wpf-v1") return null;
            var assemblyPath = Path.Combine(package.Directory, package.Manifest.EntryAssembly!);
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
            return module.CreateBody(context);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Falha ao carregar plugin {pluginId}: {ex}");
            return null;
        }
    }

    public FrameworkElement? CreateBody(string pluginId, PluginState state, bool isDarkMode, bool isEditing,
        double width, double height, double scale, string accentColor, Action beforeChange, Action<PluginState, bool> changed,
        Action<PluginHostAction> hostAction, Action<WindowsPluginViewContext>? contextReady = null)
    {
        try
        {
            var package = Resolve(pluginId);
            if (package is null || package.Manifest.Runtime != PluginRuntimes.PortableV2) return null;
            var rendererEntry = package.Manifest.RendererFor(PluginPlatforms.Windows);
            if (package.Manifest.Module is null || rendererEntry is null) return null;
            var key = Path.Combine(package.Directory, package.Manifest.Version);
            if (!_loadedV2.TryGetValue(key, out var loaded))
            {
                var modulePath = Path.Combine(package.Directory, package.Manifest.Module.Assembly);
                var rendererPath = Path.Combine(package.Directory, rendererEntry.Assembly);
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
            var execution = new PluginExecutionContext(new PluginNetworkClient(package.Manifest.NetworkHosts ?? []), package.Manifest.Permissions ?? []);
            var viewContext = new WindowsPluginViewContext(loaded.Module, state, execution, isDarkMode, isEditing,
                width, height, scale, accentColor, beforeChange, changed, hostAction);
            var body = loaded.Renderer.CreateBody(viewContext);
            contextReady?.Invoke(viewContext);
            return body;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Falha ao carregar plugin v2 {pluginId}: {ex}");
            return null;
        }
    }

    private sealed class PluginAssemblyContext(string assemblyPath) : AssemblyLoadContext(
        $"ClipDesk.Plugin.{Path.GetFileNameWithoutExtension(assemblyPath)}.{Guid.NewGuid():N}", isCollectible: false)
    {
        private readonly AssemblyDependencyResolver _resolver = new(assemblyPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is "ClipDesk.PluginSdk" or "ClipDesk.PluginSdk.Windows") return null;
            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }
    }
}
