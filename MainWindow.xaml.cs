using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using ClipDesk.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClipDesk.Models;
using ClipDesk.Services;
using ClipDesk.Views;

namespace ClipDesk;

public partial class MainWindow : Window
{
    private const int WmClipboardUpdate = 0x031D;
    private const double ElementBaseScale = 0.75;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetLongPathName(string shortPath, System.Text.StringBuilder longPath, uint bufferLength);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly StorageService _storageService = new();
    private readonly ClipboardService _clipboardService = new();
    private readonly FileIconService _fileIconService = new();
    private readonly SoundService _soundService = new();
    private List<ClipboardItem> _items = [];
    private readonly List<WorkspaceBoard> _workspaces;
    private WorkspaceBoard _activeWorkspace = null!;
    private readonly StartupService _startupService = new();
    private readonly List<ItemCard> _selectedCards = [];
    private readonly ObservableCollection<ClipboardHistoryEntry> _history = [];
    private ClipboardPopupWindow? _historyPopup;
    private TrayService? _trayService;
    private double _historyWidth = 360;
    private readonly Stack<BoardSnapshot> _undoStack = [];
    private readonly Stack<BoardSnapshot> _redoStack = [];
    private ItemCard? _activeDropTarget;
    private ClipboardItem? _openFolder;
    private string? _openFolderCategoryKey;
    private ClipboardItem? _folderDragItem;
    private FrameworkElement? _folderDragElement;
    private Point _folderDragStart;
    private HwndSource? _hwndSource;
    private bool _isDarkMode;
    private bool _isRestoringSnapshot;
    private bool _isTrashHovering;
    private bool _initialLayoutDone;
    private bool _isFolderItemDragging;
    private bool _isSwitchingWorkspace;
    private bool _workspaceNameCreates;
    private WorkspaceBoard? _workspaceModeTarget;
    private bool _updateCheckStarted;
    private bool _isExitRequested;
    private bool _wasHiddenBeforeUpdatePrompt;
    private bool _isInstallingUpdate;
    private AvailableUpdate? _availableUpdate;
    private readonly bool _startHidden;
    private bool _isMarqueeSelecting;
    private Point _marqueeStart;
    private Point _canvasContextPoint;
    private int _canvasContextAnimationVersion;
    private ItemCard? _groupDragLead;
    private readonly Dictionary<ItemCard, Point> _groupDragPositions = [];
    private double _workspaceZoom = 1;
    private AppSettings _appearanceSettings = new();
    private bool _isRightPanCandidate;
    private bool _isPanningWorkspace;
    private bool _isMiddlePanningWorkspace;
    private bool _zoomAnchoredToPointer;
    private Point _zoomAnimationWorldAnchor;
    private Point _zoomAnimationScreenAnchor;
    private DateTime _zoomAnimationUntil;
    private Point _rightPanStart;
    private Point _rightPanScrollStart;
    private readonly DispatcherTimer _appearanceSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer _zoomAnchorTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };

    public MainWindow(bool startHidden = false)
    {
        InitializeComponent();
        InitializeCreativeTools();
        Icon=new System.Windows.Media.Imaging.BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory,"Resources","Brand","clipdesk.ico")));
        BrandMark.Source=new System.Windows.Media.Imaging.BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory,"Resources","Brand","clipdesk-icon.png")));
        InstalledVersionText.Text = $"{(AppEnvironment.IsDevelopment ? "DEV · " : "Versão ")}{UpdateService.CurrentVersion}";
        _startHidden = startHidden;
        _appearanceSaveTimer.Tick += (_, _) =>
        {
            _appearanceSaveTimer.Stop();
            SaveAppearanceSettings();
        };
        _zoomAnchorTimer.Tick += (_, _) => UpdateAnimatedZoomAnchor();
        _workspaces = _storageService.LoadWorkspaces();
        _activeWorkspace = _workspaces[0];
        _items = _activeWorkspace.Items;
        Loaded += MainWindow_Loaded;
        SourceInitialized += MainWindow_SourceInitialized;
        Closing += MainWindow_Closing;
        _appearanceSettings = _storageService.LoadSettings();
        _isDarkMode = _appearanceSettings.IsDarkMode;
        _magnetAlignmentEnabled = _appearanceSettings.MagnetAlignmentEnabled;
        UpdateMagnetButtonPresentation();
        _workspaceZoom = _appearanceSettings.BoardViewports.TryGetValue(_activeWorkspace.Id.ToString("N"), out var savedViewport)
            ? Math.Clamp(savedViewport.Zoom, BoardViewport.MinimumZoom, BoardViewport.MaximumZoom)
            : _appearanceSettings.ZoomScaleVersion >= 2
            ? Math.Clamp(_appearanceSettings.WorkspaceZoom, BoardViewport.MinimumZoom, BoardViewport.MaximumZoom)
            : 1;
        ZoomSlider.Value = Math.Clamp(_workspaceZoom * 100, ZoomSlider.Minimum, ZoomSlider.Maximum);
        ThemeService.Apply(_isDarkMode);
        foreach (var entry in _storageService.LoadHistory().OrderByDescending(entry => entry.CapturedAt).Take(80))
        {
            _history.Add(entry);
        }
        HistoryPane.Bind(_history);
        HistoryPane.CloseRequested += (_, _) => HideHistoryPanel();
        HistoryPane.CopyRequested += HistoryCopyRequested;
        HistoryPane.AddRequested += (_, entry) => AddHistoryToWorkspace(entry);
        ApplyTheme();
        Closed += (_, _) =>
        {
            _trayService?.Dispose();
            _historyPopup?.Close();
            HistoryPane.Detach();
            _clipboardService.Dispose();
        };
        UpdateUndoRedoButtons();
        InitializeCloud();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateResponsiveHeader();
        RenderAllItems(animate: true, isFirstVisit: true);
        EnsureWorkspaceExtent();
        _trayService = new TrayService(Dispatcher, ShowCompactHistory, ShowWorkspace, ExitApplication);
        UpdateWorkspacePresentation();
        UpdateStartupButtonState();
        if (_startHidden)
        {
            Dispatcher.BeginInvoke(() => Hide());
        }
        if (!_updateCheckStarted && !AppEnvironment.IsDevelopment)
        {
            _updateCheckStarted = true;
            _ = CheckForUpdatesAsync();
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var update = await UpdateService.CheckForUpdateAsync();
            if (update is null || !IsLoaded) return;

            _availableUpdate = update;
            _wasHiddenBeforeUpdatePrompt = !IsVisible;
            if (_wasHiddenBeforeUpdatePrompt)
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            }
            ShowUpdateOverlay(update);
        }
        catch (OperationCanceledException)
        {
            // A checagem é silenciosa quando a aplicação é encerrada.
        }
        catch (Exception)
        {
            ShowToast("Não foi possível verificar atualizações agora.");
            if (_startHidden) Hide();
        }
    }

    private void ShowUpdateOverlay(AvailableUpdate update)
    {
        AvailableUpdateVersionText.Text = update.TagName;
        InstalledUpdateVersionText.Text = $"v{UpdateService.CurrentVersion}";
        UpdateTitleText.Text = "Uma versão nova chegou";
        UpdateDescriptionText.Text = "O ClipDesk será atualizado com segurança e reiniciado automaticamente.";
        UpdateStatusText.Text = "A atualização será baixada com segurança.";
        UpdateErrorText.Visibility = Visibility.Collapsed;
        UpdateInstallButton.IsEnabled = true;
        UpdateLaterButton.IsEnabled = true;
        UpdateInstallButtonText.Text = "Atualizar agora";
        UpdateLaterButton.Content = "Agora não";
        UpdateOverlay.Visibility = Visibility.Visible;
        UpdateOverlay.Opacity = 0;
        UpdateDialogScale.ScaleX = 0.94;
        UpdateDialogScale.ScaleY = 0.94;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        UpdateOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease });
        UpdateDialogScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease });
        UpdateDialogScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease });
    }

    private void HideUpdateOverlay()
    {
        if (UpdateOverlay.Visibility != Visibility.Visible) return;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(130));
        fade.Completed += (_, _) => UpdateOverlay.Visibility = Visibility.Collapsed;
        UpdateOverlay.BeginAnimation(OpacityProperty, fade);
        if (_wasHiddenBeforeUpdatePrompt) Hide();
    }

    private async void UpdateInstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isInstallingUpdate || _availableUpdate is null) return;
        _isInstallingUpdate = true;
        UpdateInstallButton.IsEnabled = false;
        UpdateLaterButton.IsEnabled = false;
        UpdateInstallButtonText.Text = "Baixando…";
        UpdateStatusText.Text = "Baixando e preparando a nova versão…";
        UpdateErrorText.Visibility = Visibility.Collapsed;
        try
        {
            await UpdateService.DownloadAndStartAsync(_availableUpdate);
            UpdateInstallButtonText.Text = "Reiniciando…";
            UpdateStatusText.Text = "A nova versão está pronta. Reiniciando o ClipDesk…";
            await Task.Delay(180);
            ExitApplication();
        }
        catch (Exception ex)
        {
            UpdateTitleText.Text = "Não foi possível atualizar";
            UpdateStatusText.Text = "Nada foi alterado. Você pode tentar novamente.";
            UpdateErrorText.Text = ex.Message;
            UpdateErrorText.Visibility = Visibility.Visible;
            UpdateInstallButtonText.Text = "Tentar novamente";
            UpdateInstallButton.IsEnabled = true;
            UpdateLaterButton.IsEnabled = true;
            UpdateLaterButton.Content = "Fechar";
        }
        finally
        {
            _isInstallingUpdate = false;
        }
    }

    private void UpdateLaterButton_Click(object sender, RoutedEventArgs e) => HideUpdateOverlay();
    private void UpdateOverlay_MouseDown(object sender, MouseButtonEventArgs e) { if (!_isInstallingUpdate && e.OriginalSource == UpdateOverlay) HideUpdateOverlay(); }
    private void UpdateDialog_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void RenderAllItems(bool animate = false, bool isFirstVisit = false)
    {
        WorkspaceCanvas.Children.Clear();
        RenderCreativeObjects(animate, isFirstVisit);
        for (var index = 0; index < _items.Count; index++)
        {
            var card = AddCard(_items[index], playPopIn: false);
            if (animate) card.PlayWorkspaceEntrance(index, isFirstVisit);
        }
        RefreshAllCardConnectors();
    }

    private ItemCard AddCard(ClipboardItem item, bool playPopIn)
    {
        var card = new ItemCard(item, _fileIconService, _storageService);
        card.Selected += Card_Selected;
        card.CopyRequested += Card_CopyRequested;
        card.OpenRequested += Card_OpenRequested;
        card.CloudFileActionRequested += Card_CloudFileAction;
        card.FolderCategoryRequested += Card_FolderCategoryRequested;
        card.TitleEdited += Card_TitleEdited;
        card.DuplicateRequested += Card_DuplicateRequested;
        card.DeleteRequested += Card_DeleteRequested;
        card.DetailsRequested += Card_DetailsRequested;
        card.DragStarted += Card_DragStarted;
        card.DragMoved += Card_DragMoved;
        card.DragFinished += Card_DragFinished;
        card.ResizeStarted += Card_ResizeStarted;
        card.ResizeMoved += Card_ResizeMoved;
        card.ResizeFinished += Card_ResizeFinished;

        WorkspaceCanvas.Children.Add(card);
        Canvas.SetLeft(card, Math.Max(0, item.X));
        Canvas.SetTop(card, Math.Max(0, item.Y));
        Panel.SetZIndex(card, item.ZIndex);
        card.ApplyTheme(_isDarkMode);
        card.SetWorkspaceZoom(1, animate: false);
        card.IsHitTestVisible = _activeCreativeTool is CreativeTool.Select or CreativeTool.Connector;
        card.SetCloudPresentation(_cloud?.User?.Id,_activeWorkspace.SyncMode!=WorkspaceSyncMode.Local);
        if (!MatchesWorkspaceSearch(item, WorkspaceSearchBox.Text)) card.Visibility = Visibility.Collapsed;

        if (playPopIn)
        {
            card.PlayPopIn();
        }

        return card;
    }

    private void AddItems(IEnumerable<ClipboardItem> newItems, Point? dropPoint = null)
    {
        var items = newItems.ToList();
        if (items.Count == 0)
        {
            _soundService.Error();
            ShowToast("Nada compatível na área de transferência");
            return;
        }

        RegisterUndoSnapshot();
        ItemCard? lastCard = null;
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var position = dropPoint.HasValue
                ? ClampToWorkspace(new Point(
                    dropPoint.Value.X + (index % 3) * 290 - 160,
                    dropPoint.Value.Y + (index / 3) * 230 - 105))
                : FindFreePosition();
            item.X = position.X;
            item.Y = position.Y;
            item.ZIndex = NextBoardZIndex();
            _items.Add(item);
            lastCard = AddCard(item, playPopIn: true);
        }

        if (lastCard is not null)
        {
            SelectSingle(lastCard);
        }

        _soundService.Added();
        Save();
        ShowToast(items.Count == 1 ? "Item adicionado ao ClipDesk" : "Itens adicionados ao ClipDesk");
    }

    private Point ClampToWorkspace(Point point)
    {
        var width = _activeWorkspace.WorldWidth;
        var height = _activeWorkspace.WorldHeight;
        return new Point(
            Math.Max(0, Math.Min(width - 340, point.X)),
            Math.Max(0, Math.Min(height - 220, point.Y)));
    }

    private Point FindFreePosition()
    {
        var width = _activeWorkspace.WorldWidth;
        var height = _activeWorkspace.WorldHeight;
        const double cardWidth = 340;
        const double cardHeight = 220;
        var centerX = Math.Max(40, (width - cardWidth) / 2);
        var centerY = Math.Max(110, (height - cardHeight) / 2);

        for (var step = 0; step < 80; step++)
        {
            var x = centerX + (step % 5) * 370 - 370;
            var y = centerY + (step / 5) * 250 - 250;
            var candidate = new Rect(Math.Max(24, x), Math.Max(108, y), cardWidth, cardHeight);
            var collides = WorkspaceCanvas.Children
                .OfType<ItemCard>()
                .Any(card => new Rect(Canvas.GetLeft(card), Canvas.GetTop(card), card.ActualWidth, card.ActualHeight).IntersectsWith(candidate));

            if (!collides && candidate.Right < width - 24 && candidate.Bottom < height - 24)
            {
                return candidate.Location;
            }
        }

        return new Point(centerX, centerY);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && UpdateOverlay.Visibility == Visibility.Visible && !_isInstallingUpdate)
        {
            HideUpdateOverlay();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && WorkspaceNameOverlay.Visibility == Visibility.Visible)
        {
            HideWorkspaceNameOverlay();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && HistoryPane.Visibility == Visibility.Visible && FolderOverlay.Visibility != Visibility.Visible)
        {
            HideHistoryPanel();
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.H)
        {
            ShowCompactHistory();
            e.Handled = true;
            return;
        }
        // Text editors and history search own their clipboard/undo shortcuts.
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;
        if (HistoryPane.IsKeyboardFocusWithin && e.Key != Key.Escape) return;
        if (HandleCreativeShortcut(e)) return;
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V)
        {
            PasteFromClipboard();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
        {
            UndoLastChange();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y)
        {
            RedoLastChange();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
        {
            CopySelected();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.A)
        {
            SelectAllCards();
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Delete:
                DeleteSelected();
                e.Handled = true;
                break;
            case Key.F2:
                RenameSelected();
                e.Handled = true;
                break;
            case Key.Escape:
                if (FolderOverlay.Visibility == Visibility.Visible)
                {
                    HideFolderOverlay();
                    e.Handled = true;
                    break;
                }

                SetCreativeTool(CreativeTool.Select);
                ClearSelection();
                e.Handled = true;
                break;
        }
    }

    private void PasteFromClipboard()
    {
        try
        {
            var point = WorkspaceScroll.IsMouseOver
                ? ClampCreativePoint(Mouse.GetPosition(WorkspaceCanvas))
                : new Point((WorkspaceScroll.HorizontalOffset + WorkspaceScroll.ViewportWidth / 2) / Math.Max(.01, _workspaceZoom),
                    (WorkspaceScroll.VerticalOffset + WorkspaceScroll.ViewportHeight / 2) / Math.Max(.01, _workspaceZoom));
            AddItems(_clipboardService.CaptureClipboard(_storageService), point);
        }
        catch (Exception ex)
        {
            _soundService.Error();
            ShowToast($"Não foi possível ler o clipboard: {ex.Message}");
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (WorkspaceNameOverlay.Visibility == Visibility.Visible)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (e.Data.GetDataPresent(HistoryView.DragFormat))
        {
            var point = e.GetPosition(WorkspaceScroll);
            var canDrop = FolderOverlay.Visibility != Visibility.Visible && new Rect(WorkspaceScroll.RenderSize).Contains(point);
            e.Effects = canDrop ? DragDropEffects.Copy : DragDropEffects.None;
            HistoryDropGlowText.Text = "Solte para adicionar à mesa";
            SetHistoryDropGlow(canDrop);
            e.Handled = true;
            return;
        }
        if (e.Data.GetDataPresent(FolderWindow.FolderItemDragFormat))
        {
            e.Effects = DragDropEffects.Move;
        }
        else
        {
            var canDrop = HasFileDrop(e) || TryGetDroppedLink(e) is not null;
            e.Effects = canDrop ? DragDropEffects.Copy : DragDropEffects.None;
            HistoryDropGlowText.Text = "Solte para criar um atalho na mesa";
            SetHistoryDropGlow(canDrop);
        }

        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        SetHistoryDropGlow(false);
        if (e.Data.GetDataPresent(HistoryView.DragFormat))
        {
            SetHistoryDropGlow(false);
            var point = e.GetPosition(WorkspaceScroll);
            if (FolderOverlay.Visibility != Visibility.Visible && new Rect(WorkspaceScroll.RenderSize).Contains(point)
                && e.Data.GetData(HistoryView.DragFormat) is ClipboardHistoryEntry entry)
                AddHistoryToWorkspace(entry, e.GetPosition(WorkspaceCanvas));
            e.Handled = true;
            return;
        }
        if (e.Data.GetDataPresent(FolderWindow.FolderItemDragFormat)
            && e.Data.GetData(FolderWindow.FolderItemDragFormat) is FolderDragData dragData)
        {
            MoveFolderItemToDesktop(dragData, e.GetPosition(WorkspaceCanvas));
            e.Handled = true;
            return;
        }

        var paths = GetDroppedPaths(e);
        if (paths.Length == 0)
        {
            if (TryGetDroppedLink(e) is { } url)
            {
                AddItems([new ClipboardItem { Type = ClipboardItemType.Link, DisplayName = url.Host, Url = url.AbsoluteUri }], e.GetPosition(WorkspaceCanvas));
                e.Handled = true;
            }
            return;
        }

        AddItems(_clipboardService.CreateFileItems(paths), e.GetPosition(WorkspaceCanvas));
        e.Handled = true;
    }

    private void FolderOverlay_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(FolderWindow.FolderItemDragFormat))
        {
            e.Effects = IsPointInsideFolderPanel(e.GetPosition(Root))
                ? DragDropEffects.None
                : DragDropEffects.Move;
        }
        else
        {
            e.Effects = HasFileDrop(e)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void FolderOverlay_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(FolderWindow.FolderItemDragFormat)
            && e.Data.GetData(FolderWindow.FolderItemDragFormat) is FolderDragData dragData)
        {
            if (!IsPointInsideFolderPanel(e.GetPosition(Root)))
            {
                MoveFolderItemToDesktop(dragData, e.GetPosition(WorkspaceCanvas));
                HideFolderOverlay();
            }

            e.Handled = true;
            return;
        }

        var paths = GetDroppedPaths(e);
        if (paths.Length > 0)
        {
            AddItems(_clipboardService.CreateFileItems(paths), e.GetPosition(WorkspaceCanvas));
            HideFolderOverlay();
            e.Handled = true;
        }
    }

    private static bool HasFileDrop(DragEventArgs e)
    {
        return e.Data.GetDataPresent(DataFormats.FileDrop, autoConvert: true)
            || e.Data.GetDataPresent("FileNameW", autoConvert: true)
            || e.Data.GetDataPresent("FileName", autoConvert: true);
    }

    private static string[] GetDroppedPaths(DragEventArgs e)
    {
        var candidates = new List<string>();
        foreach (var format in new[] { DataFormats.FileDrop, "FileNameW", "FileName" })
        {
            if (!e.Data.GetDataPresent(format, autoConvert: true)) continue;
            var data = e.Data.GetData(format, autoConvert: true);
            if (data is string[] paths) candidates.AddRange(paths);
            else if (data is System.Collections.Specialized.StringCollection collection) candidates.AddRange(collection.Cast<string>());
            else if (data is string path) candidates.Add(path);
        }

        if (e.Data.GetDataPresent(DataFormats.UnicodeText, autoConvert: true)
            && e.Data.GetData(DataFormats.UnicodeText, autoConvert: true) is string text)
        {
            foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Trim();
                if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.IsFile) candidate = uri.LocalPath;
                if (File.Exists(candidate) || Directory.Exists(candidate)) candidates.Add(candidate);
            }
        }

        return candidates
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Select(CanonicalDropPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string CanonicalDropPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var buffer = new System.Text.StringBuilder(32768);
        var length = GetLongPathName(fullPath, buffer, (uint)buffer.Capacity);
        return length > 0 && length < buffer.Capacity ? buffer.ToString() : fullPath;
    }

    private static Uri? TryGetDroppedLink(DragEventArgs e)
    {
        foreach (var format in new[] { DataFormats.UnicodeText, DataFormats.Text })
        {
            if (!e.Data.GetDataPresent(format, autoConvert: true)) continue;
            if (e.Data.GetData(format, autoConvert: true) is string value
                && Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https") return uri;
        }
        return null;
    }

    private void Window_DragLeave(object sender, DragEventArgs e) => SetHistoryDropGlow(false);

    private void MoveFolderItemToDesktop(FolderDragData dragData, Point dropPoint)
    {
        var parent = FindItemById(_items, dragData.ParentFolderId);
        var child = parent?.Children.FirstOrDefault(item => item.Id == dragData.ItemId);
        if (parent is null || child is null)
        {
            _soundService.Error();
            return;
        }

        RegisterUndoSnapshot();
        parent.Children.Remove(child);
        child.X = Math.Max(0, dropPoint.X - 66);
        child.Y = Math.Max(0, dropPoint.Y - 74);
        _items.Add(child);
        var card = AddCard(child, playPopIn: true);
        SelectSingle(card);

        RefreshCard(parent);
        Save();
        _soundService.FolderChanged();
        ShowToast("Item movido para a área principal");
    }

    private void Card_Selected(object? sender, EventArgs e)
    {
        if (sender is ItemCard card)
        {
            if (card.IsSelectionToggleRequested)
            {
                ToggleSelection(card);
            }
            else if (!_selectedCards.Contains(card))
            {
                SelectSingle(card);
            }
        }
    }

    private void Card_CopyRequested(object? sender, EventArgs e)
    {
        if (sender is not ItemCard card)
        {
            return;
        }

        try
        {
            _clipboardService.CopyToClipboard(card.Item, _storageService);
            card.PulseCopied();
            SelectSingle(card);
            _soundService.Copied();
            ShowToast("Copiado para a área de transferência");
        }
        catch (Exception ex)
        {
            _soundService.Error();
            ShowToast($"Falha ao copiar: {ex.Message}");
        }
    }

    private void Card_OpenRequested(object? sender, EventArgs e)
    {
        if (sender is ItemCard card)
        {
            OpenItem(card.Item, card);
        }
    }

    private void Card_FolderCategoryRequested(object? sender, string categoryKey)
    {
        if (sender is ItemCard card && card.Item.Type == ClipboardItemType.AppFolder)
        {
            ShowFolderOverlay(card.Item, categoryKey);
        }
    }

    private void OpenItem(ClipboardItem item, ItemCard? ownerCard)
    {
        if(TryOpenCloudFile(item)) return;
        if (item.Type == ClipboardItemType.AppFolder)
        {
            ShowFolderOverlay(item);
            return;
        }

        if (item.Type is ClipboardItemType.File or ClipboardItemType.Folder or ClipboardItemType.Link)
        {
            if (!PreviewWindow.TryOpenExternally(item))
            {
                _soundService.Error();
                ShowToast("Não foi possível abrir este item");
            }

            return;
        }

        var preview = new PreviewWindow(item, _storageService, _isDarkMode)
        {
            Owner = this
        };
        preview.ShowDialog();

        if (preview.WasEdited)
        {
            ownerCard?.Refresh(_storageService);
            RenderOpenFolderItems();
            Save();
        }
    }

    private void ShowFolderOverlay(ClipboardItem folder, string? categoryKey = null)
    {
        _openFolder = folder;
        _openFolderCategoryKey = categoryKey;
        UpdateFolderOverlayTitle();
        FolderOverlayTitle.Visibility = Visibility.Visible;
        FolderOverlayTitleEditor.Visibility = Visibility.Collapsed;
        FolderBackButton.Visibility = categoryKey is null ? Visibility.Collapsed : Visibility.Visible;
        RenderOpenFolderItems();
        FolderOverlay.Visibility = Visibility.Visible;
        FolderOverlay.Opacity = 0;
        FolderPanelScale.ScaleX = 0.86;
        FolderPanelScale.ScaleY = 0.86;

        var fade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(170))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var pop = new DoubleAnimation(1, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.18 }
        };

        FolderOverlay.BeginAnimation(OpacityProperty, fade);
        FolderPanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        FolderPanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    private void HideFolderOverlay()
    {
        if (FolderOverlay.Visibility != Visibility.Visible)
        {
            return;
        }

        _openFolder = null;
        _openFolderCategoryKey = null;
        _folderDragItem = null;
        _folderDragElement = null;
        _isFolderItemDragging = false;
        CancelFolderTitleEdit();
        FolderOverlay.BeginAnimation(OpacityProperty, null);
        FolderOverlay.Opacity = 0;
        FolderOverlay.Visibility = Visibility.Collapsed;
    }

    private void RenderOpenFolderItems()
    {
        FolderItemsHost.Children.Clear();
        if (_openFolder is null)
        {
            return;
        }

        if (_openFolder.Children.Count == 0)
        {
            FolderItemsHost.Children.Add(new TextBlock
            {
                Text = "Esta pasta está vazia. Arraste itens da mesa ou do histórico para cá.",
                Margin = new Thickness(12, 18, 12, 0),
                Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#94A3B8" : "#64748B")!,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center
            });
            return;
        }

        if (_openFolderCategoryKey is null)
        {
            var categories = FolderCategoryService.GetCategories(_openFolder.Children);
            var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var category in categories)
            {
                wrap.Children.Add(CreateFolderCategoryItem(category));
            }
            FolderItemsHost.Children.Add(wrap);
        }
        else
        {
            var category = FolderCategoryService.GetCategory(_openFolder.Children, _openFolderCategoryKey);
            if (category is null)
            {
                _openFolderCategoryKey = null;
                RenderOpenFolderItems();
                return;
            }

            foreach (var child in category.Items)
            {
                FolderItemsHost.Children.Add(CreateFolderOverlayItem(child, listMode: true));
            }
        }
    }

    private UIElement CreateFolderCategoryItem(FolderCategory category)
    {
        var accent = (SolidColorBrush)new BrushConverter().ConvertFromString(category.Accent)!;
        var shell = new Border
        {
            Width = 170,
            Height = 132,
            Margin = new Thickness(5),
            Padding = new Thickness(15),
            Background = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#A817202D" : "#EFFFFFFF")!,
            BorderBrush = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#3B64748B" : "#D7DFEA")!,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        var scale = new ScaleTransform();
        shell.RenderTransform = scale;
        shell.MouseEnter += (_, _) =>
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.025, TimeSpan.FromMilliseconds(130)));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.025, TimeSpan.FromMilliseconds(130)));
        };
        shell.MouseLeave += (_, _) =>
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(170)));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(170)));
        };
        shell.MouseLeftButtonDown += (_, eventArgs) =>
        {
            ShowFolderCategory(category.Key);
            eventArgs.Handled = true;
        };

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(new Border
        {
            Width = 42,
            Height = 42,
            Background = new LinearGradientBrush(Color.FromRgb(196, 181, 253), accent.Color, 45),
            CornerRadius = new CornerRadius(12),
            Child = new TextBlock
            {
                Text = category.Glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 20,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        var title = new TextBlock
        {
            Text = category.Name,
            Margin = new Thickness(0, 10, 0, 0),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#F8FAFC" : "#1F2937")!
        };
        Grid.SetRow(title, 1);
        content.Children.Add(title);
        var count = new TextBlock
        {
            Text = $"{category.Items.Count} {(category.Items.Count == 1 ? "item" : "itens")}",
            VerticalAlignment = VerticalAlignment.Bottom,
            FontSize = 12,
            Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#94A3B8" : "#64748B")!
        };
        Grid.SetRow(count, 2);
        content.Children.Add(count);
        shell.Child = content;
        return shell;
    }

    private UIElement CreateFolderOverlayItem(ClipboardItem item, bool listMode)
    {
        var visual = _fileIconService.GetVisual(item);
        var accent = (SolidColorBrush)new BrushConverter().ConvertFromString(visual.Accent)!;
        var grid = new Grid
        {
            Width = listMode ? double.NaN : 150,
            Height = listMode ? 66 : 150,
            Margin = listMode ? new Thickness(0, 0, 0, 7) : new Thickness(3),
            Background = Brushes.Transparent,
            Tag = item,
            Cursor = Cursors.Hand,
            RenderTransform = new TranslateTransform()
        };

        if (listMode)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var listSurface = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#A817202D" : "#EFFFFFFF")!,
                BorderBrush = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#3B64748B" : "#D7DFEA")!,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12)
            };
            Grid.SetColumnSpan(listSurface, 3);
            grid.Children.Add(listSurface);
        }

        var icon = new Border
        {
            Width = listMode ? 40 : 96,
            Height = listMode ? 40 : 96,
            Margin = listMode ? new Thickness(10, 0, 8, 0) : new Thickness(0, 0, 0, 44),
            HorizontalAlignment = listMode ? HorizontalAlignment.Left : HorizontalAlignment.Center,
            VerticalAlignment = listMode ? VerticalAlignment.Center : VerticalAlignment.Top,
            Background = accent,
            CornerRadius = new CornerRadius(listMode ? 10 : 24),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = listMode ? 10 : 18,
                ShadowDepth = listMode ? 3 : 5,
                Opacity = 0.2,
                Color = Color.FromRgb(83, 97, 122)
            }
        };

        if (item.Type == ClipboardItemType.Image && _storageService.LoadBitmap(item.StoredFilePath) is { } bitmap)
        {
            icon.Background = new ImageBrush(bitmap)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };
        }
        else
        {
            icon.Child = new TextBlock
            {
                Text = visual.Glyph,
                FontFamily = visual.UseSymbolFont ? new FontFamily("Segoe MDL2 Assets") : new FontFamily("Segoe UI"),
                FontSize = listMode ? (visual.UseSymbolFont ? 18 : 13) : (visual.UseSymbolFont ? 42 : 34),
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
        }

        var name = new TextBlock
        {
            Text = item.DisplayName,
            Margin = listMode ? new Thickness(0, 0, 12, 0) : new Thickness(4, 104, 4, 0),
            TextAlignment = listMode ? TextAlignment.Left : TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = listMode ? 14 : 15,
            FontWeight = FontWeights.Medium,
            Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#F8FAFC" : "#1F2937")!,
            VerticalAlignment = listMode ? VerticalAlignment.Center : VerticalAlignment.Top,
            MaxHeight = 44
        };

        if (listMode)
        {
            Grid.SetColumn(name, 1);
            var type = new TextBlock
            {
                Text = visual.Label,
                Margin = new Thickness(10, 0, 16, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#94A3B8" : "#64748B")!,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetColumn(type, 2);
            grid.Children.Add(type);
        }

        grid.Children.Add(icon);
        grid.Children.Add(name);
        grid.MouseLeftButtonDown += FolderItem_MouseLeftButtonDown;
        grid.MouseMove += FolderItem_MouseMove;
        grid.MouseLeftButtonUp += FolderItem_MouseLeftButtonUp;
        return grid;
    }

    private void ShowFolderCategory(string categoryKey)
    {
        if (_openFolder is null) return;
        _openFolderCategoryKey = categoryKey;
        FolderBackButton.Visibility = Visibility.Visible;
        UpdateFolderOverlayTitle();
        RenderOpenFolderItems();
        FolderItemsHost.Opacity = 0;
        FolderItemsHost.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(170))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void FolderBackButton_Click(object sender, RoutedEventArgs e)
    {
        _openFolderCategoryKey = null;
        FolderBackButton.Visibility = Visibility.Collapsed;
        UpdateFolderOverlayTitle();
        RenderOpenFolderItems();
        e.Handled = true;
    }

    private void UpdateFolderOverlayTitle()
    {
        if (_openFolder is null) return;
        var category = _openFolderCategoryKey is null ? null : FolderCategoryService.GetCategory(_openFolder.Children, _openFolderCategoryKey);
        FolderOverlayTitle.Text = category is null ? _openFolder.DisplayName : $"{_openFolder.DisplayName} · {category.Name}";
    }

    private void FolderItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not ClipboardItem item)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            OpenItem(item, null);
            e.Handled = true;
            return;
        }

        _folderDragStart = e.GetPosition(FolderOverlay);
        _folderDragItem = item;
        _folderDragElement = element;
        _isFolderItemDragging = false;
        element.CaptureMouse();
        e.Handled = true;
    }

    private void FolderItem_MouseMove(object sender, MouseEventArgs e)
    {
        UpdateFolderItemDrag(e);
    }

    private void FolderOverlay_MouseMove(object sender, MouseEventArgs e)
    {
        UpdateFolderItemDrag(e);
    }

    private void UpdateFolderItemDrag(MouseEventArgs e)
    {
        if (_openFolder is null || _folderDragItem is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(FolderOverlay);
        if (Math.Abs(current.X - _folderDragStart.X) + Math.Abs(current.Y - _folderDragStart.Y) < 10)
        {
            return;
        }

        _isFolderItemDragging = true;
        if (_folderDragElement?.RenderTransform is TranslateTransform transform)
        {
            transform.X = current.X - _folderDragStart.X;
            transform.Y = current.Y - _folderDragStart.Y;
            Panel.SetZIndex(_folderDragElement, 40);
            _folderDragElement.Opacity = IsPointInsideFolderPanel(current) ? 0.92 : 0.72;
        }
    }

    private void FolderItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        CompleteFolderItemDrag(e);
    }

    private void FolderOverlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        CompleteFolderItemDrag(e);
    }

    private void CompleteFolderItemDrag(MouseButtonEventArgs e)
    {
        if (_openFolder is null || _folderDragItem is null)
        {
            return;
        }

        var item = _folderDragItem;
        var parent = _openFolder;
        var element = _folderDragElement;
        var current = e.GetPosition(FolderOverlay);
        element?.ReleaseMouseCapture();

        if (_isFolderItemDragging && !IsPointInsideFolderPanel(current))
        {
            MoveFolderItemToDesktop(new FolderDragData(parent.Id, item.Id), e.GetPosition(WorkspaceCanvas));
            HideFolderOverlay();
            e.Handled = true;
            return;
        }

        ResetFolderDragElement(element, animated: _isFolderItemDragging);
        _folderDragItem = null;
        _folderDragElement = null;
        _isFolderItemDragging = false;
        e.Handled = true;
    }

    private void ResetFolderDragElement(FrameworkElement? element, bool animated)
    {
        if (element is null)
        {
            return;
        }

        element.Opacity = 1;
        Panel.SetZIndex(element, 0);
        if (element.RenderTransform is not TranslateTransform transform)
        {
            return;
        }

        if (!animated)
        {
            transform.X = 0;
            transform.Y = 0;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(160);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, duration) { EasingFunction = ease });
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, duration) { EasingFunction = ease });
    }

    private void Card_TitleEdited(object? sender, string newTitle)
    {
        if (sender is not ItemCard card)
        {
            return;
        }

        RegisterUndoSnapshot();
        card.Item.DisplayName = newTitle;
        card.Refresh(_storageService);
        card.ApplyTheme(_isDarkMode);
        Save();
        ShowToast("Título atualizado");
    }

    private void Card_DuplicateRequested(object? sender, EventArgs e)
    {
        if (sender is not ItemCard card)
        {
            return;
        }

        RegisterUndoSnapshot();
        var duplicate = _clipboardService.Duplicate(card.Item, _storageService);
        duplicate.ZIndex = NextBoardZIndex();
        _items.Add(duplicate);
        var duplicateCard = AddCard(duplicate, playPopIn: true);
        SelectSingle(duplicateCard);
        _soundService.Added();
        Save();
        ShowToast("Item duplicado");
    }

    private void Card_DeleteRequested(object? sender, EventArgs e)
    {
        if (sender is ItemCard card)
        {
            DeleteCards([card]);
        }
    }

    private void Card_DetailsRequested(object? sender, EventArgs e)
    {
        if (sender is not ItemCard card)
        {
            return;
        }

        var item = card.Item;
        var origin = item.Type == ClipboardItemType.AppFolder
            ? $"{item.Children.Count} itens nesta pasta"
            : item.StoredFilePath ?? item.Url ?? item.Text ?? string.Join(", ", item.FilePaths);
        var details =
            $"Nome: {item.DisplayName}{Environment.NewLine}" +
            $"Tipo: {item.Type}{Environment.NewLine}" +
            $"Criado em: {item.CreatedAt:G}{Environment.NewLine}" +
            $"Posição: {item.X:0}, {item.Y:0}{Environment.NewLine}" +
            $"Origem: {origin}";

        MessageBox.Show(this, details, "Detalhes", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Card_DragStarted(object? sender, EventArgs e)
    {
        if (sender is ItemCard card)
        {
            ExpandConnectedDragSelection(card);
            RegisterUndoSnapshot();
            _groupDragLead = card;
            _groupDragPositions.Clear();
            foreach (var selected in _selectedCards)
            {
                _groupDragPositions[selected] = new Point(Canvas.GetLeft(selected), Canvas.GetTop(selected));
                selected.Item.ZIndex = NextBoardZIndex();
                Panel.SetZIndex(selected, selected.Item.ZIndex);
            }
            PrepareAlignmentTargets(_groupDragPositions.Keys.Concat(_linkedCardDragOrigins.Keys), _linkedObjectDragOrigins.Keys);
        }
    }

    private void Card_DragMoved(object? sender, EventArgs e)
    {
        if (sender is not ItemCard card)
        {
            return;
        }

        ApplyMagnetToCard(card);
        MoveSelectedGroup(card);
        MoveLinkedNodes(CardNodeId(card.Item.Id), new Point(Canvas.GetLeft(card), Canvas.GetTop(card)));
        RefreshConnectorsForNodes(ConnectedComponent(CardNodeId(card.Item.Id)));
        _presenceInputDirty=true;
        var isGroupDrag = _groupDragPositions.Count > 1;
        var target = isGroupDrag ? null : FindFolderTarget(card);
        SetTrashHover(IsOverTrash(card));
        if (_activeDropTarget == target)
        {
            return;
        }

        _activeDropTarget?.SetDropTarget(false);
        _activeDropTarget = target;
        _activeDropTarget?.SetDropTarget(true);
    }

    private void Card_DragFinished(object? sender, EventArgs e)
    {
        if (sender is not ItemCard card)
        {
            return;
        }

        FinishAlignmentDrag();

        foreach (var selected in _groupDragPositions.Keys)
        {
            Panel.SetZIndex(selected, selected.Item.ZIndex);
        }
        _activeDropTarget?.SetDropTarget(false);
        SetTrashHover(false);

        if (IsOverTrash(card))
        {
            _activeDropTarget = null;
            DeleteCards(_groupDragPositions.Count > 1 ? _groupDragPositions.Keys : [card]);
            _groupDragPositions.Clear();
            _groupDragLead = null;
            ClearLinkedDrag();
            return;
        }

        var target = _activeDropTarget ?? FindFolderTarget(card);
        _activeDropTarget = null;

        if (target is not null && _groupDragPositions.Count <= 1)
        {
            PlaceCardIntoFolder(card, target);
            _groupDragPositions.Clear();
            _groupDragLead = null;
            ClearLinkedDrag();
            return;
        }

        _groupDragPositions.Clear();
        _groupDragLead = null;
        ClearLinkedDrag();
        Save();
    }

    private void Card_ResizeStarted(object? sender, EventArgs e)
    {
        if (sender is not ItemCard card)
        {
            return;
        }

        RegisterUndoSnapshot();
        card.Item.ZIndex = NextBoardZIndex();
        Panel.SetZIndex(card, card.Item.ZIndex);
    }

    private void Card_ResizeMoved(object? sender, EventArgs e)
    {
        if (sender is ItemCard card)
        {
            card.Item.Width = card.Width;
            card.Item.Height = card.Height;
            RefreshConnectorsForCards([card.Item.Id]);
            EnsureWorkspaceExtent();
        }
    }

    private void Card_ResizeFinished(object? sender, EventArgs e)
    {
        if (sender is ItemCard card)
        {
            Panel.SetZIndex(card, card.Item.ZIndex);
            Save();
            ShowToast("Tamanho da prévia atualizado");
        }
    }

    private ItemCard? FindFolderTarget(ItemCard draggedCard)
    {
        var draggedBounds = draggedCard.GetVisualBounds(WorkspaceCanvas);
        if (draggedBounds.IsEmpty) return null;
        var center = new Point(draggedBounds.Left + draggedBounds.Width / 2, draggedBounds.Top + draggedBounds.Height / 2);

        return WorkspaceCanvas.Children
            .OfType<ItemCard>()
            .Where(card => card != draggedCard)
            .Reverse()
            .FirstOrDefault(card =>
            {
                var rect = card.GetVisualBounds(WorkspaceCanvas);
                return rect.Contains(center);
            });
    }

    private void PlaceCardIntoFolder(ItemCard draggedCard, ItemCard targetCard)
    {
        if (targetCard.Item.Type == ClipboardItemType.AppFolder)
        {
            AddCardToExistingFolder(draggedCard, targetCard);
            return;
        }

        CreateFolderFromCards(draggedCard, targetCard);
    }

    private void AddCardToExistingFolder(ItemCard draggedCard, ItemCard folderCard)
    {
        RegisterUndoSnapshot();
        RemoveCardsFromConnectors([draggedCard.Item.Id]);
        _items.Remove(draggedCard.Item);
        folderCard.Item.Children.Add(draggedCard.Item);
        _selectedCards.Remove(draggedCard);
        WorkspaceCanvas.Children.Remove(draggedCard);
        folderCard.Refresh(_storageService);
        folderCard.PlayPopIn();
        SelectSingle(folderCard);
        Save();
        _soundService.FolderChanged();
        ShowToast("Item adicionado à pasta");
    }

    private void CreateFolderFromCards(ItemCard draggedCard, ItemCard targetCard)
    {
        RegisterUndoSnapshot();
        RemoveCardsFromConnectors([draggedCard.Item.Id, targetCard.Item.Id]);
        var folder = new ClipboardItem
        {
            Type = ClipboardItemType.AppFolder,
            DisplayName = NextFolderName(),
            X = Canvas.GetLeft(targetCard),
            Y = Canvas.GetTop(targetCard),
            ZIndex = NextBoardZIndex(),
            Children = [targetCard.Item, draggedCard.Item]
        };

        _items.Remove(targetCard.Item);
        _items.Remove(draggedCard.Item);
        _selectedCards.Remove(targetCard);
        _selectedCards.Remove(draggedCard);
        WorkspaceCanvas.Children.Remove(targetCard);
        WorkspaceCanvas.Children.Remove(draggedCard);

        _items.Add(folder);
        var folderCard = AddCard(folder, playPopIn: true);
        SelectSingle(folderCard);
        Save();
        _soundService.FolderChanged();
        ShowToast("Pasta criada");
    }

    private string NextFolderName()
    {
        var number = _items.Count(item => item.Type == ClipboardItemType.AppFolder) + 1;
        return number == 1 ? "Pasta" : $"Pasta {number}";
    }

    private bool IsOverTrash(ItemCard card)
    {
        var cardBounds = card.GetVisualBounds(Root);
        if (cardBounds.IsEmpty) return false;
        var trashTopLeft = TrashZone.TranslatePoint(new Point(0, 0), Root);
        var trashRect = new Rect(trashTopLeft, new Size(TrashZone.ActualWidth, TrashZone.ActualHeight));
        return trashRect.IntersectsWith(cardBounds);
    }

    private void SetTrashHover(bool active)
    {
        if (_isTrashHovering == active)
        {
            return;
        }

        _isTrashHovering = active;
        var scale = active ? 1.08 : 1;
        var duration = TimeSpan.FromMilliseconds(active ? 130 : 220);
        IEasingFunction ease = active
            ? new CubicEase { EasingMode = EasingMode.EaseOut }
            : new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.16 };

        TrashScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, duration) { EasingFunction = ease });
        TrashScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, duration) { EasingFunction = ease });
        ApplyTrashTheme(active);
    }

    private void ApplyTrashTheme(bool active)
    {
        if (active)
        {
            TrashZone.Background = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#5533414F" : "#66E8EEF6")!;
            TrashZone.BorderBrush = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#94A3B8" : "#64748B")!;
            TrashOutline.Stroke = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#B7A8FF" : "#6D5BD0")!;
            TrashIcon.Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#F8FAFC" : "#475569")!;
            TrashLabel.Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#F8FAFC" : "#1F2937")!;
            TrashLabel.Visibility = Visibility.Visible;
            return;
        }

        TrashZone.Background = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#331E2531" : "#33FFFFFF")!;
        TrashZone.BorderBrush = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#66748694" : "#CBD5E1")!;
        TrashOutline.Stroke = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#66748694" : "#CBD5E1")!;
        TrashIcon.Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#CBD5E1" : "#94A3B8")!;
        TrashLabel.Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#F8FAFC" : "#475569")!;
        TrashLabel.Visibility = Visibility.Collapsed;
    }

    private void SelectSingle(ItemCard card)
    {
        ClearSelection();
        card.SetSelected(true);
        _selectedCards.Add(card);
    }

    private void ToggleSelection(ItemCard card)
    {
        if (_selectedCards.Remove(card))
        {
            card.SetSelected(false);
            return;
        }

        _selectedCards.Add(card);
        card.SetSelected(true);
    }

    private void SelectAllCards()
    {
        ClearSelection();
        foreach (var card in WorkspaceCanvas.Children.OfType<ItemCard>())
        {
            card.SetSelected(true);
            _selectedCards.Add(card);
        }
    }

    private void ClearSelection()
    {
        foreach (var card in _selectedCards)
        {
            card.SetSelected(false);
        }

        _selectedCards.Clear();
        ClearBoardObjectSelection();
    }

    private void CopySelected()
    {
        var card = _selectedCards.LastOrDefault();
        if (card is not null)
        {
            Card_CopyRequested(card, EventArgs.Empty);
        }
    }

    private void DeleteSelected()
    {
        if (DeleteSelectedBoardObject()) return;
        if (_selectedCards.Count > 0)
        {
            DeleteCards([.. _selectedCards]);
        }
    }

    private void RenameSelected()
    {
        var card = _selectedCards.LastOrDefault();
        if (card is not null)
        {
            RenameCard(card);
        }
    }

    private void RenameCard(ItemCard card)
    {
        var dialog = new RenameWindow(card.Item.DisplayName)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            RegisterUndoSnapshot();
            card.Item.DisplayName = dialog.ItemName;
            card.Refresh(_storageService);
            Save();
            ShowToast("Item renomeado");
        }
    }

    private void DeleteCards(IEnumerable<ItemCard> cards)
    {
        var cardList = cards.ToList();
        if (cardList.Count == 0)
        {
            return;
        }

        RegisterUndoSnapshot();
        RemoveCardsFromConnectors(cardList.Select(card => card.Item.Id));
        foreach (var card in cardList)
        {
            _items.Remove(card.Item);
            _selectedCards.Remove(card);
            card.PlayDismiss(() => WorkspaceCanvas.Children.Remove(card));
        }

        Save();
        _soundService.Deleted();
        ShowToast(cardList.Count == 1 ? "Item excluído" : "Itens excluídos");
    }

    private void ResetWorkspace()
    {
        var response = MessageBox.Show(
            this,
            "Apagar todos os itens da área de trabalho?",
            "Resetar ClipDesk",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (response != MessageBoxResult.Yes)
        {
            return;
        }

        RegisterUndoSnapshot();
        _items.Clear();
        _activeWorkspace.Objects.Clear();
        _selectedCards.Clear();
        WorkspaceCanvas.Children.Clear();
        Save();
        _soundService.Deleted();
        ShowToast("Area de trabalho resetada");
    }

    private void Root_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (WorkspaceDropdown.Visibility == Visibility.Visible
            && !IsInsideElement(e.OriginalSource as DependencyObject, WorkspaceDropdown)
            && !IsInsideElement(e.OriginalSource as DependencyObject, WorkspaceButton)
            && !IsInsideElement(e.OriginalSource as DependencyObject, CloudStatusButton))
        {
            HideWorkspaceDropdown();
        }
        if (e.OriginalSource == Root || e.OriginalSource == WorkspaceCanvas || e.OriginalSource == WorkspaceExtentHost)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (HandleCreativeMouseDown(e)) { e.Handled = true; return; }
                HideCanvasContextMenu();
                HideBoardObjectContextMenu();
                BeginMarqueeSelection(e.GetPosition(WorkspaceCanvas));
            }
        }
    }

    private static bool IsInsideElement(DependencyObject? source, DependencyObject ancestor)
    {
        for(var current=source;current is not null;)
        {
            if(ReferenceEquals(current,ancestor)) return true;
            current=current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        return false;
    }

    private void Root_MouseMove(object sender, MouseEventArgs e)
    {
        if (HandleCreativeMouseMove(e)) { e.Handled = true; return; }
        if (_isRightPanCandidate || _isMiddlePanningWorkspace)
        {
            if ((_isRightPanCandidate && e.RightButton != MouseButtonState.Pressed) || (_isMiddlePanningWorkspace && e.MiddleButton != MouseButtonState.Pressed))
            {
                _isRightPanCandidate = false;
                _isPanningWorkspace = false;
                _isMiddlePanningWorkspace = false;
            }
            else
            {
                var current = e.GetPosition(Root);
                var delta = current - _rightPanStart;
                if (!_isPanningWorkspace && (Math.Abs(delta.X) > 4 || Math.Abs(delta.Y) > 4))
                {
                    _isPanningWorkspace = true;
                    HideCanvasContextMenu();
                    Root.CaptureMouse();
                    Cursor = Cursors.SizeAll;
                }
                if (_isPanningWorkspace)
                {
                    WorkspaceScroll.ScrollToHorizontalOffset(Math.Clamp(_rightPanScrollStart.X - delta.X, 0, WorkspaceScroll.ScrollableWidth));
                    WorkspaceScroll.ScrollToVerticalOffset(Math.Clamp(_rightPanScrollStart.Y - delta.Y, 0, WorkspaceScroll.ScrollableHeight));
                    return;
                }
            }
        }
        if (!_isMarqueeSelecting || e.LeftButton != MouseButtonState.Pressed) return;
        UpdateMarqueeSelection(e.GetPosition(WorkspaceCanvas));
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (HandleCreativeMouseUp(e)) { e.Handled = true; return; }
        if (!_isMarqueeSelecting) return;
        EndMarqueeSelection();
        e.Handled = true;
    }

    private void Root_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource != Root && e.OriginalSource != WorkspaceCanvas) return;
        _isRightPanCandidate = true;
        _isPanningWorkspace = false;
        _rightPanStart = e.GetPosition(Root);
        _rightPanScrollStart = new Point(WorkspaceScroll.HorizontalOffset, WorkspaceScroll.VerticalOffset);
        e.Handled = true;
    }

    private void Root_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var interactionSource=e.OriginalSource as DependencyObject;
        if(_followedMemberId is not null && !IsInsideElement(interactionSource,CollaboratorAvatars) && !IsInsideElement(interactionSource,CloudPanel) && !IsInsideElement(interactionSource,FollowCameraBanner)) StopFollowingCamera(true);
        else if(_presenceTravelTimer.IsEnabled && _followedMemberId is null) _presenceTravelTimer.Stop();
        if(CanvasContextMenu.Visibility==Visibility.Visible)
        {
            var source=e.OriginalSource as DependencyObject;
            if(e.ChangedButton==MouseButton.Right || e.ChangedButton==MouseButton.Left && !IsInsideElement(source,CanvasContextMenu))
            {
                _isRightPanCandidate=false;
                HideCanvasContextMenu();
                if(e.ChangedButton==MouseButton.Right) {e.Handled=true;return;}
            }
        }
        if (e.ChangedButton == MouseButton.Left && _activeCreativeTool != CreativeTool.Select && WorkspaceScroll.IsMouseOver)
        {
            if (HandleCreativeMouseDown(e)) { e.Handled = true; return; }
        }
        if (e.ChangedButton != MouseButton.Middle) return;
        _isMiddlePanningWorkspace = true;
        _isPanningWorkspace = true;
        _rightPanStart = e.GetPosition(Root);
        _rightPanScrollStart = new Point(WorkspaceScroll.HorizontalOffset, WorkspaceScroll.VerticalOffset);
        Root.CaptureMouse(); Cursor = Cursors.SizeAll; e.Handled = true;
    }

    private void Root_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && HandleCreativeMouseUp(e)) { e.Handled = true; return; }
        if (e.ChangedButton != MouseButton.Middle) return;
        if (!_isMiddlePanningWorkspace) return;
        _isMiddlePanningWorkspace = false; _isPanningWorkspace = false;
        if (Mouse.Captured == Root) Root.ReleaseMouseCapture(); Cursor = Cursors.Arrow;
        _appearanceSaveTimer.Stop(); _appearanceSaveTimer.Start(); e.Handled = true;
    }

    private void Root_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isRightPanCandidate) return;
        var wasPanning = _isPanningWorkspace;
        _isRightPanCandidate = false;
        _isPanningWorkspace = false;
        if (Mouse.Captured == Root) Root.ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
        if (!wasPanning)
        {
            _canvasContextPoint = e.GetPosition(WorkspaceCanvas);
            ShowCanvasContextMenu(e.GetPosition(Root));
        }
        else
        {
            _appearanceSaveTimer.Stop();
            _appearanceSaveTimer.Start();
        }
        e.Handled = true;
    }

    private void BeginMarqueeSelection(Point start)
    {
        HideCanvasContextMenu();
        _marqueeStart = start;
        _isMarqueeSelecting = true;
        ClearSelection();
        SelectionMarquee.Margin = new Thickness(start.X, start.Y, 0, 0);
        SelectionMarquee.Width = 0;
        SelectionMarquee.Height = 0;
        SelectionMarquee.Visibility = Visibility.Visible;
        Root.CaptureMouse();
    }

    private void UpdateMarqueeSelection(Point current)
    {
        var left = Math.Min(_marqueeStart.X, current.X);
        var top = Math.Min(_marqueeStart.Y, current.Y);
        var rectangle = new Rect(new Point(left, top), new Point(Math.Max(_marqueeStart.X, current.X), Math.Max(_marqueeStart.Y, current.Y)));
        SelectionMarquee.Margin = new Thickness(left, top, 0, 0);
        SelectionMarquee.Width = rectangle.Width;
        SelectionMarquee.Height = rectangle.Height;

        foreach (var card in WorkspaceCanvas.Children.OfType<ItemCard>())
        {
            var selected = rectangle.IntersectsWith(card.GetVisualBounds(WorkspaceCanvas));
            if (selected && !_selectedCards.Contains(card)) _selectedCards.Add(card);
            if (!selected) _selectedCards.Remove(card);
            card.SetSelected(selected);
        }
    }

    private void EndMarqueeSelection()
    {
        _isMarqueeSelecting = false;
        SelectionMarquee.Visibility = Visibility.Collapsed;
        if (Mouse.Captured == Root) Mouse.Capture(null);
    }

    private void ShowCanvasContextMenu(Point position)
    {
        HideWorkspaceDropdown();
        HideBoardObjectContextMenu();
        CanvasContextMenu.Visibility = Visibility.Visible;
        CanvasContextMenu.UpdateLayout();
        var menuWidth = Math.Max(232, CanvasContextMenu.ActualWidth);
        var menuHeight = Math.Max(230, CanvasContextMenu.ActualHeight);
        var left = Math.Min(Math.Max(12, position.X), Math.Max(12, Root.ActualWidth - menuWidth - 12));
        var top = Math.Min(Math.Max(82, position.Y), Math.Max(82, Root.ActualHeight - menuHeight - 12));
        CanvasContextMenu.Margin = new Thickness(left, top, 0, 0);
        CanvasContextMenu.Opacity = 0;
        CanvasContextMenu.IsHitTestVisible=true;
        _canvasContextAnimationVersion++;
        CanvasContextMenu.BeginAnimation(OpacityProperty,null);CanvasContextMenuScale.BeginAnimation(ScaleTransform.ScaleXProperty,null);CanvasContextMenuScale.BeginAnimation(ScaleTransform.ScaleYProperty,null);
        CanvasContextMenuScale.ScaleX = 0.96;
        CanvasContextMenuScale.ScaleY = 0.96;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        CanvasContextMenu.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(130)) { EasingFunction = ease });
        CanvasContextMenuScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        CanvasContextMenuScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
    }

    private void HideCanvasContextMenu()
    {
        if(CanvasContextMenu is null || CanvasContextMenu.Visibility!=Visibility.Visible || !CanvasContextMenu.IsHitTestVisible) return;
        CanvasContextMenu.IsHitTestVisible=false;
        var version=++_canvasContextAnimationVersion;
        var fade=new DoubleAnimation(0,TimeSpan.FromMilliseconds(90)) {EasingFunction=new QuadraticEase {EasingMode=EasingMode.EaseIn}};
        fade.Completed+=(_,_)=> {if(version==_canvasContextAnimationVersion) CanvasContextMenu.Visibility=Visibility.Collapsed;};
        CanvasContextMenu.BeginAnimation(OpacityProperty,fade);
        CanvasContextMenuScale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(.97,TimeSpan.FromMilliseconds(90)));
        CanvasContextMenuScale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(.97,TimeSpan.FromMilliseconds(90)));
    }

    private void MoveSelectedGroup(ItemCard lead)
    {
        if (_groupDragLead != lead || _groupDragPositions.Count <= 1) return;
        var originalLead = _groupDragPositions[lead];
        var delta = new Vector(Canvas.GetLeft(lead) - originalLead.X, Canvas.GetTop(lead) - originalLead.Y);
        foreach (var (card, originalPosition) in _groupDragPositions)
        {
            if (card == lead) continue;
            var position = ClampCardPosition(card, new Point(originalPosition.X + delta.X, originalPosition.Y + delta.Y));
            Canvas.SetLeft(card, position.X);
            Canvas.SetTop(card, position.Y);
            card.Item.X = position.X;
            card.Item.Y = position.Y;
        }
    }

    private Point ClampCardPosition(ItemCard card, Point position)
    {
        var width = Math.Max(0, _activeWorkspace.WorldWidth - card.ActualWidth);
        var height = Math.Max(0, _activeWorkspace.WorldHeight - card.ActualHeight);
        return new Point(Math.Clamp(position.X, 0, width), Math.Clamp(position.Y, 0, height));
    }

    private void FolderOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == FolderOverlay)
        {
            HideFolderOverlay();
            e.Handled = true;
        }
    }

    private void FolderOverlayTitle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        BeginFolderTitleEdit();
        e.Handled = true;
    }

    private void FolderOverlayTitleEditor_LostFocus(object sender, RoutedEventArgs e) => CommitFolderTitleEdit();

    private void FolderOverlayTitleEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitFolderTitleEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelFolderTitleEdit();
            e.Handled = true;
        }
    }

    private void FolderPanel_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void BeginFolderTitleEdit()
    {
        if (_openFolder is null)
        {
            return;
        }

        FolderOverlayTitleEditor.Text = _openFolder.DisplayName;
        FolderOverlayTitle.Visibility = Visibility.Collapsed;
        FolderOverlayTitleEditor.Visibility = Visibility.Visible;
        FolderOverlayTitleEditor.SelectAll();
        FolderOverlayTitleEditor.Focus();
    }

    private void CommitFolderTitleEdit()
    {
        if (FolderOverlayTitleEditor.Visibility != Visibility.Visible)
        {
            return;
        }

        var newName = FolderOverlayTitleEditor.Text.Trim();
        FolderOverlayTitleEditor.Visibility = Visibility.Collapsed;
        FolderOverlayTitle.Visibility = Visibility.Visible;

        if (_openFolder is null || string.IsNullOrWhiteSpace(newName) || newName == _openFolder.DisplayName)
        {
            return;
        }

        RegisterUndoSnapshot();
        _openFolder.DisplayName = newName;
        UpdateFolderOverlayTitle();
        RefreshCard(_openFolder);
        Save();
        ShowToast("Pasta renomeada");
    }

    private void CancelFolderTitleEdit()
    {
        FolderOverlayTitleEditor.Visibility = Visibility.Collapsed;
        FolderOverlayTitle.Visibility = Visibility.Visible;
        if (_openFolder is not null)
        {
            UpdateFolderOverlayTitle();
        }
    }

    private void TrashZone_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            ResetWorkspace();
            e.Handled = true;
        }
    }

    private void WorkspaceCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_initialLayoutDone)
        {
            return;
        }

        _initialLayoutDone = true;
        Save();
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveHeader();
    private void Root_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateResponsiveHeader();

    private void UpdateResponsiveHeader()
    {
        if (HeaderBar is null || WorkspaceSearchSurface is null || Root is null) return;
        var width = Root.ActualWidth > 0 ? Root.ActualWidth : ActualWidth;
        var compact = width < 1250;
        var medium = width >= 1250 && width < 1650;
        var veryCompact = width < 1080;
        BrandNameText.Visibility = width < 1080 ? Visibility.Collapsed : Visibility.Visible;
        InstalledVersionText.Visibility = width < 1080 ? Visibility.Collapsed : Visibility.Visible;
        BrandColumn.Width = new GridLength(veryCompact ? 48 : compact ? 112 : medium ? 120 : 154);
        ToolbarColumn.Width = new GridLength(veryCompact ? 174 : compact ? 180 : 184);
        TopToolbar.Width = veryCompact ? 170 : compact ? 176 : 176;
        TopToolbar.Margin = new Thickness(2, 0, compact ? 2 : 6, 0);
        WorkspaceColumn.Width = new GridLength(veryCompact ? 86 : compact ? 108 : medium ? 140 : 178);
        StatusColumn.Width = new GridLength(veryCompact ? 50 : compact ? 58 : medium ? 70 : 72);
        SearchColumn.Width = new GridLength(0);
        WorkspaceSearchSurface.Width = veryCompact ? 170 : compact ? 230 : medium ? (width < 1400 ? 260 : 360) : 460;
        ZoomColumn.Width = new GridLength(compact ? 150 : medium ? 175 : 220);
        WorkspaceNameText.Visibility = Visibility.Visible;
        WorkspaceNameText.MaxWidth = veryCompact ? 66 : compact ? 88 : medium ? 116 : 150;
        WorkspaceNameText.FontSize = compact ? 13 : medium ? 14 : 15;
        WorkspaceButton.Padding = new Thickness(compact ? 1 : 4,0,compact ? 1 : 4,0);
        HeaderBar.Margin = new Thickness(16, 0, 12, 0);
        HeaderBar.Height = 74;
        Grid.SetRow(ZoomSurface,0); Grid.SetColumn(ZoomSurface,6); Grid.SetColumnSpan(ZoomSurface,1);
        ZoomSurface.Width = double.NaN; ZoomSurface.HorizontalAlignment = HorizontalAlignment.Stretch; ZoomSurface.Margin = new Thickness(compact ? 6 : 18,0,0,0);
        WorkspaceSearchSurface.Visibility = Visibility.Visible;
        CloudAccountText.Visibility = width < 1180 ? Visibility.Collapsed : Visibility.Visible;
        CloudStatusText.MaxWidth = compact ? 38 : 48;
        Grid.SetRow(CloudStatusButton,0); Grid.SetColumn(CloudStatusButton,3); Grid.SetColumnSpan(CloudStatusButton,1);
        CloudPanel.Margin = new Thickness(0, 76, Math.Min(300, Math.Max(16,width-330)), 0);

        var dropdownLeft = HeaderBar.Margin.Left + BrandColumn.Width.Value + ToolbarColumn.Width.Value;
        WorkspaceDropdown.Margin = new Thickness(dropdownLeft, 76, 0, 0);
        WorkspaceDropdown.MaxWidth = Math.Max(220, width - dropdownLeft - 20);
    }

    private void RefreshCard(ClipboardItem item)
    {
        var card = WorkspaceCanvas.Children
            .OfType<ItemCard>()
            .FirstOrDefault(candidate => candidate.Item.Id == item.Id);
        card?.Refresh(_storageService);
    }

    private bool IsPointInsideFolderPanel(Point rootPoint)
    {
        var topLeft = FolderPanel.TranslatePoint(new Point(0, 0), Root);
        var rect = new Rect(topLeft, new Size(FolderPanel.ActualWidth, FolderPanel.ActualHeight));
        return rect.Contains(rootPoint);
    }

    private static ClipboardItem? FindItemById(IEnumerable<ClipboardItem> items, string id)
    {
        foreach (var item in items)
        {
            if (item.Id == id)
            {
                return item;
            }

            var child = FindItemById(item.Children, id);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _hwndSource = (HwndSource?)PresentationSource.FromVisual(this);
        if (_hwndSource is null)
        {
            return;
        }

        _hwndSource.AddHook(WndProc);
        ApplyWindowFrameTheme();
        AddClipboardFormatListener(_hwndSource.Handle);
        CaptureClipboardHistory();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmClipboardUpdate)
        {
            CaptureClipboardHistory();
        }

        return IntPtr.Zero;
    }

    private void CaptureClipboardHistory()
    {
        try
        {
            var entry = CreateHistoryEntryFromClipboard();
            if (entry is null)
            {
                return;
            }

            if (_history.FirstOrDefault() is { } latest
                && HistoryContent.AreEquivalent(latest, entry))
            {
                return;
            }

            _history.Insert(0, entry);
            if (_history.Count > 80)
            {
                _history.RemoveAt(_history.Count - 1);
            }
            _storageService.SaveHistory(_history);
            if(!_applyingCloud) { _cloud?.Stage(_workspaces,_history.ToList()); QueueCloudSync(); }

        }
        catch
        {
            // Clipboard can be temporarily locked by the source application.
        }
    }

    private ClipboardHistoryEntry? CreateHistoryEntryFromClipboard()
    {
        if (Clipboard.ContainsFileDropList())
        {
            var paths = Clipboard.GetFileDropList().Cast<string>().ToList();
            if (paths.Count == 0)
            {
                return null;
            }

            return new ClipboardHistoryEntry
            {
                Type = Directory.Exists(paths[0]) ? ClipboardItemType.Folder : ClipboardItemType.File,
                Title = paths.Count == 1 ? Path.GetFileName(paths[0]) : $"{paths.Count} arquivos/pastas",
                Preview = string.Join(Environment.NewLine, paths.Take(4)),
                FilePaths = paths
            };
        }

        if (Clipboard.ContainsImage())
        {
            var image = Clipboard.GetImage();
            if (image is null)
            {
                return null;
            }

            var path = _storageService.SaveBitmap(image);
            return new ClipboardHistoryEntry
            {
                Type = ClipboardItemType.Image,
                Title = "Imagem copiada",
                Preview = "Imagem salva no histórico local",
                StoredFilePath = path
            };
        }

        if (Clipboard.ContainsText())
        {
            var text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var isUrl = Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https";
            var firstLine = text.Trim().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Texto";
            return new ClipboardHistoryEntry
            {
                Type = isUrl ? ClipboardItemType.Link : ClipboardItemType.Text,
                Title = isUrl ? "Link copiado" : (firstLine.Length <= 40 ? firstLine : $"{firstLine[..40]}..."),
                Preview = text.Length <= 180 ? text : $"{text[..180]}...",
                Text = isUrl ? null : text,
                Url = isUrl ? text.Trim() : null
            };
        }

        return null;
    }

    private int _historyAnimationVersion;

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryPane.Visibility == Visibility.Visible && HistoryPane.IsHitTestVisible) { HideHistoryPanel(); return; }
        var version = ++_historyAnimationVersion;
        HistoryColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
        HistoryColumn.MinWidth = 0;
        HistoryColumn.MaxWidth = Math.Max(300, Math.Min(520, ActualWidth - 510));
        var targetWidth = Math.Min(_historyWidth, HistoryColumn.MaxWidth);
        // Measure the board once; animate only composited properties, never the layout width.
        HistoryColumn.Width = new GridLength(targetWidth);
        HistoryColumn.MinWidth = Math.Min(300, targetWidth);
        HistoryPane.Visibility = Visibility.Visible;
        HistoryPane.IsHitTestVisible = true;
        HistoryPane.RenderTransform = new TranslateTransform();
        HistorySplitter.Visibility = Visibility.Visible;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var slide = new DoubleAnimation(-26, 0, TimeSpan.FromMilliseconds(190)) { EasingFunction = ease };
        slide.Completed += (_, _) => { if (version == _historyAnimationVersion) HistoryPane.FocusSearch(); };
        ((TranslateTransform)HistoryPane.RenderTransform).BeginAnimation(TranslateTransform.XProperty, slide);
        HistoryPane.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        HistorySplitter.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
    }

    private void HideHistoryPanel()
    {
        if (HistoryPane.Visibility != Visibility.Visible) return;
        var version = ++_historyAnimationVersion;
        _historyWidth = Math.Max(300, HistoryColumn.ActualWidth);
        HistoryPane.IsHitTestVisible = false;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(130));
        fade.Completed += (_, _) =>
        {
            if (version != _historyAnimationVersion) return;
            HistoryPane.Visibility = Visibility.Collapsed;
            HistorySplitter.Visibility = Visibility.Collapsed;
            HistoryColumn.MinWidth = 0;
            HistoryColumn.Width = new GridLength(0);
            WorkspaceCanvas.Focus();
        };
        HistoryPane.BeginAnimation(OpacityProperty, fade);
        HistorySplitter.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(130)));
    }

    private void ShowCompactHistory()
    {
        if (!IsEnabled) return;
        if (_historyPopup is null)
        {
            _historyPopup = new ClipboardPopupWindow();
            _historyPopup.History.Bind(_history, compact: true);
            _historyPopup.History.CopyRequested += HistoryCopyRequested;
            _historyPopup.History.AddRequested += (_, entry) => { ShowWorkspace(); AddHistoryToWorkspace(entry); };
            _historyPopup.History.OpenWorkspaceRequested += (_, _) => ShowWorkspace();
        }
        if (_historyPopup.IsVisible) _historyPopup.Hide();
        else _historyPopup.ShowAtCursor();
    }

    private void SetHistoryDropGlow(bool visible)
    {
        var targetOpacity = visible ? 1d : 0d;
        HistoryDropGlow.BeginAnimation(OpacityProperty, new DoubleAnimation(targetOpacity, TimeSpan.FromMilliseconds(110)));
    }

    private void ShowWorkspace()
    {
        _historyPopup?.Hide();
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    internal void ActivateFromSecondaryLaunch() => ShowWorkspace();

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isExitRequested)
        {
            e.Cancel = true;
            _historyPopup?.Hide();
            Hide();
            return;
        }

        if (_hwndSource is not null)
        {
            RemoveClipboardFormatListener(_hwndSource.Handle);
            _hwndSource.RemoveHook(WndProc);
        }

        Save();
        _storageService.SaveHistory(_history);
        _appearanceSaveTimer.Stop();
        SaveAppearanceSettings();
    }

    private void ExitApplication()
    {
        if (_isExitRequested) return;
        _isExitRequested = true;
        Application.Current.Shutdown();
    }

    private void StartupButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _startupService.SetEnabled(!_startupService.IsEnabled());
            UpdateStartupButtonState();
            ShowToast(_startupService.IsEnabled() ? "O ClipDesk iniciará discretamente com o Windows" : "Início com o Windows desativado");
        }
        catch (Exception ex)
        {
            _soundService.Error();
            ShowToast($"Não foi possível alterar o início com o Windows: {ex.Message}");
        }
    }

    private void UpdateStartupButtonState()
    {
        var enabled = _startupService.IsEnabled();
        StartupButton.Opacity = enabled ? 1 : 0.55;
        StartupButton.ToolTip = enabled ? "Iniciar com o Windows: ativado" : "Iniciar com o Windows: desativado";
    }

    private void WorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (WorkspaceDropdown.Visibility == Visibility.Visible)
        {
            HideWorkspaceDropdown();
            return;
        }
        OpenWorkspaceDropdown();
    }

    private void OpenWorkspaceDropdown(WorkspaceBoard? modeTarget = null)
    {
        BuildWorkspaceDropdown();
        if(modeTarget is not null) ShowWorkspaceModePage(modeTarget);
        WorkspaceDropdown.Visibility = Visibility.Visible;
        WorkspaceDropdown.Opacity = 0;
        WorkspaceDropdownScale.ScaleX = 0.96;
        WorkspaceDropdownScale.ScaleY = 0.96;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        WorkspaceDropdown.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease });
        WorkspaceDropdownScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(210)) { EasingFunction = ease });
        WorkspaceDropdownScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(210)) { EasingFunction = ease });
    }

    private void BuildWorkspaceDropdown()
    {
        WorkspaceDropdownTitle.Text = _activeWorkspace.Name;
        WorkspaceDropdownCurrentStatusIcon.Text = WorkspaceModeGlyph(_activeWorkspace.SyncMode);
        WorkspaceDropdownCurrentStatusIcon.ToolTip = WorkspaceModeLabel(_activeWorkspace.SyncMode);
        WorkspaceDropdownDeleteButton.IsEnabled = CanDeleteWorkspace(_activeWorkspace);
        WorkspaceDropdownDeleteButton.Opacity = WorkspaceDropdownDeleteButton.IsEnabled ? 0.82 : 0.3;
        WorkspaceDropdownDeleteButton.ToolTip = WorkspaceDropdownDeleteButton.IsEnabled
            ? "Excluir esta mesa completamente"
            : "Somente o proprietário pode excluir esta mesa";
        WorkspaceDropdownItems.Children.Clear();
        WorkspaceDropdownActions.Children.Clear();
        ResetWorkspaceDropdownPage();

        foreach (var board in _workspaces.Where(candidate => candidate.Id != _activeWorkspace.Id)
                     .OrderBy(candidate => candidate.SyncMode == WorkspaceSyncMode.Shared ? 0
                         : candidate.SyncMode == WorkspaceSyncMode.PersonalCloud ? 1 : 2))
        {
            WorkspaceDropdownItems.Children.Add(CreateWorkspaceDropdownRow(board));
        }

        if (WorkspaceDropdownItems.Children.Count == 0)
        {
            WorkspaceDropdownItems.Children.Add(new TextBlock
            {
                Text = "Nenhuma outra mesa criada",
                Margin = new Thickness(10, 2, 10, 4),
                Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#94A3B8" : "#64748B")!,
                FontSize = 13
            });
        }

        WorkspaceDropdownActions.Children.Add(CreateWorkspaceDropdownButton("Nova mesa", "\uE145", () => ShowWorkspaceNameOverlay(create: true), accent: true));
    }

    private FrameworkElement CreateWorkspaceDropdownRow(WorkspaceBoard board)
    {
        var row = new Grid { Height = 42, Margin = new Thickness(0, 0, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        var switchButton = new Button
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#F8FAFC" : "#334155")!,
            ToolTip = $"Abrir {board.Name}"
        };
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(MaterialGlyph("\uE8F1", 16));
        var label = new TextBlock { Text = board.Name, FontSize = 14, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(label, 1); content.Children.Add(label);
        switchButton.Content = content;
        switchButton.Click += (_, _) => { HideWorkspaceDropdown(immediate: true); _ = SwitchWorkspaceAsync(board); };
        Grid.SetColumnSpan(switchButton, 2);
        row.Children.Add(switchButton);

        var statusButton=new Button {Width=30,Height=30,Padding=new Thickness(0),Background=Brushes.Transparent,ToolTip=WorkspaceModeLabel(board.SyncMode)};
        var status=MaterialGlyph(WorkspaceModeGlyph(board.SyncMode),17);status.Foreground=(Brush)new BrushConverter().ConvertFromString("#AAB8D0")!;
        statusButton.Content=status;
        statusButton.Click+=(_,eventArgs)=>{eventArgs.Handled=true;ShowWorkspaceModePage(board);};
        Grid.SetColumn(statusButton,2);row.Children.Add(statusButton);
        var deleteButton = new Button { Width = 30, Height = 30, Padding = new Thickness(0), Background = Brushes.Transparent, ToolTip = "Excluir mesa", IsEnabled = CanDeleteWorkspace(board) };
        deleteButton.Opacity = deleteButton.IsEnabled ? 0.72 : 0.24;
        deleteButton.Content = MaterialGlyph("\uE92E", 16);
        deleteButton.Click += (_, eventArgs) => { eventArgs.Handled = true; RequestWorkspaceDeletion(board); };
        Grid.SetColumn(deleteButton, 4); row.Children.Add(deleteButton);
        return row;
    }

    private void ShowWorkspaceModePage(WorkspaceBoard board)
    {
        _workspaceModeTarget=board;
        WorkspaceModeTargetName.Text=board.Name;
        WorkspaceDropdownModes.Children.Clear();
        WorkspaceDropdownModes.Children.Add(CreateWorkspaceModeButton(board,WorkspaceSyncMode.Local,"Somente local"));
        WorkspaceDropdownModes.Children.Add(CreateWorkspaceModeButton(board,WorkspaceSyncMode.PersonalCloud,"Nuvem pessoal"));
        WorkspaceDropdownModes.Children.Add(CreateWorkspaceModeButton(board,WorkspaceSyncMode.Shared,"Compartilhada"));
        WorkspaceDropdownHome.Visibility=Visibility.Collapsed;
        WorkspaceDropdownModePage.Visibility=Visibility.Visible;
    }

    private void ResetWorkspaceDropdownPage()
    {
        _workspaceModeTarget=null;
        WorkspaceDropdownModes.Children.Clear();
        WorkspaceDropdownModePage.Visibility=Visibility.Collapsed;
        WorkspaceDropdownHome.Visibility=Visibility.Visible;
    }

    private Button CreateWorkspaceModeButton(WorkspaceBoard board, WorkspaceSyncMode mode, string title)
    {
        var selected = board.SyncMode == mode;
        var button = new Button
        {
            Height = 37,
            Margin = new Thickness(0, 0, 0, 3),
            Padding = new Thickness(10, 0, 8, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = selected ? (Brush)new BrushConverter().ConvertFromString("#335B42B6")! : Brushes.Transparent,
            Foreground = (Brush)new BrushConverter().ConvertFromString(selected ? (_isDarkMode ? "#D9CCFF" : "#43318F") : (_isDarkMode ? "#CBD5E1" : "#475569"))!,
            ToolTip = WorkspaceModeDescription(mode)
        };
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        content.Children.Add(MaterialGlyph(WorkspaceModeGlyph(mode), 18));
        var label = new TextBlock { Text = title, FontSize = 13, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 1); content.Children.Add(label);
        if (selected)
        {
            var check = MaterialGlyph("\uE5CA", 17); Grid.SetColumn(check, 2); content.Children.Add(check);
        }
        button.Content = content;
        button.Click += (_, eventArgs) => { eventArgs.Handled = true; _ = SetWorkspaceModeFromDropdownAsync(board,mode); };
        return button;
    }

    private TextBlock MaterialGlyph(string glyph, double size) => new()
    {
        Text = glyph,
        FontFamily = (FontFamily)FindResource("MaterialSymbols"),
        FontSize = size,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static string WorkspaceModeGlyph(WorkspaceSyncMode mode) => mode switch
    {
        WorkspaceSyncMode.Local => "\uE31E",
        WorkspaceSyncMode.PersonalCloud => "\uE2C3",
        _ => "\uF233"
    };

    private static string WorkspaceModeLabel(WorkspaceSyncMode mode) => mode switch
    {
        WorkspaceSyncMode.Local => "Somente local",
        WorkspaceSyncMode.PersonalCloud => "Sincronizada na nuvem",
        _ => "Compartilhada com colaboradores"
    };

    private static string WorkspaceModeDescription(WorkspaceSyncMode mode) => mode switch
    {
        WorkspaceSyncMode.Local => "Mantém esta mesa somente neste computador",
        WorkspaceSyncMode.PersonalCloud => "Sincroniza esta mesa na sua conta",
        _ => "Sincroniza e permite convidar colaboradores"
    };

    private bool CanDeleteWorkspace(WorkspaceBoard board) =>
        board.SyncMode == WorkspaceSyncMode.Local || board.OwnerId is null || board.OwnerId == _cloud?.User?.Id;

    private Button CreateWorkspaceDropdownButton(string title, string glyph, Action action, bool accent = false)
    {
        var foreground = (Brush)new BrushConverter().ConvertFromString(accent ? "#A78BFA" : (_isDarkMode ? "#F8FAFC" : "#334155"))!;
        var button = new Button
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Height = 42,
            Margin = new Thickness(0, 0, 0, 5),
            Padding = new Thickness(10, 0, 10, 0),
            Background = Brushes.Transparent,
            Foreground = foreground
        };
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = (FontFamily)FindResource("MaterialSymbols"),
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center
        });
        var label = new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(label, 1);
        content.Children.Add(label);
        button.Content = content;
        button.Click += (_, _) =>
        {
            HideWorkspaceDropdown(immediate: true);
            action();
        };
        return button;
    }

    private void HideWorkspaceDropdown(bool immediate = false)
    {
        if (WorkspaceDropdown.Visibility != Visibility.Visible) return;
        if (immediate)
        {
            WorkspaceDropdown.BeginAnimation(OpacityProperty, null);
            WorkspaceDropdown.Visibility = Visibility.Collapsed;
            ResetWorkspaceDropdownPage();
            return;
        }

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(110));
        fade.Completed += (_, _) => {WorkspaceDropdown.Visibility = Visibility.Collapsed;ResetWorkspaceDropdownPage();};
        WorkspaceDropdown.BeginAnimation(OpacityProperty, fade);
    }

    private void WorkspaceEditButton_Click(object sender, RoutedEventArgs e)
    {
        HideWorkspaceDropdown(immediate: true);
        ShowWorkspaceNameOverlay(create: false);
        e.Handled = true;
    }

    private void WorkspaceDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        RequestWorkspaceDeletion(_activeWorkspace);
        e.Handled = true;
    }

    private void WorkspaceCurrentStatusButton_Click(object sender,RoutedEventArgs e)
    {
        ShowWorkspaceModePage(_activeWorkspace);
        e.Handled=true;
    }

    private void WorkspaceModeBackButton_Click(object sender,RoutedEventArgs e)
    {
        ResetWorkspaceDropdownPage();
        e.Handled=true;
    }

    private void ShowWorkspaceNameOverlay(bool create)
    {
        _workspaceNameCreates = create;
        WorkspaceNameDialogTitle.Text = create ? "Criar nova mesa" : "Editar nome da mesa";
        WorkspaceNameDialogPrompt.Text = create ? "Como você quer chamar esta mesa?" : "Escolha um nome que identifique melhor este espaço.";
        WorkspaceNameDialogIcon.Text = create ? "\uE710" : "\uE70F";
        WorkspaceNameConfirmButton.Content = create ? "Criar mesa" : "Salvar";
        WorkspaceNameInput.Text = create ? $"Mesa {_workspaces.Count + 1}" : _activeWorkspace.Name;
        WorkspaceNameOverlay.Visibility = Visibility.Visible;
        WorkspaceNameOverlay.Opacity = 0;
        WorkspaceNameDialogScale.ScaleX = 0.94;
        WorkspaceNameDialogScale.ScaleY = 0.94;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        WorkspaceNameOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease });
        WorkspaceNameDialogScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease });
        WorkspaceNameDialogScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease });
        Dispatcher.BeginInvoke(() =>
        {
            WorkspaceNameInput.Focus();
            WorkspaceNameInput.SelectAll();
        });
    }

    private void HideWorkspaceNameOverlay()
    {
        if (WorkspaceNameOverlay.Visibility != Visibility.Visible) return;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(130));
        fade.Completed += (_, _) => WorkspaceNameOverlay.Visibility = Visibility.Collapsed;
        WorkspaceNameOverlay.BeginAnimation(OpacityProperty, fade);
    }

    private async void WorkspaceNameConfirm_Click(object sender, RoutedEventArgs e)
    {
        await CommitWorkspaceNameAsync();
        e.Handled = true;
    }

    private async Task CommitWorkspaceNameAsync()
    {
        if (!WorkspaceNameConfirmButton.IsEnabled) return;
        var name = WorkspaceNameInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            WorkspaceNameInput.Focus();
            return;
        }

        WorkspaceNameConfirmButton.IsEnabled = false;
        try
        {
            if (_workspaceNameCreates)
            {
                Save();
                var board = new WorkspaceBoard { Name = name };
                _workspaces.Add(board);
                HideWorkspaceNameOverlay();
                await SwitchWorkspaceAsync(board, saveCurrent: false);
                Save();
                ShowToast("Nova mesa criada");
            }
            else
            {
                _activeWorkspace.Name = name;
                Save();
                UpdateWorkspacePresentation();
                HideWorkspaceNameOverlay();
                ShowToast("Nome da mesa atualizado");
            }
        }
        finally
        {
            WorkspaceNameConfirmButton.IsEnabled = true;
        }
    }

    private void WorkspaceNameCancel_Click(object sender, RoutedEventArgs e)
    {
        HideWorkspaceNameOverlay();
        e.Handled = true;
    }

    private void WorkspaceNameInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = CommitWorkspaceNameAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            HideWorkspaceNameOverlay();
            e.Handled = true;
        }
    }

    private void WorkspaceNameOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == WorkspaceNameOverlay) HideWorkspaceNameOverlay();
    }

    private void WorkspaceNameDialog_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private async Task SwitchWorkspaceAsync(WorkspaceBoard board, bool saveCurrent = true)
    {
        if (_isSwitchingWorkspace || board.Id == _activeWorkspace.Id) return;

        StopFollowingCamera(false);
        _isSwitchingWorkspace = true;
        WorkspaceCanvas.IsHitTestVisible = false;
        HideWorkspaceDropdown(immediate: true);
        try
        {
            if (saveCurrent) Save();
            HideFolderOverlay();
            ClearSelection();
            var cards = WorkspaceCanvas.Children.OfType<ItemCard>().ToList();
            await Task.WhenAll(cards.Select((card, index) => card.PlayWorkspaceExit(index)));
            WorkspaceCanvas.Children.Clear();

            _activeWorkspace = board;
            _items = board.Items;
            SetCreativeTool(CreativeTool.Select);
            ApplyWorkspaceViewport(board);
            _undoStack.Clear();
            _redoStack.Clear();
            UpdateWorkspacePresentation();
            RenderAllItems(animate: true);
            EnsureWorkspaceExtent();
            _=PrepareActivePresenceAsync();
            UpdateUndoRedoButtons();
        }
        catch (Exception ex)
        {
            _soundService.Error();
            ShowToast($"Não foi possível trocar de mesa: {ex.Message}");
        }
        finally
        {
            WorkspaceCanvas.IsHitTestVisible = true;
            _isSwitchingWorkspace = false;
        }
    }

    private void ApplyWorkspaceViewport(WorkspaceBoard board)
    {
        var state = _appearanceSettings.BoardViewports.GetValueOrDefault(board.Id.ToString("N"));
        _workspaceZoom = state is null
            ? Math.Clamp(_appearanceSettings.WorkspaceZoom, BoardViewport.MinimumZoom, BoardViewport.MaximumZoom)
            : Math.Clamp(state.Zoom, BoardViewport.MinimumZoom, BoardViewport.MaximumZoom);
        ZoomSlider.Value = _workspaceZoom * 100;
        Dispatcher.BeginInvoke(() =>
        {
            if (state is null) return;
            WorkspaceScroll.ScrollToHorizontalOffset(Math.Clamp(state.PanX, 0, WorkspaceScroll.ScrollableWidth));
            WorkspaceScroll.ScrollToVerticalOffset(Math.Clamp(state.PanY, 0, WorkspaceScroll.ScrollableHeight));
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void UpdateWorkspacePresentation()
    {
        if (WorkspaceNameText is not null) WorkspaceNameText.Text = _activeWorkspace.Name;
        Title = $"{AppEnvironment.Name} — {_activeWorkspace.Name}";
        UpdateCloudPresentation();
    }

    private void HistoryCopyRequested(object? sender, ClipboardHistoryEntry entry)
    {
        try
        {
            _clipboardService.CopyToClipboard(HistoryContent.ToItem(entry), _storageService);
            _soundService.Copied();
            (sender as HistoryView)?.ShowStatus("Copiado. Pronto para colar com Ctrl+V.");
            ShowToast("Copiado para a área de transferência");
        }
        catch (Exception ex)
        {
            (sender as HistoryView)?.ShowStatus($"Não foi possível copiar: {ex.Message}");
            _soundService.Error();
        }
    }

    private void AddHistoryToWorkspace(ClipboardHistoryEntry entry, Point? position = null)
    {
        try
        {
            var item = HistoryContent.ToItem(entry);
            if (item.Type == ClipboardItemType.Image && !File.Exists(item.StoredFilePath))
                throw new IOException("A imagem não está mais disponível.");
            if (item.IsFileBacked && !item.FilePaths.Any(path => File.Exists(path) || Directory.Exists(path)))
                throw new IOException("O arquivo não está mais disponível.");
            AddItems([item], position);
            EnsureWorkspaceExtent();
        }
        catch (Exception ex) { ShowToast(ex.Message); }
    }

    private void WorkspaceScroll_SizeChanged(object sender, SizeChangedEventArgs e) => EnsureWorkspaceExtent();

    private void WorkspaceCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Keep the logical point below the pointer stationary while changing the
        // camera scale. Cards retain their own board coordinates for every user.
        var boardPoint = e.GetPosition(WorkspaceCanvas);
        var screenPoint = e.GetPosition(WorkspaceScroll);
        var next = Math.Clamp(_workspaceZoom * (e.Delta > 0 ? 1.07 : 1 / 1.07), ZoomSlider.Minimum / 100d, 1d);
        if (Math.Abs(next - _workspaceZoom) < .0001) return;
        _zoomAnimationWorldAnchor = boardPoint;
        _zoomAnimationScreenAnchor = screenPoint;
        _zoomAnchoredToPointer = true;
        ZoomSlider.Value = next * 100;
        _zoomAnchoredToPointer = false;
        e.Handled = true;
    }

    private void UpdateAnimatedZoomAnchor()
    {
        if (WorkspaceBoardScale is null || WorkspaceScroll is null) return;
        var scale = WorkspaceBoardScale.ScaleX;
        WorkspaceScroll.ScrollToHorizontalOffset(Math.Clamp(_zoomAnimationWorldAnchor.X * scale - _zoomAnimationScreenAnchor.X, 0, WorkspaceScroll.ScrollableWidth));
        WorkspaceScroll.ScrollToVerticalOffset(Math.Clamp(_zoomAnimationWorldAnchor.Y * scale - _zoomAnimationScreenAnchor.Y, 0, WorkspaceScroll.ScrollableHeight));
        if (DateTime.UtcNow < _zoomAnimationUntil) return;
        _zoomAnchorTimer.Stop();
        WorkspaceBoardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        WorkspaceBoardScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        WorkspaceBoardScale.ScaleX = _workspaceZoom;
        WorkspaceBoardScale.ScaleY = _workspaceZoom;
        WorkspaceExtentHost.Width = _activeWorkspace.WorldWidth * _workspaceZoom;
        WorkspaceExtentHost.Height = _activeWorkspace.WorldHeight * _workspaceZoom;
        WorkspaceScroll.ScrollToHorizontalOffset(Math.Clamp(_zoomAnimationWorldAnchor.X * _workspaceZoom - _zoomAnimationScreenAnchor.X, 0, WorkspaceScroll.ScrollableWidth));
        WorkspaceScroll.ScrollToVerticalOffset(Math.Clamp(_zoomAnimationWorldAnchor.Y * _workspaceZoom - _zoomAnimationScreenAnchor.Y, 0, WorkspaceScroll.ScrollableHeight));
    }

    private void EnsureWorkspaceExtent()
    {
        if (WorkspaceCanvas is null || WorkspaceScroll is null || _items is null) return;
        WorkspaceCanvas.Width = _activeWorkspace.WorldWidth;
        WorkspaceCanvas.Height = _activeWorkspace.WorldHeight;
        if (!_zoomAnchorTimer.IsEnabled)
        {
            WorkspaceExtentHost.Width = _activeWorkspace.WorldWidth * _workspaceZoom;
            WorkspaceExtentHost.Height = _activeWorkspace.WorldHeight * _workspaceZoom;
        }
        var fit = Math.Min(
            WorkspaceScroll.ViewportWidth / Math.Max(1, _activeWorkspace.WorldWidth),
            WorkspaceScroll.ViewportHeight / Math.Max(1, _activeWorkspace.WorldHeight));
        if (double.IsFinite(fit) && fit > 0)
        {
            ZoomSlider.Minimum = Math.Clamp(fit * 100, 1, 100);
            if (_workspaceZoom < fit) ZoomSlider.Value = ZoomSlider.Minimum;
        }
        WorkspaceEmptyState.Visibility = _items.Count == 0 && !_activeWorkspace.Objects.Any(boardObject => boardObject.Kind != BoardObjectKind.Connector)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void WorkspaceSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (WorkspaceSearchPlaceholder is null || WorkspaceCanvas is null) return;
        var query = WorkspaceSearchBox.Text.Trim();
        WorkspaceSearchPlaceholder.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var found = 0;
        foreach (var card in WorkspaceCanvas.Children.OfType<ItemCard>())
        {
            var matches = MatchesWorkspaceSearch(card.Item, query);
            if (matches) found++;
            if (matches && card.Visibility != Visibility.Visible)
            {
                card.Opacity = 0;
                card.Visibility = Visibility.Visible;
                card.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(170))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            }
            else if (!matches)
            {
                card.Visibility = Visibility.Collapsed;
            }
        }
        WorkspaceSearchCount.Text = query.Length == 0 ? string.Empty : found.ToString();
    }

    private static bool MatchesWorkspaceSearch(ClipboardItem item, string? rawQuery)
    {
        if (string.IsNullOrWhiteSpace(rawQuery)) return true;
        var query = rawQuery.Trim();
        bool Contains(string? value) => !string.IsNullOrWhiteSpace(value) && value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
        return Contains(item.DisplayName) || Contains(item.Text) || Contains(item.Url)
            || item.FilePaths.Any(path => Contains(Path.GetFileName(path)) || Contains(Path.GetExtension(path)))
            || item.Children.Any(child => MatchesWorkspaceSearch(child, query));
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ZoomValueText is null || WorkspaceCanvas is null) return;
        if(_applyingPresenceTravel) return;
        if(_followedMemberId is not null) StopFollowingCamera(true);
        var displayed = Math.Max(WorkspaceBoardScale.ScaleX, .0001);
        if (!_zoomAnchoredToPointer)
        {
            _zoomAnimationScreenAnchor = new Point(WorkspaceScroll.ViewportWidth / 2, WorkspaceScroll.ViewportHeight / 2);
            _zoomAnimationWorldAnchor = new Point(
                (WorkspaceScroll.HorizontalOffset + _zoomAnimationScreenAnchor.X) / displayed,
                (WorkspaceScroll.VerticalOffset + _zoomAnimationScreenAnchor.Y) / displayed);
        }
        _workspaceZoom = Math.Clamp(e.NewValue / 100d, BoardViewport.MinimumZoom, 1d);
        if (IsLoaded)
        {
            var currentExtentScale = WorkspaceExtentHost.Width > 0
                ? WorkspaceExtentHost.Width / Math.Max(1, _activeWorkspace.WorldWidth)
                : displayed;
            var animationExtentScale = Math.Max(currentExtentScale, Math.Max(displayed, _workspaceZoom));
            WorkspaceExtentHost.Width = _activeWorkspace.WorldWidth * animationExtentScale;
            WorkspaceExtentHost.Height = _activeWorkspace.WorldHeight * animationExtentScale;
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            WorkspaceBoardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            WorkspaceBoardScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            WorkspaceBoardScale.ScaleX = _workspaceZoom;
            WorkspaceBoardScale.ScaleY = _workspaceZoom;
            var duration = TimeSpan.FromMilliseconds(155);
            var zoomX = new DoubleAnimation(displayed, _workspaceZoom, duration) { EasingFunction = ease };
            var zoomY = new DoubleAnimation(displayed, _workspaceZoom, duration) { EasingFunction = ease };
            Timeline.SetDesiredFrameRate(zoomX, 60);
            Timeline.SetDesiredFrameRate(zoomY, 60);
            WorkspaceBoardScale.BeginAnimation(ScaleTransform.ScaleXProperty, zoomX);
            WorkspaceBoardScale.BeginAnimation(ScaleTransform.ScaleYProperty, zoomY);
            _zoomAnimationUntil = DateTime.UtcNow.AddMilliseconds(170);
            _zoomAnchorTimer.Start();
        }
        else
        {
            WorkspaceBoardScale.ScaleX = _workspaceZoom;
            WorkspaceBoardScale.ScaleY = _workspaceZoom;
            WorkspaceExtentHost.Width = _activeWorkspace.WorldWidth * _workspaceZoom;
            WorkspaceExtentHost.Height = _activeWorkspace.WorldHeight * _workspaceZoom;
        }
        ZoomValueText.Text = $"{Math.Round(_workspaceZoom * 100):0}%";
        UpdateRemoteCursorScales();
        EnsureWorkspaceExtent();
        if (!IsInitialized) return;
        _appearanceSaveTimer.Stop();
        _appearanceSaveTimer.Start();
        RefreshPresenceViewport();
    }

    private void WorkspaceScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if(!IsLoaded || _activeWorkspace.SyncMode!=WorkspaceSyncMode.Shared || _presenceTravelTimer.IsEnabled) return;
        _presenceInputDirty=true;
    }

    private void SaveAppearanceSettings()
    {
        try
        {
            _appearanceSettings.IsDarkMode = _isDarkMode;
            _appearanceSettings.MagnetAlignmentEnabled = _magnetAlignmentEnabled;
            _appearanceSettings.WorkspaceZoom = _workspaceZoom;
            _appearanceSettings.ZoomScaleVersion = 3;
            _appearanceSettings.BoardViewports[_activeWorkspace.Id.ToString("N")] = new BoardViewportState
            {
                Zoom = _workspaceZoom,
                PanX = WorkspaceScroll.HorizontalOffset,
                PanY = WorkspaceScroll.VerticalOffset
            };
            _storageService.SaveSettings(_appearanceSettings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private double GetElementScale() => 1;
    private void UndoButton_Click(object sender, RoutedEventArgs e) => UndoLastChange();
    private void RedoButton_Click(object sender, RoutedEventArgs e) => RedoLastChange();

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _isDarkMode = !_isDarkMode;
        ApplyTheme();
        try { SaveAppearanceSettings(); }
        catch (Exception ex) { ShowToast($"Não foi possível salvar o tema: {ex.Message}"); }
    }

    private void RegisterUndoSnapshot()
    {
        if (_isRestoringSnapshot)
        {
            return;
        }

        SaveCardPositionsToItems();
        var snapshot = CaptureBoardSnapshot();
        var snapshotJson = JsonSerializer.Serialize(snapshot, SnapshotJsonOptions);
        var latestJson = _undoStack.Count > 0
            ? JsonSerializer.Serialize(_undoStack.Peek(), SnapshotJsonOptions)
            : null;

        if (snapshotJson == latestJson)
        {
            return;
        }

        _undoStack.Push(snapshot);
        _redoStack.Clear();
        UpdateUndoRedoButtons();
    }

    private void UndoLastChange()
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        _redoStack.Push(CaptureBoardSnapshot());
        RestoreSnapshot(_undoStack.Pop());
        ShowToast("Alteracao desfeita");
    }

    private void RedoLastChange()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        _undoStack.Push(CaptureBoardSnapshot());
        RestoreSnapshot(_redoStack.Pop());
        ShowToast("Alteracao refeita");
    }

    private void RestoreSnapshot(BoardSnapshot snapshot)
    {
        _isRestoringSnapshot = true;
        HideFolderOverlay();
        _items.Clear();
        _items.AddRange(CloneItems(snapshot.Items));
        _activeWorkspace.Objects = CloneObjects(snapshot.Objects);
        ClearSelection();
        RenderAllItems();
        Save();
        _isRestoringSnapshot = false;
        UpdateUndoRedoButtons();
    }

    private void UpdateUndoRedoButtons()
    {
        UndoButton.IsEnabled = _undoStack.Count > 0;
        RedoButton.IsEnabled = _redoStack.Count > 0;
    }

    private static List<ClipboardItem> CloneItems(IEnumerable<ClipboardItem> items)
    {
        var json = JsonSerializer.Serialize(items, SnapshotJsonOptions);
        return JsonSerializer.Deserialize<List<ClipboardItem>>(json, SnapshotJsonOptions) ?? [];
    }

    private BoardSnapshot CaptureBoardSnapshot() => new(CloneItems(_items), CloneObjects(_activeWorkspace.Objects));

    private static List<BoardObject> CloneObjects(IEnumerable<BoardObject> objects)
    {
        var json = JsonSerializer.Serialize(objects, SnapshotJsonOptions);
        return JsonSerializer.Deserialize<List<BoardObject>>(json, SnapshotJsonOptions) ?? [];
    }

    private sealed record BoardSnapshot(List<ClipboardItem> Items, List<BoardObject> Objects);

    private void ApplyTheme()
    {
        ThemeService.Apply(_isDarkMode);
        var workspace = _isDarkMode ? "#0B1524" : "#F4F4F2";
        var gridLine = _isDarkMode ? "#182A42" : "#E3E5E8";
        // Only the fixed-size board owns the grid. The surrounding viewport has
        // a separate surface so the background never looks infinite.
        Root.Background = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#070C14" : "#E5E8EC")!;
        WorkspaceCanvas.Background = CreateGridBrush(workspace, gridLine);
        Background = (Brush)new BrushConverter().ConvertFromString(workspace)!;
        HeaderBackdrop.Background = Brushes.Transparent;
        HeaderBackdrop.BorderBrush = Brushes.Transparent;
        TopToolbar.Background = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#A8172232" : "#9CFFFFFF")!;
        TopToolbar.BorderBrush = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#334D6E" : "#55FFFFFF")!;
        WorkspaceSearchSurface.Background = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#A8172232" : "#B8F8FAFC")!;
        WorkspaceSearchSurface.BorderBrush = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#50647A" : "#CBD5E1")!;
        WorkspaceSearchBox.Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#F8FAFC" : "#1F2937")!;
        WorkspaceSearchBox.CaretBrush = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#A78BFA" : "#7C3AED")!;
        WorkspaceSearchPlaceholder.Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#8090A5" : "#718096")!;
        ZoomSurface.Background = WorkspaceSearchSurface.Background;
        ZoomSurface.BorderBrush = WorkspaceSearchSurface.BorderBrush;
        ZoomValueText.Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#D8D4F0" : "#4B5563")!;
        CreativeToolRail.Background = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#B8172232" : "#C8FFFFFF")!;
        CreativeToolRail.BorderBrush = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#405A7C" : "#CBD5E1")!;
        CreativeFormatMenu.Background = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#F21A2332" : "#F7FFFFFF")!;
        CreativeFormatMenu.BorderBrush = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#526784A6" : "#CBD5E1")!;
        CanvasContextMenu.Background = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#F51A2434" : "#FCFFFFFF")!;
        CanvasContextMenu.BorderBrush = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#637B96B8" : "#CBD5E1")!;
        BoardObjectContextMenu.Background = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#F4263040" : "#FCFFFFFF")!;
        BoardObjectContextMenu.BorderBrush = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#61748E" : "#CBD5E1")!;
        foreach (var button in CanvasContextMenu.FindVisualChildren<Button>())
            button.Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#F3F6FF" : "#263449")!;
        foreach (var button in BoardObjectContextMenu.FindVisualChildren<Button>())
            button.Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#EAF0FA" : "#334155")!;
        FolderOverlay.Background = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#C0141820" : "#B8F4F4F2")!;
        FolderPanel.Background = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#F21D2432" : "#F7FFFFFF")!;
        FolderPanel.BorderBrush = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#59647A" : "#CBD5E1")!;
        FolderOverlayTitle.Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#F8FAFC" : "#1F2937")!;
        FolderOverlayTitleEditor.Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#F8FAFC" : "#1F2937")!;
        FolderOverlayTitleEditor.Background = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#331E2531" : "#22FFFFFF")!;
        FolderOverlayTitleEditor.BorderBrush = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#99CBD5E1" : "#66FFFFFF")!;
        ApplyTrashTheme(_isTrashHovering);
        ThemeIcon.Text = _isDarkMode ? "\uE51C" : "\uE518";
        ApplyWindowFrameTheme();

        foreach (var button in TopToolbar.FindVisualChildren<Button>())
        {
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            button.Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#F8FAFC" : "#25282D")!;
        }
        WorkspaceButton.Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#F8FAFC" : "#25282D")!;
        WorkspaceButton.Background = Brushes.Transparent;
        WorkspaceButton.BorderBrush = Brushes.Transparent;
        WorkspaceDropdown.Background = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#D91D2636" : "#E8FFFFFF")!;
        WorkspaceDropdown.BorderBrush = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#5C71839D" : "#C5D1DF")!;
        WorkspaceDropdownTitle.Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#F8FAFC" : "#1F2937")!;
        WorkspaceDropdownEditButton.Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#E2E8F0" : "#334155")!;
        WorkspaceNameDialog.Background = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#F21D2636" : "#FAF8FAFC")!;
        WorkspaceNameDialog.BorderBrush = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#66788AA3" : "#CBD5E1")!;
        WorkspaceNameDialogTitle.Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#F8FAFC" : "#1F2937")!;
        WorkspaceNameDialogPrompt.Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#AAB7C8" : "#64748B")!;
        WorkspaceNameInput.Foreground = WorkspaceNameDialogTitle.Foreground;

        foreach (var card in WorkspaceCanvas.Children.OfType<ItemCard>().ToArray())
        {
            card.ApplyTheme(_isDarkMode);
        }
        foreach (var view in WorkspaceCanvas.Children.OfType<BoardObjectView>().ToArray())
        {
            view.IsDarkMode = _isDarkMode;
            view.RefreshFromObject();
        }

        RenderOpenFolderItems();
    }

    private void ApplyWindowFrameTheme()
    {
        var handle = _hwndSource?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;
        var enabled = _isDarkMode ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
    }

    private static DrawingBrush CreateGridBrush(string background, string line)
    {
        var backgroundBrush = (Brush)new BrushConverter().ConvertFromString(background)!;
        var lineBrush = (Brush)new BrushConverter().ConvertFromString(line)!;
        return new DrawingBrush
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 32, 32),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
            Drawing = new DrawingGroup
            {
                Children =
                {
                    new GeometryDrawing(backgroundBrush, null, new RectangleGeometry(new Rect(0, 0, 32, 32))),
                    new GeometryDrawing(null, new Pen(lineBrush, 1), new GeometryGroup
                    {
                        Children =
                        {
                            new LineGeometry(new Point(32, 0), new Point(32, 32)),
                            new LineGeometry(new Point(0, 32), new Point(32, 32))
                        }
                    })
                }
            }
        };
    }

    private void Save(bool queueCloud = true)
    {
        SaveCardPositionsToItems();
        _activeWorkspace.Items = _items;
        _activeWorkspace.UpdatedAt = DateTime.Now;
        _storageService.SaveWorkspaces(_workspaces);
        _storageService.SaveItems(_items);
        if(!_applyingCloud && queueCloud) { _cloud?.Stage(_workspaces,_history.ToList()); QueueCloudSync(); }
        EnsureWorkspaceExtent();
    }

    private void SaveCardPositionsToItems()
    {
        foreach (var card in WorkspaceCanvas.Children.OfType<ItemCard>())
        {
            card.Item.X = (double)card.GetAnimationBaseValue(Canvas.LeftProperty);
            card.Item.Y = (double)card.GetAnimationBaseValue(Canvas.TopProperty);
            card.Item.Width = card.Width;
            card.Item.Height = card.Height;
        }
    }

    private void ShowToast(string message)
    {
        ToastText.Text = message;

        var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(120));
        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260))
        {
            BeginTime = TimeSpan.FromMilliseconds(1650)
        };

        var storyboard = new Storyboard();
        Storyboard.SetTarget(fadeIn, Toast);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath(OpacityProperty));
        Storyboard.SetTarget(fadeOut, Toast);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(fadeIn);
        storyboard.Children.Add(fadeOut);
        storyboard.Begin();
    }
}

internal static class VisualTreeExtensions
{
    public static IEnumerable<T> FindVisualChildren<T>(this DependencyObject parent)
        where T : DependencyObject
    {
        if (parent is null)
        {
            yield break;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typedChild)
            {
                yield return typedChild;
            }

            foreach (var descendant in child.FindVisualChildren<T>())
            {
                yield return descendant;
            }
        }
    }
}
