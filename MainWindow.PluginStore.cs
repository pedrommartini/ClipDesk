using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ClipDesk.Core;
using ClipDesk.PluginSdk;
using ClipDesk.Services;

namespace ClipDesk;

public partial class MainWindow
{
    private readonly PluginCatalogService _pluginCatalogService = new();
    private IReadOnlyList<PluginCatalogEntry> _pluginCatalogEntries = [];
    private Point _pluginStoreCanvasPoint;
    private int _pluginStoreAnimationVersion;
    private string? _pluginCatalogLoadError;
    private PluginFeed? _remotePluginFeed;
    private bool _pluginFeedCheckInProgress;
    private DateTimeOffset _lastPluginFeedCheck;
    private readonly DispatcherTimer _pluginUpdateTimer = new() { Interval = TimeSpan.FromMinutes(30) };
    private bool _pluginMaintenanceInProgress;

    private void InitializePluginStore()
    {
        try
        {
            _pluginCatalogEntries = _pluginCatalogService.LoadCatalog();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _pluginCatalogEntries = [];
            _pluginCatalogLoadError = ex.Message;
        }
        PluginStore.Bind(_pluginCatalogEntries);
        PluginStore.CloseRequested += (_, _) => HidePluginStore();
        PluginStore.PluginActionRequested += PluginStoreActionRequested;
        PluginStore.PluginRepairRequested += PluginStoreRepairRequested;
        PluginStore.PluginUninstallRequested += PluginStoreUninstallRequested;
        _pluginUpdateTimer.Tick += (_, _) => _ = CheckForPluginUpdatesAsync();
        _pluginUpdateTimer.Start();
        Closed += (_, _) => _pluginUpdateTimer.Stop();
    }

    private void ShowPluginStore(Point canvasPoint)
    {
        _ = CheckForPluginUpdatesAsync();
        _pluginStoreCanvasPoint = canvasPoint;
        PluginStore.ShowCatalog();
        PluginStoreOverlay.BeginAnimation(OpacityProperty, null);
        PluginStoreOverlay.Visibility = Visibility.Visible;
        PluginStoreOverlay.Opacity = 0;
        var version = ++_pluginStoreAnimationVersion;
        var fade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fade.Completed += (_, _) =>
        {
            if (version != _pluginStoreAnimationVersion) return;
            PluginStoreOverlay.BeginAnimation(OpacityProperty, null);
            PluginStoreOverlay.Opacity = 1;
        };
        PluginStoreOverlay.BeginAnimation(OpacityProperty, fade);
        Dispatcher.BeginInvoke(PluginStore.FocusSearch, DispatcherPriority.Input);
        if (_pluginCatalogLoadError is not null) ShowToast($"Não foi possível carregar os plugins: {_pluginCatalogLoadError}");
    }

    private async Task CheckForPluginUpdatesAsync()
    {
        if (AppEnvironment.IsTestClient || _pluginFeedCheckInProgress
            || DateTimeOffset.UtcNow - _lastPluginFeedCheck < TimeSpan.FromMinutes(15)) return;
        _pluginFeedCheckInProgress = true;
        _lastPluginFeedCheck = DateTimeOffset.UtcNow;
        try
        {
            var delivery = new PluginDeliveryService(_pluginCatalogService);
            var feed = await delivery.FetchFeedAsync();
            if (feed is null || !IsLoaded) return;
            var updated = await delivery.UpdateInstalledAsync(feed);
            _remotePluginFeed = feed;
            ReloadPluginCatalog();
            if (updated.Count == 0) return;
            var versions = updated.ToDictionary(item => item.Manifest.Id, item => item.Manifest.Version);
            foreach (var pluginId in versions.Keys) Views.BoardObjectView.InvalidateExternalPlugin(pluginId);
            foreach (var board in _workspaces)
            foreach (var obj in board.Objects)
            {
                if (obj.PluginId is not { } id || !versions.TryGetValue(id, out var version)) continue;
                obj.PluginVersion = version;
                obj.UpdatedAt = DateTimeOffset.UtcNow;
            }
            foreach (var view in WorkspaceCanvas.Children.OfType<Views.BoardObjectView>())
            {
                if (view.Object.PluginId is not { } id || !versions.TryGetValue(id, out var version)) continue;
                view.RefreshFromObject();
                QueueBoardObjectRealtime(view.Object);
            }
            Save();
            if (IsVisible) ShowToast(updated.Count == 1 ? "Plugin atualizado" : $"{updated.Count} plugins atualizados");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Verificação de plugins falhou: {ex}");
        }
        finally { _pluginFeedCheckInProgress = false; }
    }

    private void ReloadPluginCatalog()
    {
        var local = _pluginCatalogService.LoadCatalog();
        _pluginCatalogEntries = _remotePluginFeed is null ? local
            : PluginDeliveryService.AddRemoteEntries(_remotePluginFeed, local, Version.Parse(UpdateService.CurrentVersion));
        PluginStore.Bind(_pluginCatalogEntries);
    }

    private void HidePluginStore()
    {
        if (PluginStoreOverlay.Visibility != Visibility.Visible) return;
        var version = ++_pluginStoreAnimationVersion;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(130))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) =>
        {
            if (version != _pluginStoreAnimationVersion) return;
            PluginStoreOverlay.BeginAnimation(OpacityProperty, null);
            PluginStoreOverlay.Opacity = 0;
            PluginStoreOverlay.Visibility = Visibility.Collapsed;
        };
        PluginStoreOverlay.BeginAnimation(OpacityProperty, fade);
    }

    private async void PluginStoreActionRequested(PluginCatalogEntry entry)
    {
        var waitingViews = !entry.IsInstalled
            ? WorkspaceCanvas.Children.OfType<Views.BoardObjectView>()
                .Where(view => view.Object.PluginId == entry.Manifest.Id).ToArray()
            : [];
        if (!entry.IsInstalled)
        {
            try
            {
                if (entry.RemotePackage is { } remote)
                    await new PluginDeliveryService(_pluginCatalogService).InstallRemoteAsync(remote);
                else _pluginCatalogService.Enable(entry.Manifest.Id);
                Views.BoardObjectView.InvalidateExternalPlugin(entry.Manifest.Id);
                ReloadPluginCatalog();
                entry = _pluginCatalogEntries.Single(item => item.Manifest.Id == entry.Manifest.Id);
                foreach (var view in waitingViews) view.RefreshFromObject();
                ShowToast($"{entry.Manifest.Name} instalado");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or HttpRequestException)
            {
                ShowToast($"Não foi possível instalar: {ex.Message}");
                return;
            }
        }

        _canvasContextPoint = _pluginStoreCanvasPoint;
        HidePluginStore();
        if (waitingViews.Length > 0)
        {
            SelectBoardObject(waitingViews[0]);
            ShowToast($"{entry.Manifest.Name} disponível na mesa");
            return;
        }
        if (!AddInstalledPluginToCanvas(entry))
        {
            ShowToast("Este plugin ainda não possui uma versão compatível com o Windows.");
            return;
        }
        ShowToast($"{entry.Manifest.Name} adicionado à mesa");
    }

    private async void PluginStoreRepairRequested(PluginCatalogEntry entry)
    {
        if (_pluginMaintenanceInProgress || !entry.IsInstalled) return;
        _pluginMaintenanceInProgress = true;
        try
        {
            InstalledPluginPackage repaired;
            if (entry.RemotePackage is { } remote)
                repaired = await new PluginDeliveryService(_pluginCatalogService).RepairRemoteAsync(remote);
            else
            {
                _pluginCatalogService.Repair(entry.Manifest, entry.PackageDirectory);
                repaired = _pluginCatalogService.GetInstalledPackage(entry.Manifest.Id)
                           ?? throw new InvalidDataException("O plugin não ficou disponível após o reparo.");
            }
            ApplyPluginPackageChange(repaired.Manifest.Id, repaired.Manifest.Version, synchronizeVersion: true);
            ReloadPluginCatalog();
            ShowToast($"{repaired.Manifest.Name} reparado");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or HttpRequestException)
        {
            ShowToast($"Não foi possível reparar: {ex.Message}");
        }
        finally { _pluginMaintenanceInProgress = false; }
    }

    private void PluginStoreUninstallRequested(PluginCatalogEntry entry)
    {
        if (_pluginMaintenanceInProgress || !entry.IsInstalled) return;
        _pluginMaintenanceInProgress = true;
        try
        {
            if (!_pluginCatalogService.Uninstall(entry.Manifest.Id))
                throw new InvalidDataException("O plugin não está instalado.");
            ApplyPluginPackageChange(entry.Manifest.Id, version: null, synchronizeVersion: false);
            ReloadPluginCatalog();
            ShowToast($"{entry.Manifest.Name} desinstalado · dados da mesa preservados");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ShowToast($"Não foi possível desinstalar: {ex.Message}");
        }
        finally { _pluginMaintenanceInProgress = false; }
    }

    private void ApplyPluginPackageChange(string pluginId, string? version, bool synchronizeVersion)
    {
        Views.BoardObjectView.InvalidateExternalPlugin(pluginId);
        if (synchronizeVersion && version is not null)
        {
            foreach (var board in _workspaces)
            foreach (var obj in board.Objects.Where(obj => obj.PluginId == pluginId))
            {
                obj.PluginVersion = version;
                obj.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
        foreach (var view in WorkspaceCanvas.Children.OfType<Views.BoardObjectView>()
                     .Where(view => view.Object.PluginId == pluginId))
        {
            view.RefreshFromObject();
            if (synchronizeVersion) QueueBoardObjectRealtime(view.Object);
        }
        if (synchronizeVersion) Save();
    }

    private bool AddInstalledPluginToCanvas(PluginCatalogEntry entry)
    {
        if (entry.Manifest.Runtime is not (PluginRuntimes.WpfV1 or PluginRuntimes.PortableV2)) return false;
        var kind = entry.Manifest.Id switch
        {
            BuiltInPluginIds.Checklist => BoardObjectKind.Checklist,
            BuiltInPluginIds.Calculator => BoardObjectKind.Calculator,
            BuiltInPluginIds.Translator => BoardObjectKind.Translator,
            BuiltInPluginIds.CurrencyConverter => BoardObjectKind.CurrencyConverter,
            _ => BoardObjectKind.Plugin
        };
        var width = Math.Clamp(entry.Manifest.DefaultSize.Width, 190, 1200);
        var height = Math.Clamp(entry.Manifest.DefaultSize.Height, 140, 900);
        HideCanvasContextMenu(); RegisterUndoSnapshot();
        var point = ClampObjectPosition(_pluginStoreCanvasPoint, width, height);
        var obj = NewObject(kind, point.X, point.Y, width, height,
            style: new Dictionary<string, string> { ["accent"] = entry.Manifest.AccentColor },
            content: new Dictionary<string, string>(entry.Manifest.DefaultContent ?? []));
        obj.PluginId = entry.Manifest.Id; obj.PluginVersion = entry.Manifest.Version;
        _activeWorkspace.Objects.Add(obj);
        var view = AddBoardObjectView(obj); view.PlayPopIn(); SelectBoardObject(view); Save();
        return true;
    }
}
