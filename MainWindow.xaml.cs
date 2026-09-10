using System.IO;
using System.Collections.ObjectModel;
using ClipDesk.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

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
    private readonly Stack<List<ClipboardItem>> _undoStack = [];
    private readonly Stack<List<ClipboardItem>> _redoStack = [];
    private ItemCard? _activeDropTarget;
    private ClipboardItem? _openFolder;
    private ClipboardItem? _folderDragItem;
    private FrameworkElement? _folderDragElement;
    private Point _folderDragStart;
    private HwndSource? _hwndSource;
    private bool _isDarkMode;
    private bool _isRestoringSnapshot;
    private bool _isTrashHovering;
    private bool _initialLayoutDone;
    private bool _isFolderItemDragging;
    private bool _updateCheckStarted;
    private readonly bool _startHidden;

    public MainWindow(bool startHidden = false)
    {
        InitializeComponent();
        _startHidden = startHidden;
        _workspaces = _storageService.LoadWorkspaces();
        _activeWorkspace = _workspaces[0];
        _items = _activeWorkspace.Items;
        Loaded += MainWindow_Loaded;
        SourceInitialized += MainWindow_SourceInitialized;
        Closing += (_, _) =>
        {
            if (_hwndSource is not null)
            {
                RemoveClipboardFormatListener(_hwndSource.Handle);
                _hwndSource.RemoveHook(WndProc);
            }

            Save();
        };
        _isDarkMode = _storageService.LoadSettings().IsDarkMode;
        ThemeService.Apply(_isDarkMode);
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
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RenderAllItems();
        EnsureWorkspaceExtent();
        _trayService = new TrayService(Dispatcher, ShowCompactHistory, ShowWorkspace, Close);
        UpdateWorkspacePresentation();
        UpdateStartupButtonState();
        if (_startHidden)
        {
            Dispatcher.BeginInvoke(() => Hide());
        }
        else if (!_updateCheckStarted)
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
            if (update is null || !IsVisible || !IsLoaded) return;

            var choice = MessageBox.Show(
                this,
                $"Uma nova versão do ClipDesk está disponível ({update.TagName}).\n\nDeseja baixar e instalar agora? O aplicativo será reiniciado automaticamente.",
                "Atualização disponível",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (choice != MessageBoxResult.Yes) return;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                await UpdateService.DownloadAndStartAsync(update);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }

            MessageBox.Show(this, "A atualização foi baixada. O ClipDesk será reiniciado agora.", "Atualização pronta", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
        catch (OperationCanceledException)
        {
            // A checagem é silenciosa quando a aplicação é encerrada.
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível verificar atualizações agora.\n\n{ex.Message}", "Atualização", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RenderAllItems()
    {
        WorkspaceCanvas.Children.Clear();
        foreach (var item in _items)
        {
            AddCard(item, playPopIn: false);
        }
    }

    private ItemCard AddCard(ClipboardItem item, bool playPopIn)
    {
        var card = new ItemCard(item, _fileIconService, _storageService);
        card.Selected += Card_Selected;
        card.CopyRequested += Card_CopyRequested;
        card.OpenRequested += Card_OpenRequested;
        card.RenameRequested += Card_RenameRequested;
        card.TitleEdited += Card_TitleEdited;
        card.DuplicateRequested += Card_DuplicateRequested;
        card.DeleteRequested += Card_DeleteRequested;
        card.DetailsRequested += Card_DetailsRequested;
        card.DragStarted += Card_DragStarted;
        card.DragMoved += Card_DragMoved;
        card.DragFinished += Card_DragFinished;

        WorkspaceCanvas.Children.Add(card);
        Canvas.SetLeft(card, Math.Max(0, item.X));
        Canvas.SetTop(card, Math.Max(0, item.Y));
        card.ApplyTheme(_isDarkMode);

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
                    dropPoint.Value.X + (index % 4) * 150 - 66,
                    dropPoint.Value.Y + (index / 4) * 166 - 74))
                : FindFreePosition();
            item.X = position.X;
            item.Y = position.Y;
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
        var width = WorkspaceCanvas.ActualWidth > 0 ? WorkspaceCanvas.ActualWidth : ActualWidth;
        var height = WorkspaceCanvas.ActualHeight > 0 ? WorkspaceCanvas.ActualHeight : ActualHeight;
        return new Point(
            Math.Max(0, Math.Min(width - 132, point.X)),
            Math.Max(0, Math.Min(height - 148, point.Y)));
    }

    private Point FindFreePosition()
    {
        var width = WorkspaceCanvas.ActualWidth > 0 ? WorkspaceCanvas.ActualWidth : ActualWidth;
        var height = WorkspaceCanvas.ActualHeight > 0 ? WorkspaceCanvas.ActualHeight : ActualHeight;
        var centerX = Math.Max(40, (width - 132) / 2);
        var centerY = Math.Max(40, (height - 148) / 2);

        for (var step = 0; step < 80; step++)
        {
            var x = centerX + (step % 8) * 150 - 300;
            var y = centerY + (step / 8) * 166 - 166;
            var candidate = new Rect(Math.Max(24, x), Math.Max(24, y), 132, 148);
            var collides = WorkspaceCanvas.Children
                .OfType<ItemCard>()
                .Any(card => new Rect(Canvas.GetLeft(card), Canvas.GetTop(card), 132, 148).IntersectsWith(candidate));

            if (!collides && candidate.Right < width - 24 && candidate.Bottom < height - 24)
            {
                return candidate.Location;
            }
        }

        return new Point(centerX, centerY);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
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

                ClearSelection();
                e.Handled = true;
                break;
        }
    }

    private void PasteFromClipboard()
    {
        try
        {
            AddItems(_clipboardService.CaptureClipboard(_storageService));
        }
        catch (Exception ex)
        {
            _soundService.Error();
            ShowToast($"Não foi possível ler o clipboard: {ex.Message}");
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(HistoryView.DragFormat))
        {
            var point = e.GetPosition(WorkspaceScroll);
            var canDrop = FolderOverlay.Visibility != Visibility.Visible && new Rect(WorkspaceScroll.RenderSize).Contains(point);
            e.Effects = canDrop ? DragDropEffects.Copy : DragDropEffects.None;
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
            e.Effects = HasFileDrop(e)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
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
        return e.Data.GetDataPresent(DataFormats.FileDrop, autoConvert: true);
    }

    private static string[] GetDroppedPaths(DragEventArgs e)
    {
        return e.Data.GetData(DataFormats.FileDrop, autoConvert: true) as string[] ?? [];
    }

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
            SelectSingle(card);
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

    private void OpenItem(ClipboardItem item, ItemCard? ownerCard)
    {
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

    private void ShowFolderOverlay(ClipboardItem folder)
    {
        _openFolder = folder;
        FolderOverlayTitle.Text = folder.DisplayName;
        FolderOverlayTitle.Visibility = Visibility.Visible;
        FolderOverlayTitleEditor.Visibility = Visibility.Collapsed;
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
        FolderItemsPanel.Children.Clear();
        if (_openFolder is null)
        {
            return;
        }

        foreach (var child in _openFolder.Children)
        {
            FolderItemsPanel.Children.Add(CreateFolderOverlayItem(child));
        }
    }

    private UIElement CreateFolderOverlayItem(ClipboardItem item)
    {
        var visual = _fileIconService.GetVisual(item);
        var accent = (SolidColorBrush)new BrushConverter().ConvertFromString(visual.Accent)!;
        var grid = new Grid
        {
            Width = 150,
            Height = 150,
            Background = Brushes.Transparent,
            Tag = item,
            Cursor = Cursors.Hand,
            RenderTransform = new TranslateTransform()
        };

        var icon = new Border
        {
            Width = 96,
            Height = 96,
            Margin = new Thickness(0, 0, 0, 44),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Background = accent,
            CornerRadius = new CornerRadius(24),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 5,
                Opacity = 0.18,
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
                FontSize = visual.UseSymbolFont ? 42 : 34,
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
            Margin = new Thickness(4, 104, 4, 0),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 17,
            FontWeight = FontWeights.Medium,
            Foreground = _isDarkMode ? Brushes.White : Brushes.White,
            MaxHeight = 44
        };

        grid.Children.Add(icon);
        grid.Children.Add(name);
        grid.MouseLeftButtonDown += FolderItem_MouseLeftButtonDown;
        grid.MouseMove += FolderItem_MouseMove;
        grid.MouseLeftButtonUp += FolderItem_MouseLeftButtonUp;
        return grid;
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

    private void Card_RenameRequested(object? sender, EventArgs e)
    {
        if (sender is ItemCard card)
        {
            RenameCard(card);
        }
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
            RegisterUndoSnapshot();
            Panel.SetZIndex(card, 10);
        }
    }

    private void Card_DragMoved(object? sender, EventArgs e)
    {
        if (sender is not ItemCard card)
        {
            return;
        }

        var target = FindFolderTarget(card);
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

        Panel.SetZIndex(card, 0);
        _activeDropTarget?.SetDropTarget(false);
        SetTrashHover(false);

        if (IsOverTrash(card))
        {
            _activeDropTarget = null;
            DeleteCards([card]);
            return;
        }

        var target = _activeDropTarget ?? FindFolderTarget(card);
        _activeDropTarget = null;

        if (target is not null)
        {
            PlaceCardIntoFolder(card, target);
            return;
        }

        Save();
    }

    private ItemCard? FindFolderTarget(ItemCard draggedCard)
    {
        var center = draggedCard.TranslatePoint(
            new Point(draggedCard.ActualWidth / 2, draggedCard.ActualHeight / 2),
            WorkspaceCanvas);

        return WorkspaceCanvas.Children
            .OfType<ItemCard>()
            .Where(card => card != draggedCard)
            .Reverse()
            .FirstOrDefault(card =>
            {
                var rect = new Rect(Canvas.GetLeft(card), Canvas.GetTop(card), card.ActualWidth, card.ActualHeight);
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
        var folder = new ClipboardItem
        {
            Type = ClipboardItemType.AppFolder,
            DisplayName = NextFolderName(),
            X = Canvas.GetLeft(targetCard),
            Y = Canvas.GetTop(targetCard),
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
        var cardCenter = card.TranslatePoint(new Point(card.ActualWidth / 2, card.ActualHeight / 2), Root);
        var trashTopLeft = TrashZone.TranslatePoint(new Point(0, 0), Root);
        var trashRect = new Rect(trashTopLeft, new Size(TrashZone.ActualWidth, TrashZone.ActualHeight));
        return trashRect.Contains(cardCenter);
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
            TrashIcon.Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#F8FAFC" : "#475569")!;
            TrashLabel.Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#F8FAFC" : "#1F2937")!;
            return;
        }

        TrashZone.Background = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#331E2531" : "#33FFFFFF")!;
        TrashZone.BorderBrush = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#66748694" : "#CBD5E1")!;
        TrashIcon.Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#CBD5E1" : "#94A3B8")!;
        TrashLabel.Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#F8FAFC" : "#475569")!;
    }

    private void SelectSingle(ItemCard card)
    {
        ClearSelection();
        card.SetSelected(true);
        _selectedCards.Add(card);
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
        foreach (var card in cardList)
        {
            _items.Remove(card.Item);
            _selectedCards.Remove(card);
            WorkspaceCanvas.Children.Remove(card);
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
        _selectedCards.Clear();
        WorkspaceCanvas.Children.Clear();
        Save();
        _soundService.Deleted();
        ShowToast("Area de trabalho resetada");
    }

    private void Root_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == Root || e.OriginalSource == WorkspaceCanvas)
        {
            ClearSelection();
        }
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
        FolderOverlayTitle.Text = newName;
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
            FolderOverlayTitle.Text = _openFolder.DisplayName;
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

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryPane.Visibility == Visibility.Visible) { HideHistoryPanel(); return; }
        HistoryColumn.MinWidth = 300;
        HistoryColumn.MaxWidth = Math.Max(300, Math.Min(520, ActualWidth - 510));
        HistoryColumn.Width = new GridLength(Math.Min(_historyWidth, HistoryColumn.MaxWidth));
        HistoryPane.Visibility = Visibility.Visible;
        HistorySplitter.Visibility = Visibility.Visible;
        HistoryPane.FocusSearch();
    }

    private void HideHistoryPanel()
    {
        if (HistoryPane.Visibility == Visibility.Visible) _historyWidth = HistoryColumn.ActualWidth;
        HistoryPane.Visibility = Visibility.Collapsed;
        HistorySplitter.Visibility = Visibility.Collapsed;
        HistoryColumn.MinWidth = 0;
        HistoryColumn.Width = new GridLength(0);
        WorkspaceCanvas.Focus();
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
        var menu = new ContextMenu
        {
            Style = (Style)FindResource("WorkspaceContextMenuStyle"),
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };
        var itemStyle = (Style)FindResource("WorkspaceMenuItemStyle");
        foreach (var board in _workspaces)
        {
            var item = new MenuItem { Header = board.Name, IsCheckable = true, IsChecked = board.Id == _activeWorkspace.Id, Style = itemStyle };
            item.Click += (_, _) => SwitchWorkspace(board);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator { Style = (Style)FindResource("WorkspaceMenuSeparatorStyle") });
        var create = new MenuItem { Header = "+ Nova mesa", Style = itemStyle, Foreground = (Brush)FindResource("AccentBrush") };
        create.Click += (_, _) => CreateWorkspace();
        menu.Items.Add(create);
        var rename = new MenuItem { Header = "Renomear mesa atual…", Style = itemStyle };
        rename.Click += (_, _) => RenameWorkspace();
        menu.Items.Add(rename);
        menu.PlacementTarget = WorkspaceButton;
        menu.IsOpen = true;
    }

    private void CreateWorkspace()
    {
        var defaultName = $"Mesa {_workspaces.Count + 1}";
        var dialog = new RenameWindow(defaultName, "Nova mesa", "Nome da mesa"){ Owner = this };
        if (dialog.ShowDialog() != true) return;
        Save();
        var board = new WorkspaceBoard { Name = dialog.ItemName };
        _workspaces.Add(board);
        SwitchWorkspace(board, saveCurrent: false);
        ShowToast("Nova mesa criada");
    }

    private void RenameWorkspace()
    {
        var dialog = new RenameWindow(_activeWorkspace.Name, "Renomear mesa", "Nome da mesa") { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _activeWorkspace.Name = dialog.ItemName;
        Save();
        UpdateWorkspacePresentation();
    }

    private void SwitchWorkspace(WorkspaceBoard board, bool saveCurrent = true)
    {
        if (board.Id == _activeWorkspace.Id) return;
        if (saveCurrent) Save();
        _activeWorkspace = board;
        _items = board.Items;
        _undoStack.Clear();
        _redoStack.Clear();
        HideFolderOverlay();
        ClearSelection();
        RenderAllItems();
        UpdateUndoRedoButtons();
        UpdateWorkspacePresentation();
        EnsureWorkspaceExtent();
    }

    private void UpdateWorkspacePresentation()
    {
        if (WorkspaceNameText is not null) WorkspaceNameText.Text = _activeWorkspace.Name;
        Title = $"ClipDesk — {_activeWorkspace.Name}";
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

    private void EnsureWorkspaceExtent()
    {
        if (WorkspaceCanvas is null || WorkspaceScroll is null || _items is null) return;
        WorkspaceCanvas.Width = Math.Max(Math.Max(480, WorkspaceScroll.ActualWidth - 18), _items.Select(item => item.X + 156).DefaultIfEmpty(0).Max());
        WorkspaceCanvas.Height = Math.Max(Math.Max(360, WorkspaceScroll.ActualHeight - 18), _items.Select(item => item.Y + 172).DefaultIfEmpty(0).Max());
        WorkspaceEmptyState.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void UndoButton_Click(object sender, RoutedEventArgs e) => UndoLastChange();
    private void RedoButton_Click(object sender, RoutedEventArgs e) => RedoLastChange();

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _isDarkMode = !_isDarkMode;
        ApplyTheme();
        try { _storageService.SaveSettings(new AppSettings { IsDarkMode = _isDarkMode }); }
        catch (Exception ex) { ShowToast($"Não foi possível salvar o tema: {ex.Message}"); }
    }

    private void RegisterUndoSnapshot()
    {
        if (_isRestoringSnapshot)
        {
            return;
        }

        SaveCardPositionsToItems();
        var snapshot = CloneItems(_items);
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

        _redoStack.Push(CloneItems(_items));
        RestoreSnapshot(_undoStack.Pop());
        ShowToast("Alteracao desfeita");
    }

    private void RedoLastChange()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        _undoStack.Push(CloneItems(_items));
        RestoreSnapshot(_redoStack.Pop());
        ShowToast("Alteracao refeita");
    }

    private void RestoreSnapshot(List<ClipboardItem> snapshot)
    {
        _isRestoringSnapshot = true;
        HideFolderOverlay();
        _items.Clear();
        _items.AddRange(CloneItems(snapshot));
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

    private void ApplyTheme()
    {
        ThemeService.Apply(_isDarkMode);
        var workspace = _isDarkMode ? "#141820" : "#F4F4F2";
        var gridLine = _isDarkMode ? "#232B36" : "#E3E5E8";
        Root.Background = CreateGridBrush(workspace, gridLine);
        Background = (Brush)new BrushConverter().ConvertFromString(workspace)!;
        TopToolbar.Background = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#D81E2531" : "#B8FFFFFF")!;
        TopToolbar.BorderBrush = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#66505E74" : "#55FFFFFF")!;
        FolderOverlay.Background = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#C0141820" : "#B8F4F4F2")!;
        FolderPanel.Background = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#D82B3344" : "#B8DDEAFF")!;
        FolderPanel.BorderBrush = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#66505E74" : "#66FFFFFF")!;
        FolderOverlayTitle.Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#F8FAFC" : "#FAFFFFFF")!;
        FolderOverlayTitleEditor.Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#F8FAFC" : "#FAFFFFFF")!;
        FolderOverlayTitleEditor.Background = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#331E2531" : "#22FFFFFF")!;
        FolderOverlayTitleEditor.BorderBrush = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#99CBD5E1" : "#66FFFFFF")!;
        ApplyTrashTheme(_isTrashHovering);
        ThemeIcon.Text = _isDarkMode ? "\uE706" : "\uE708";

        foreach (var button in TopToolbar.FindVisualChildren<Button>())
        {
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            button.Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#F8FAFC" : "#25282D")!;
        }
        WorkspaceButton.Foreground = (Brush)new BrushConverter().ConvertFromString(_isDarkMode ? "#F8FAFC" : "#25282D")!;
        WorkspaceButton.Background = Brushes.Transparent;
        WorkspaceButton.BorderBrush = Brushes.Transparent;

        foreach (var card in WorkspaceCanvas.Children.OfType<ItemCard>())
        {
            card.ApplyTheme(_isDarkMode);
        }

        RenderOpenFolderItems();
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

    private void Save()
    {
        SaveCardPositionsToItems();
        _activeWorkspace.Items = _items;
        _activeWorkspace.UpdatedAt = DateTime.Now;
        _storageService.SaveWorkspaces(_workspaces);
        _storageService.SaveItems(_items);
        EnsureWorkspaceExtent();
    }

    private void SaveCardPositionsToItems()
    {
        foreach (var card in WorkspaceCanvas.Children.OfType<ItemCard>())
        {
            card.Item.X = Canvas.GetLeft(card);
            card.Item.Y = Canvas.GetTop(card);
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
