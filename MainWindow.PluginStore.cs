using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
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
    private string? _pluginFeedLastError;
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
            try
            {
                _remotePluginFeed = PluginDeliveryService.LoadBundledFeed();
                if (_remotePluginFeed is not null)
                    _pluginCatalogEntries = PluginDeliveryService.AddRemoteEntries(_remotePluginFeed,
                        _pluginCatalogEntries, Version.Parse(UpdateService.CurrentVersion));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                _pluginCatalogLoadError = ex.Message;
            }
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
        PluginStore.LocalPluginLoadRequested += (_, _) => LoadLocalPluginFromStore();
        PluginStore.PluginDeployRequested += (_, _) => DeployPluginFromStore();
        PluginStore.RefreshRequested += (_, _) => RefreshPluginStoreFromStore();
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
        _pluginFeedLastError = null;
        try
        {
            var delivery = new PluginDeliveryService(_pluginCatalogService);
            PluginFeed? officialFeed = null;
            try { officialFeed = await delivery.FetchFeedAsync(); }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
            {
                _pluginFeedLastError = ex.Message;
                Trace.WriteLine($"Catálogo oficial de plugins indisponível: {ex.Message}");
            }
            PluginFeed? marketplaceFeed = null;
            try { marketplaceFeed = await delivery.FetchMarketplaceFeedAsync(); }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
            {
                _pluginFeedLastError = ex.Message;
                Trace.WriteLine($"Loja de plugins indisponível: {ex.Message}");
            }
            var feed = PluginDeliveryService.MergeFeeds(officialFeed, marketplaceFeed, _remotePluginFeed);
            if (!IsLoaded) return;
            _remotePluginFeed = feed;
            ReloadPluginCatalog();
            var updated = await delivery.UpdateInstalledAsync(feed);
            if (updated.Count > 0) ReloadPluginCatalog();
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
            _lastPluginFeedCheck = DateTimeOffset.MinValue;
            _pluginFeedLastError = ex.Message;
            Trace.WriteLine($"Verificação de plugins falhou: {ex}");
            if (PluginStoreOverlay.Visibility == Visibility.Visible)
                ShowToast("Atualização da loja indisponível. Exibindo plugins conhecidos.");
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

    private async void LoadLocalPluginFromStore()
    {
        var picker = new OpenFileDialog
        {
            Title = "Carregar plugin local",
            Filter = "Pacote de plugin ClipDesk (*.zip)|*.zip",
            CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            var installed = await new PluginDeliveryService(_pluginCatalogService).InstallLocalArchiveAsync(picker.FileName);
            Views.BoardObjectView.InvalidateExternalPlugin(installed.Manifest.Id);
            ReloadPluginCatalog();
            ShowToast($"{installed.Manifest.Name} carregado");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            ShowToast($"Não foi possível carregar: {ex.Message}");
        }
    }

    private async void DeployPluginFromStore()
    {
        if (PluginPublishingService.ResolveStoreUri() is null)
        {
            ShowToast("Publicação direta indisponível. Publique o pacote no feed oficial da main.");
            return;
        }
        var username = _cloud?.User?.Username;
        if (_cloud?.Connected != true || string.IsNullOrWhiteSpace(username))
        {
            ShowToast("Entre com Google e defina seu username antes de fazer deploy.");
            CloudAccount_Click(CloudStatusButton, new RoutedEventArgs());
            return;
        }
        var picker = new OpenFileDialog
        {
            Title = "Selecionar pacote para deploy",
            Filter = "Pacote de plugin ClipDesk (*.zip)|*.zip",
            CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog(this) != true) return;
        var dialog = new Views.PluginDeploymentDialog(Path.GetFileName(picker.FileName), username) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var published = await new PluginPublishingService().PublishAsync(picker.FileName, username, dialog.DeployPassword);
            _lastPluginFeedCheck = DateTimeOffset.MinValue;
            await CheckForPluginUpdatesAsync();
            ShowToast($"{published.Name} publicado por @{published.Publisher}");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowToast($"Não foi possível publicar: {ex.Message}");
        }
    }

    private async void RefreshPluginStoreFromStore()
    {
        if (_pluginFeedCheckInProgress) return;
        PluginStore.SetRefreshInProgress(true);
        try
        {
            _lastPluginFeedCheck = DateTimeOffset.MinValue;
            await CheckForPluginUpdatesAsync();
            ReloadPluginCatalog();
            ShowToast(_pluginFeedLastError is null ? "Loja de plugins atualizada" : "Loja atualizada parcialmente; alguns catálogos estão indisponíveis");
        }
        finally { PluginStore.SetRefreshInProgress(false); }
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
                entry = await EnsurePluginInstalledAsync(entry);
                RefreshPluginObjects(entry, synchronizeMissingName: true);
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

    private async void BoardObjectPluginInstallRequested(Views.BoardObjectView view)
    {
        if (_pluginMaintenanceInProgress || string.IsNullOrWhiteSpace(view.Object.PluginId)) return;
        _pluginMaintenanceInProgress = true;
        try
        {
            var entry = await FindPluginForInstallationAsync(view.Object.PluginId);
            if (entry is null)
                throw new InvalidDataException("Este plugin não foi encontrado no catálogo atual.");
            entry = await EnsurePluginInstalledAsync(entry);
            RefreshPluginObjects(entry, synchronizeMissingName: true);
            SelectBoardObject(view);
            ShowToast($"{entry.Manifest.Name} instalado · estado compartilhado carregado");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or HttpRequestException)
        {
            ShowToast($"Não foi possível instalar: {ex.Message}");
        }
        finally { _pluginMaintenanceInProgress = false; }
    }

    private async Task<PluginCatalogEntry?> FindPluginForInstallationAsync(string pluginId)
    {
        ReloadPluginCatalog();
        var entry = _pluginCatalogEntries.FirstOrDefault(item =>
            item.Manifest.Id.Equals(pluginId, StringComparison.OrdinalIgnoreCase));
        if (entry is not null) return entry;

        var delivery = new PluginDeliveryService(_pluginCatalogService);
        PluginFeed? official = null;
        PluginFeed? marketplace = null;
        try { official = await delivery.FetchFeedAsync(); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
        { Trace.WriteLine($"Catálogo oficial de plugins indisponível: {ex.Message}"); }
        try { marketplace = await delivery.FetchMarketplaceFeedAsync(); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
        { Trace.WriteLine($"Loja de plugins indisponível: {ex.Message}"); }
        _remotePluginFeed = PluginDeliveryService.MergeFeeds(official, marketplace, _remotePluginFeed);
        _lastPluginFeedCheck = DateTimeOffset.UtcNow;
        ReloadPluginCatalog();
        return _pluginCatalogEntries.FirstOrDefault(item =>
            item.Manifest.Id.Equals(pluginId, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<PluginCatalogEntry> EnsurePluginInstalledAsync(PluginCatalogEntry entry)
    {
        if (!entry.IsInstalled)
        {
            if (entry.RemotePackage is { } remote)
                await new PluginDeliveryService(_pluginCatalogService).InstallRemoteAsync(remote);
            else _pluginCatalogService.Enable(entry.Manifest.Id);
        }
        Views.BoardObjectView.InvalidateExternalPlugin(entry.Manifest.Id);
        ReloadPluginCatalog();
        return _pluginCatalogEntries.Single(item =>
            item.Manifest.Id.Equals(entry.Manifest.Id, StringComparison.OrdinalIgnoreCase));
    }

    private void RefreshPluginObjects(PluginCatalogEntry entry, bool synchronizeMissingName)
    {
        var metadataChanged = false;
        foreach (var board in _workspaces)
        foreach (var obj in board.Objects.Where(obj =>
                     obj.PluginId?.Equals(entry.Manifest.Id, StringComparison.OrdinalIgnoreCase) == true))
        {
            if (!synchronizeMissingName || !string.IsNullOrWhiteSpace(obj.PluginName)) continue;
            obj.PluginName = entry.Manifest.Name;
            obj.UpdatedAt = DateTimeOffset.UtcNow;
            metadataChanged = true;
        }
        foreach (var pluginView in WorkspaceCanvas.Children.OfType<Views.BoardObjectView>()
                     .Where(pluginView => pluginView.Object.PluginId?.Equals(entry.Manifest.Id,
                         StringComparison.OrdinalIgnoreCase) == true))
        {
            pluginView.RefreshFromObject();
            if (metadataChanged) QueueBoardObjectRealtime(pluginView.Object);
        }
        if (metadataChanged) Save();
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
        obj.PluginId = entry.Manifest.Id; obj.PluginName = entry.Manifest.Name; obj.PluginVersion = entry.Manifest.Version;
        _activeWorkspace.Objects.Add(obj);
        var view = AddBoardObjectView(obj); view.PlayPopIn(); SelectBoardObject(view); Save();
        return true;
    }
}
