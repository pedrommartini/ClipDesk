using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ClipDesk.Models;
using ClipDesk.Services;

namespace ClipDesk.Views;

public sealed partial class ItemCard : UserControl
{
    private readonly FileIconService _fileIconService;
    private readonly StorageService _storageService;
    private readonly PdfPreviewService _pdfPreviewService = new();
    private Point _dragStart;
    private bool _isDragging;
    private bool _isResizing;
    private bool _doubleClickStarted;
    private bool _isDarkMode;

    public ItemCard(ClipboardItem item, FileIconService fileIconService, StorageService storageService)
    {
        InitializeComponent();
        Item = item;
        _fileIconService = fileIconService;
        _storageService = storageService;

        if (item.Type == ClipboardItemType.AppFolder) { MinWidth = 300; MinHeight = 304; }
        if (IsPdf(item)) MinHeight = 260;
        var defaultSize = GetDefaultSize(item);
        Width = item.Width > 0 ? Math.Clamp(item.Width, MinWidth, MaxWidth) : defaultSize.Width;
        Height = item.Height > 0 ? Math.Clamp(item.Height, MinHeight, MaxHeight) : defaultSize.Height;
        item.Width = Width;
        item.Height = Height;
        SizeChanged += ItemCard_SizeChanged;
        PreviewSurface.SizeChanged += (_, _) => UpdateFolderPreviewLayout(false);
        Loaded += (_, _) => UpdateFolderPreviewLayout(animate: false);
        Refresh(storageService);
    }

    public ClipboardItem Item { get; }
    public bool IsSelected { get; private set; }
    public bool IsSelectionToggleRequested { get; private set; }
    public double WorkspaceScaleFactor { get; private set; } = 1;

    public event EventHandler? Selected;
    public event EventHandler? CopyRequested;
    public event EventHandler? OpenRequested;
    public event EventHandler? DuplicateRequested;
    public event EventHandler? DeleteRequested;
    public event EventHandler? DetailsRequested;
    public event EventHandler<string>? FolderCategoryRequested;
    public event EventHandler<string>? TitleEdited;
    public event EventHandler? DragStarted;
    public event EventHandler? DragMoved;
    public event EventHandler? DragFinished;
    public event EventHandler? ResizeStarted;
    public event EventHandler? ResizeMoved;
    public event EventHandler? ResizeFinished;

    private static Size GetDefaultSize(ClipboardItem item)
    {
        if (IsPdf(item)) return new Size(370, 420);
        return item.Type switch
        {
            ClipboardItemType.AppFolder => new Size(460, 304),
            ClipboardItemType.Image => new Size(350, 260),
            ClipboardItemType.Text => new Size(340, 220),
            ClipboardItemType.File or ClipboardItemType.Folder => new Size(330, 205),
            ClipboardItemType.Link => new Size(330, 190),
            _ => new Size(300, 190)
        };
    }

    public void Refresh(StorageService storageService)
    {
        NameText.Text = Item.DisplayName;
        var visual = _fileIconService.GetVisual(Item);
        var accent = BrushFrom(visual.Accent);

        HeaderIcon.Background = CreateAccentBrush((SolidColorBrush)accent);
        GlyphText.Text = visual.Glyph;
        GlyphText.FontFamily = visual.UseSymbolFont ? new FontFamily("Segoe MDL2 Assets") : new FontFamily("Segoe UI");
        GlyphText.FontSize = visual.UseSymbolFont ? 18 : 14;
        HeaderIcon.Background = Brushes.Transparent;
        HeaderIcon.Child = new ContentIcon { Kind = Item.Type == ClipboardItemType.AppFolder || Item.Type == ClipboardItemType.Folder
            ? "folder" : visual.Label == "PDF" ? "PDF" : FolderCategoryService.GetCategories(new[] { Item }).FirstOrDefault()?.Key ?? "document" };
        var isFolder = Item.Type == ClipboardItemType.AppFolder;
        var compactFile = Item.Type == ClipboardItemType.File && !HasVisualPreview(Item);
        HeaderLayout.Visibility = compactFile ? Visibility.Collapsed : Visibility.Visible;
        HeaderRow.Height = compactFile ? new GridLength(0) : new GridLength(isFolder ? 88 : 68);
        IconColumn.Width = new GridLength(isFolder ? 58 : 42);
        HeaderIcon.Width = HeaderIcon.Height = isFolder ? 56 : 38;
        FolderCount.Visibility = isFolder ? Visibility.Visible : Visibility.Collapsed;
        FolderCount.Text = $"{Item.Children.Count} {(Item.Children.Count == 1 ? "item" : "itens")}";
        TypeLabel.Text = visual.Label;
        GenericGlyph.Text = visual.Glyph;
        GenericGlyph.FontFamily = GlyphText.FontFamily;

        HideAllPreviews();
        switch (Item.Type)
        {
            case ClipboardItemType.Text:
                TextPreviewContent.Text = string.IsNullOrWhiteSpace(Item.Text) ? "Texto vazio" : Item.Text.Trim();
                TextPreview.Visibility = Visibility.Visible;
                break;
            case ClipboardItemType.Image:
                var bitmap = storageService.LoadBitmap(Item.StoredFilePath);
                if (bitmap is not null)
                {
                    ImagePreview.Source = bitmap;
                    ImagePreviewHost.Visibility = Visibility.Visible;
                }
                else
                {
                    GenericPreview.Visibility = Visibility.Visible;
                }
                break;
            case ClipboardItemType.Link:
                LinkPreviewContent.Text = Item.Url ?? Item.Text ?? Item.DisplayName;
                LinkPreview.Visibility = Visibility.Visible;
                break;
            case ClipboardItemType.File:
            case ClipboardItemType.Folder:
                if (Item.Type == ClipboardItemType.File && TryLoadFileImage(storageService) is { } fileBitmap)
                {
                    ImagePreview.Source = fileBitmap;
                    ImagePreviewHost.Visibility = Visibility.Visible;
                    break;
                }
                ConfigureFileFallback(visual, accent);
                if (Item.Type == ClipboardItemType.File && IsPdf(Item))
                    _ = LoadPdfPreviewAsync(Item.FilePaths.FirstOrDefault());
                break;
            case ClipboardItemType.AppFolder:
                BuildFolderPreview();
                FolderPreviewScroller.Visibility = Visibility.Visible;
                break;
            default:
                GenericPreview.Visibility = Visibility.Visible;
                break;
        }
    }

    private async Task LoadPdfPreviewAsync(string? path)
    {
        var bitmap = await _pdfPreviewService.RenderFirstPageAsync(path);
        if (bitmap is null || path != Item.FilePaths.FirstOrDefault()) return;
        ImagePreview.Source = bitmap;
        FilePreview.Visibility = Visibility.Collapsed;
        ImagePreviewHost.Visibility = Visibility.Visible;
    }

    private void ConfigureFileFallback(FileVisual visual, Brush accent)
    {
        FileBadge.Background = accent;
        FileBadgeText.Text = visual.Label;
        var fileName = Item.FilePaths.FirstOrDefault() is { } path
            ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar))
            : null;
        FilePreviewName.Text = string.IsNullOrWhiteSpace(fileName) ? Item.DisplayName : fileName;
        FilePreviewMeta.Text = BuildFileMetadata();
        FilePreview.Visibility = Visibility.Visible;
    }

    private static bool IsPdf(ClipboardItem item) =>
        item.Type == ClipboardItemType.File && string.Equals(Path.GetExtension(item.FilePaths.FirstOrDefault()), ".pdf", StringComparison.OrdinalIgnoreCase);

    private static bool HasVisualPreview(ClipboardItem item)
    {
        var extension = Path.GetExtension(item.FilePaths.FirstOrDefault() ?? string.Empty).ToLowerInvariant();
        return IsPdf(item) || extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp";
    }

    private System.Windows.Media.Imaging.BitmapImage? TryLoadFileImage(StorageService storageService)
    {
        var path = Item.FilePaths.FirstOrDefault();
        var extension = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
        return extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp"
            ? storageService.LoadBitmap(path)
            : null;
    }

    private void HideAllPreviews()
    {
        TextPreview.Visibility = Visibility.Collapsed;
        ImagePreviewHost.Visibility = Visibility.Collapsed;
        LinkPreview.Visibility = Visibility.Collapsed;
        FilePreview.Visibility = Visibility.Collapsed;
        FolderPreviewScroller.Visibility = Visibility.Collapsed;
        GenericPreview.Visibility = Visibility.Collapsed;
    }

    private string BuildFileMetadata()
    {
        if (Item.Type == ClipboardItemType.Folder)
        {
            return "Pasta do Windows";
        }

        var path = Item.FilePaths.FirstOrDefault();
        var extension = Path.GetExtension(path ?? string.Empty).TrimStart('.').ToUpperInvariant();
        var type = string.IsNullOrWhiteSpace(extension) ? "Arquivo" : extension;
        if (Item.FilePaths.Count > 1)
        {
            return $"{type}  •  {Item.FilePaths.Count} arquivos";
        }

        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                var bytes = new FileInfo(path).Length;
                return $"{type}  •  {FormatBytes(bytes)}";
            }
            catch
            {
                // The preview remains useful even if file metadata becomes unavailable.
            }
        }

        return type;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }

    private void BuildFolderPreview()
    {
        FolderPreview.Children.Clear();
        foreach (var category in FolderCategoryService.GetCategories(Item.Children))
        {
            var tile = new Border
            {
                Width = 118,
                Height = 82,
                Margin = new Thickness(4),
                Padding = new Thickness(7),
                Background = _isDarkMode ? new LinearGradientBrush(Color.FromArgb(220, 23, 28, 43), Color.FromArgb(205, 19, 25, 37), 90) : BrushFrom("#F7FFFFFF"),
                BorderBrush = _isDarkMode ? BrushFrom("#655F6488") : BrushFrom("#D7DFEA"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Cursor = Cursors.Hand,
                ToolTip = $"Ver {category.Items.Count} itens em {category.Name}",
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1)
            };
            tile.MouseEnter += (_, _) => AnimateCategoryTile(tile, 1.025, 130);
            tile.MouseLeave += (_, _) => AnimateCategoryTile(tile, 1, 170);
            tile.MouseLeftButtonDown += (_, eventArgs) =>
            {
                FolderCategoryRequested?.Invoke(this, category.Key);
                eventArgs.Handled = true;
            };
            // A fixed artwork composition scales uniformly inside the responsive tile.
            var layout = new StackPanel { Width = 86, Margin = new Thickness(3) };
            layout.Children.Add(new ContentIcon { Kind = category.Key == "images" ? "folder" : category.Key == "texts" ? "files" : category.Key,
                Width = 48, Height = 48, HorizontalAlignment = HorizontalAlignment.Center });
            layout.Children.Add(new TextBlock {
                Text = category.Name, Margin = new Thickness(0, 12, 0, 0),
                FontSize = 16, TextAlignment = TextAlignment.Center,
                Foreground = _isDarkMode ? BrushFrom("#F4F1FF") : BrushFrom("#273247")
            });
            layout.Children.Add(new TextBlock {
                Text = $"({category.Items.Count})", Margin = new Thickness(0, 3, 0, 0),
                FontSize = 15, TextAlignment = TextAlignment.Center,
                Foreground = _isDarkMode ? BrushFrom("#BEBBEA") : BrushFrom("#67528B")
            });
            tile.Child = new Viewbox { Child = layout, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both };
            FolderPreview.Children.Add(tile);
        }

        if (Item.Children.Count == 0)
        {
            FolderPreview.Children.Add(new TextBlock
            {
                Text = "Pasta vazia — arraste itens para cá",
                Margin = new Thickness(8, 12, 0, 0),
                Foreground = BrushFrom("#94A3B8"),
                FontSize = 12
            });
        }

        Dispatcher.BeginInvoke(() => UpdateFolderPreviewLayout(animate: false));
    }

    private void ItemCard_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Item.Type == ClipboardItemType.AppFolder) UpdateFolderPreviewLayout(animate: false);
    }

    private void UpdateFolderPreviewLayout(bool animate)
    {
        if (Item.Type != ClipboardItemType.AppFolder || FolderPreview.Children.Count == 0) return;
        var tiles = FolderPreview.Children.OfType<Border>().ToList();
        if (tiles.Count == 0) return;

        var availableWidth = Math.Max(1, PreviewSurface.ActualWidth);
        var availableHeight = Math.Max(1, PreviewSurface.ActualHeight);
        // Keep labels readable. Dense folders can scroll instead of shrinking to illegible icons.
        var columns = Math.Clamp((int)(availableWidth / 100), 1, tiles.Count);
        var rows = (int)Math.Ceiling(tiles.Count / (double)columns);
        var targetWidth = Math.Max(1, Math.Floor(availableWidth / columns) - 8);
        var targetHeight = Math.Max(82, Math.Floor(availableHeight / rows) - 8);

        for (var index = 0; index < tiles.Count; index++)
        {
            var tile = tiles[index];
            var oldWidth = tile.ActualWidth > 0 ? tile.ActualWidth : targetWidth;
            var oldHeight = tile.ActualHeight > 0 ? tile.ActualHeight : targetHeight;
            tile.Width = targetWidth;
            tile.Height = targetHeight;
            if (tile.Child is Viewbox view && view.Child is StackPanel composition)
            {
                var compact = targetHeight < 130;
                var icon = (ContentIcon)composition.Children[0];
                icon.Width = icon.Height = compact ? 28 : 56;
                var label = (TextBlock)composition.Children[1];
                label.FontSize = compact ? 13 : 18;
                label.Margin = new Thickness(0, compact ? 4 : 12, 0, 0);
                ((TextBlock)composition.Children[2]).FontSize = compact ? 12 : 16;
                view.StretchDirection = StretchDirection.DownOnly;
            }
            if (!animate) continue;

            var delay = TimeSpan.FromMilliseconds(index * 24);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            tile.BeginAnimation(WidthProperty, new DoubleAnimation(oldWidth, targetWidth, TimeSpan.FromMilliseconds(190)) { BeginTime = delay, EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            tile.BeginAnimation(HeightProperty, new DoubleAnimation(oldHeight, targetHeight, TimeSpan.FromMilliseconds(190)) { BeginTime = delay, EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            tile.Opacity = 0.72;
            tile.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { BeginTime = delay });
        }
    }

    private static void AnimateCategoryTile(Border tile, double scale, int durationMs)
    {
        if (tile.RenderTransform is not ScaleTransform transform) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = ease });
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = ease });
    }

    private static Brush CreateAccentBrush(SolidColorBrush accent)
    {
        var color = accent.Color;
        var lighter = Color.FromRgb(
            (byte)Math.Min(255, color.R + 34),
            (byte)Math.Min(255, color.G + 34),
            (byte)Math.Min(255, color.B + 34));
        return new LinearGradientBrush(lighter, color, 45);
    }

    private static SolidColorBrush BrushFrom(string color) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;

    public void SetWorkspaceZoom(double scale, bool animate = true)
    {
        var previous = WorkspaceScale.ScaleX;
        WorkspaceScaleFactor = Math.Clamp(scale, 0.35, 1);
        WorkspaceScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        WorkspaceScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        WorkspaceScale.ScaleX = WorkspaceScaleFactor;
        WorkspaceScale.ScaleY = WorkspaceScaleFactor;
        if (!animate)
        {
            return;
        }

        var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(210);
        WorkspaceScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(previous, WorkspaceScaleFactor, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        WorkspaceScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(previous, WorkspaceScaleFactor, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
    }

    public Rect GetVisualBounds(Visual ancestor)
    {
        try
        {
            return Root.TransformToAncestor(ancestor).TransformBounds(new Rect(Root.RenderSize));
        }
        catch (InvalidOperationException)
        {
            return Rect.Empty;
        }
    }

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        SelectionRing.BeginAnimation(OpacityProperty, new DoubleAnimation(selected ? 1 : 0, TimeSpan.FromMilliseconds(120)));
        ResizeThumb.Opacity = selected ? 1 : 0.48;
        if (!selected) HideQuickActions();
    }

    public void ApplyTheme(bool isDarkMode)
    {
        _isDarkMode = isDarkMode;
        CardShadowSurface.Background = isDarkMode
            ? new LinearGradientBrush(Color.FromArgb(221, 31, 35, 50), Color.FromArgb(203, 26, 30, 43), 115)
            : new LinearGradientBrush(Color.FromArgb(240, 255, 255, 255), Color.FromArgb(224, 240, 245, 251), 115);
        CardShadowSurface.BorderBrush = BrushFrom(isDarkMode ? "#687A94" : "#C4D0DE");
        if (Item.Type == ClipboardItemType.AppFolder)
        {
            CardShadowSurface.Background = isDarkMode
                ? new LinearGradientBrush(Color.FromArgb(222, 44, 34, 76), Color.FromArgb(210, 31, 30, 53), 110)
                : new LinearGradientBrush(BrushFrom("#F2EAFF").Color, BrushFrom("#E7DDF8").Color, 110);
            CardShadowSurface.BorderBrush = BrushFrom(isDarkMode ? "#9970EE" : "#B293DC");
        }
        FolderCount.Foreground = BrushFrom(isDarkMode ? "#C2BDF1" : "#70558C");
        PreviewSurface.Background = Brushes.Transparent;
        PreviewSurface.BorderBrush = Brushes.Transparent;
        NameText.Foreground = BrushFrom(isDarkMode ? "#F8FAFC" : "#1F2937");
        NameEditor.Foreground = BrushFrom(isDarkMode ? "#F8FAFC" : "#1F2937");
        NameEditor.Background = BrushFrom(isDarkMode ? "#F02A3345" : "#FAFFFFFF");
        TextPreviewContent.Foreground = BrushFrom(isDarkMode ? "#DCE6F3" : "#334155");
        LinkPreviewContent.Foreground = TextPreviewContent.Foreground;
        FilePreviewName.Foreground = NameText.Foreground;
        FilePreviewMeta.Foreground = BrushFrom(isDarkMode ? "#94A3B8" : "#64748B");
        TypeLabel.Foreground = FilePreviewMeta.Foreground;
        GenericGlyph.Foreground = FilePreviewMeta.Foreground;
        QuickActions.Background = BrushFrom(isDarkMode ? "#F4263040" : "#FAFFFFFF");
        QuickActions.BorderBrush = BrushFrom(isDarkMode ? "#56647A" : "#CBD5E1");
        var actionBrush = BrushFrom(isDarkMode ? "#F8FAFC" : "#334155");
        foreach (var button in QuickActions.FindVisualChildren<Button>()) button.Foreground = actionBrush;
        MoreButton.Foreground = actionBrush;
        BuildFolderPreviewIfVisible();
    }

    private void BuildFolderPreviewIfVisible()
    {
        if (Item.Type == ClipboardItemType.AppFolder) BuildFolderPreview();
    }

    public void SetDropTarget(bool active)
    {
        DropTargetRing.BeginAnimation(OpacityProperty, new DoubleAnimation(active ? 1 : 0, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
    }

    public void PlayPopIn()
    {
        RootScale.ScaleX = 0.9;
        RootScale.ScaleY = 0.9;
        Opacity = 0;
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.18 };
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
    }

    public void PlayWorkspaceEntrance(int index, bool isFirstVisit)
    {
        var delay = TimeSpan.FromMilliseconds(Math.Min(index, 12) * 38 + (isFirstVisit ? 80 : 0));
        var translate = new TranslateTransform(0, isFirstVisit ? 26 : 16);
        RenderTransform = translate;
        Opacity = 0;
        RootScale.ScaleX = 0.96;
        RootScale.ScaleY = 0.96;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)) { BeginTime = delay, EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(340)) { BeginTime = delay, EasingFunction = ease });
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(340)) { BeginTime = delay, EasingFunction = ease });
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(340)) { BeginTime = delay, EasingFunction = ease });
    }

    public Task PlayWorkspaceExit(int index)
    {
        IsHitTestVisible = false;
        var completion = new TaskCompletionSource();
        var translate = new TranslateTransform();
        RenderTransform = translate;
        var delay = TimeSpan.FromMilliseconds(Math.Min(index, 10) * 18);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(170)) { BeginTime = delay, EasingFunction = ease };
        fade.Completed += (_, _) => completion.TrySetResult();
        BeginAnimation(OpacityProperty, fade);
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-14, TimeSpan.FromMilliseconds(190)) { BeginTime = delay, EasingFunction = ease });
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.975, TimeSpan.FromMilliseconds(190)) { BeginTime = delay, EasingFunction = ease });
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.975, TimeSpan.FromMilliseconds(190)) { BeginTime = delay, EasingFunction = ease });
        return completion.Task;
    }

    public void PlayDismiss(Action completed)
    {
        IsHitTestVisible = false;
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(190)) { EasingFunction = ease };
        fade.Completed += (_, _) => completed();
        BeginAnimation(OpacityProperty, fade);
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.9, TimeSpan.FromMilliseconds(190)) { EasingFunction = ease });
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.9, TimeSpan.FromMilliseconds(190)) { EasingFunction = ease });
    }

    public void PulseCopied()
    {
        CopiedGlow.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(650))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.025, 1, TimeSpan.FromMilliseconds(180)));
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.025, 1, TimeSpan.FromMilliseconds(180)));
    }

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isResizing || Parent is not Canvas canvas) return;
        _dragStart = e.GetPosition(canvas);
        _isDragging = false;
        _doubleClickStarted = e.ClickCount > 1;
        IsSelectionToggleRequested = Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        Selected?.Invoke(this, EventArgs.Empty);
        CaptureMouse();
        if (_doubleClickStarted)
        {
            OpenRequested?.Invoke(this, EventArgs.Empty);
            ReleaseMouseCapture();
        }
        e.Handled = true;
    }

    private void Card_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isResizing || !IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed || Parent is not Canvas canvas) return;
        var current = e.GetPosition(canvas);
        var delta = current - _dragStart;
        if (!_isDragging && Math.Abs(delta.X) + Math.Abs(delta.Y) < 5) return;
        if (!_isDragging)
        {
            _isDragging = true;
            DragStarted?.Invoke(this, EventArgs.Empty);
            AnimateLift(true);
        }
        var left = Canvas.GetLeft(this);
        var top = Canvas.GetTop(this);
        var visualWidth = ActualWidth * WorkspaceScaleFactor;
        var visualHeight = ActualHeight * WorkspaceScaleFactor;
        Canvas.SetLeft(this, Math.Max(0, Math.Min(canvas.ActualWidth - visualWidth, left + delta.X)));
        Canvas.SetTop(this, Math.Max(0, Math.Min(canvas.ActualHeight - visualHeight, top + delta.Y)));
        _dragStart = current;
        DragMoved?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isResizing) return;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (Parent is Canvas)
        {
            Item.X = Canvas.GetLeft(this);
            Item.Y = Canvas.GetTop(this);
        }
        if (_isDragging)
        {
            AnimateLift(false);
            DragFinished?.Invoke(this, EventArgs.Empty);
        }
        else if (!_doubleClickStarted && !IsSelectionToggleRequested)
        {
            CopyRequested?.Invoke(this, EventArgs.Empty);
        }
        _isDragging = false;
        e.Handled = true;
    }

    private void ResizeThumb_DragStarted(object sender, DragStartedEventArgs e)
    {
        _isResizing = true;
        Selected?.Invoke(this, EventArgs.Empty);
        ResizeStarted?.Invoke(this, EventArgs.Empty);
        Panel.SetZIndex(this, 20);
        e.Handled = true;
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Clamp(ActualWidth + e.HorizontalChange, MinWidth, MaxWidth);
        Height = Math.Clamp(ActualHeight + e.VerticalChange, MinHeight, MaxHeight);
        Item.Width = Width;
        Item.Height = Height;
        ResizeMoved?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        Item.Width = Width;
        Item.Height = Height;
        _isResizing = false;
        Panel.SetZIndex(this, 0);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.992, 1, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease });
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.992, 1, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease });
        UpdateFolderPreviewLayout(animate: true);
        ResizeFinished?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void Card_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        Selected?.Invoke(this, EventArgs.Empty);
        ShowQuickActions();
        e.Handled = true;
    }

    private void NameText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            BeginTitleEdit();
            e.Handled = true;
        }
    }

    private void BeginTitleEdit()
    {
        HideQuickActions();
        NameEditor.Text = Item.DisplayName;
        NameText.Visibility = Visibility.Collapsed;
        NameEditor.Visibility = Visibility.Visible;
        NameEditor.SelectAll();
        NameEditor.Focus();
    }

    private void CommitTitleEdit()
    {
        if (NameEditor.Visibility != Visibility.Visible) return;
        var newName = NameEditor.Text.Trim();
        NameEditor.Visibility = Visibility.Collapsed;
        NameText.Visibility = Visibility.Visible;
        if (!string.IsNullOrWhiteSpace(newName) && newName != Item.DisplayName) TitleEdited?.Invoke(this, newName);
    }

    private void CancelTitleEdit()
    {
        NameEditor.Visibility = Visibility.Collapsed;
        NameText.Visibility = Visibility.Visible;
    }

    private void NameEditor_LostFocus(object sender, RoutedEventArgs e) => CommitTitleEdit();
    private void NameEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitTitleEdit(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelTitleEdit(); e.Handled = true; }
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (QuickActions.Opacity > 0.5) HideQuickActions(); else ShowQuickActions();
        e.Handled = true;
    }

    private void ShowQuickActions()
    {
        QuickActions.IsHitTestVisible = true;
        QuickActionsScale.ScaleX = 0.86;
        QuickActionsScale.ScaleY = 0.86;
        QuickActions.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(130)));
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.14 };
        QuickActionsScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
        QuickActionsScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
    }

    private void HideQuickActions()
    {
        QuickActions.IsHitTestVisible = false;
        QuickActions.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(110)));
    }

    private void AnimateLift(bool lifted)
    {
        var duration = TimeSpan.FromMilliseconds(lifted ? 120 : 240);
        var scale = lifted ? 1.025 : 1;
        IEasingFunction easing = lifted ? new CubicEase { EasingMode = EasingMode.EaseOut } : new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.12 };
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, duration) { EasingFunction = easing });
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, duration) { EasingFunction = easing });
        CardShadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(lifted ? 34 : 22, duration));
        CardShadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.ShadowDepthProperty, new DoubleAnimation(lifted ? 14 : 7, duration));
    }

    private void QuickCopy_Click(object sender, RoutedEventArgs e) { CopyRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
    private void QuickDuplicate_Click(object sender, RoutedEventArgs e) { DuplicateRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
    private void QuickDetails_Click(object sender, RoutedEventArgs e) { DetailsRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
    private void QuickDelete_Click(object sender, RoutedEventArgs e) { DeleteRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; }
}

internal static class ItemCardVisualTreeExtensions
{
    public static IEnumerable<T> FindVisualChildren<T>(this DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typedChild) yield return typedChild;
            foreach (var descendant in child.FindVisualChildren<T>()) yield return descendant;
        }
    }
}
