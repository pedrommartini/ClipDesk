using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ClipDesk.Models;
using ClipDesk.Services;

namespace ClipDesk.Views;

public sealed partial class ItemCard : UserControl
{
    private readonly FileIconService _fileIconService;
    private Point _dragStart;
    private bool _isDragging;
    private bool _doubleClickStarted;

    public ItemCard(ClipboardItem item, FileIconService fileIconService, StorageService storageService)
    {
        InitializeComponent();
        Item = item;
        _fileIconService = fileIconService;
        Refresh(storageService);
    }

    public ClipboardItem Item { get; }
    public bool IsSelected { get; private set; }

    public event EventHandler? Selected;
    public event EventHandler? CopyRequested;
    public event EventHandler? OpenRequested;
    public event EventHandler? RenameRequested;
    public event EventHandler? DuplicateRequested;
    public event EventHandler? DeleteRequested;
    public event EventHandler? DetailsRequested;
    public event EventHandler<string>? TitleEdited;
    public event EventHandler? DragStarted;
    public event EventHandler? DragMoved;
    public event EventHandler? DragFinished;

    public void Refresh(StorageService storageService)
    {
        NameText.Text = Item.DisplayName;

        var visual = _fileIconService.GetVisual(Item);
        GlyphText.Text = visual.Glyph;
        GlyphText.FontFamily = visual.UseSymbolFont
            ? new FontFamily("Segoe MDL2 Assets")
            : new FontFamily("Segoe UI");
        TypeLabel.Text = visual.Label;
        AccentBack.Background = (SolidColorBrush)new BrushConverter().ConvertFromString(visual.Accent)!;
        Thumbnail.Visibility = Visibility.Collapsed;
        FolderPreview.Visibility = Visibility.Collapsed;
        IconPanel.Visibility = Visibility.Visible;

        if (Item.Type == ClipboardItemType.AppFolder)
        {
            AccentBack.Background = new LinearGradientBrush(
                Color.FromRgb(224, 239, 255),
                Color.FromRgb(189, 219, 255),
                45);
            IconPanel.Visibility = Visibility.Collapsed;
            FolderPreview.Visibility = Visibility.Visible;
            BuildFolderPreview();
        }
        else if (Item.Type == ClipboardItemType.Image)
        {
            var bitmap = storageService.LoadBitmap(Item.StoredFilePath);
            if (bitmap is not null)
            {
                Thumbnail.Fill = new ImageBrush(bitmap)
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                };
                Thumbnail.Visibility = Visibility.Visible;
                IconPanel.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void BuildFolderPreview()
    {
        FolderPreview.Children.Clear();

        foreach (var child in Item.Children.Take(9))
        {
            var visual = _fileIconService.GetVisual(child);
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(visual.Accent)!;
            var glyph = visual.UseSymbolFont ? visual.Glyph : visual.Glyph[..Math.Min(1, visual.Glyph.Length)];

            FolderPreview.Children.Add(new Border
            {
                Width = 18,
                Height = 18,
                Margin = new Thickness(2),
                Background = brush,
                CornerRadius = new CornerRadius(5),
                Child = new TextBlock
                {
                    Text = glyph,
                    FontFamily = visual.UseSymbolFont ? new FontFamily("Segoe MDL2 Assets") : new FontFamily("Segoe UI"),
                    FontSize = visual.UseSymbolFont ? 8 : 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                }
            });
        }

        while (FolderPreview.Children.Count < 9)
        {
            FolderPreview.Children.Add(new Border
            {
                Width = 18,
                Height = 18,
                Margin = new Thickness(2),
                Background = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                CornerRadius = new CornerRadius(5)
            });
        }
    }

    public void SetSelected(bool selected)
    {
        IsSelected = selected;
        SelectionRing.Opacity = selected ? 1 : 0;
        if (!selected)
        {
            HideQuickActions();
        }
    }

    public void ApplyTheme(bool isDarkMode)
    {
        NameText.Foreground = isDarkMode
            ? new SolidColorBrush(Color.FromRgb(248, 250, 252))
            : new SolidColorBrush(Color.FromRgb(52, 55, 61));
        NameEditor.Foreground = isDarkMode
            ? new SolidColorBrush(Color.FromRgb(248, 250, 252))
            : new SolidColorBrush(Color.FromRgb(37, 40, 45));
        NameEditor.Background = isDarkMode
            ? new SolidColorBrush(Color.FromArgb(230, 30, 37, 49))
            : new SolidColorBrush(Color.FromArgb(238, 255, 255, 255));
        QuickActions.Background = isDarkMode
            ? new SolidColorBrush(Color.FromArgb(235, 32, 38, 52))
            : new SolidColorBrush(Color.FromArgb(238, 255, 255, 255));
        QuickActions.BorderBrush = isDarkMode
            ? new SolidColorBrush(Color.FromArgb(100, 80, 94, 116))
            : new SolidColorBrush(Color.FromArgb(85, 255, 255, 255));

        var actionBrush = isDarkMode
            ? new SolidColorBrush(Color.FromRgb(248, 250, 252))
            : new SolidColorBrush(Color.FromRgb(52, 55, 61));
        foreach (var button in QuickActions.FindVisualChildren<Button>())
        {
            button.Foreground = actionBrush;
        }
    }

    public void SetDropTarget(bool active)
    {
        var animation = new DoubleAnimation(active ? 1 : 0, TimeSpan.FromMilliseconds(130))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        DropTargetRing.BeginAnimation(OpacityProperty, animation);
    }

    public void PlayPopIn()
    {
        RootScale.ScaleX = 0.78;
        RootScale.ScaleY = 0.78;
        Opacity = 0;

        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.28 };
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(360)) { EasingFunction = ease });
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(360)) { EasingFunction = ease });
    }

    public void PulseCopied()
    {
        var glow = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(700))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        CopiedGlow.BeginAnimation(OpacityProperty, glow);

        var grow = new DoubleAnimation(1.04, 1, TimeSpan.FromMilliseconds(180));
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Parent is not Canvas canvas)
        {
            return;
        }

        _dragStart = e.GetPosition(canvas);
        _isDragging = false;
        _doubleClickStarted = e.ClickCount > 1;
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
        if (!IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed || Parent is not Canvas canvas)
        {
            return;
        }

        var current = e.GetPosition(canvas);
        var delta = current - _dragStart;

        if (!_isDragging && Math.Abs(delta.X) + Math.Abs(delta.Y) < 5)
        {
            return;
        }

        if (!_isDragging)
        {
            _isDragging = true;
            DragStarted?.Invoke(this, EventArgs.Empty);
            AnimateLift(true);
        }

        var left = Canvas.GetLeft(this);
        var top = Canvas.GetTop(this);

        // Cards are free-positioned, but kept inside the visible workspace.
        Canvas.SetLeft(this, Math.Max(0, Math.Min(canvas.ActualWidth - ActualWidth, left + delta.X)));
        Canvas.SetTop(this, Math.Max(0, Math.Min(canvas.ActualHeight - ActualHeight, top + delta.Y)));
        _dragStart = current;
        DragMoved?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

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
        else if (!_doubleClickStarted)
        {
            CopyRequested?.Invoke(this, EventArgs.Empty);
        }

        _isDragging = false;
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
        if (NameEditor.Visibility != Visibility.Visible)
        {
            return;
        }

        var newName = NameEditor.Text.Trim();
        NameEditor.Visibility = Visibility.Collapsed;
        NameText.Visibility = Visibility.Visible;

        if (string.IsNullOrWhiteSpace(newName) || newName == Item.DisplayName)
        {
            return;
        }

        TitleEdited?.Invoke(this, newName);
    }

    private void CancelTitleEdit()
    {
        NameEditor.Visibility = Visibility.Collapsed;
        NameText.Visibility = Visibility.Visible;
    }

    private void NameEditor_LostFocus(object sender, RoutedEventArgs e) => CommitTitleEdit();

    private void NameEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitTitleEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelTitleEdit();
            e.Handled = true;
        }
    }

    private void ShowQuickActions()
    {
        QuickActions.IsHitTestVisible = true;
        QuickActionsScale.ScaleX = 0.86;
        QuickActionsScale.ScaleY = 0.86;
        QuickActions.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
        QuickActionsScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.18 }
        });
        QuickActionsScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.18 }
        });
    }

    private void HideQuickActions()
    {
        QuickActions.IsHitTestVisible = false;
        QuickActions.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
    }

    private void AnimateLift(bool lifted)
    {
        var duration = TimeSpan.FromMilliseconds(lifted ? 120 : 260);
        var scale = lifted ? 1.08 : 1;
        IEasingFunction easing = lifted
            ? new CubicEase { EasingMode = EasingMode.EaseOut }
            : new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.22 };

        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, duration) { EasingFunction = easing });
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, duration) { EasingFunction = easing });
        CardShadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(lifted ? 30 : 18, duration));
        CardShadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.ShadowDepthProperty, new DoubleAnimation(lifted ? 14 : 5, duration));
        CardShadow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, new DoubleAnimation(lifted ? 0.28 : 0.18, duration));
    }

    private void CopyMenu_Click(object sender, RoutedEventArgs e) => CopyRequested?.Invoke(this, EventArgs.Empty);
    private void RenameMenu_Click(object sender, RoutedEventArgs e) => RenameRequested?.Invoke(this, EventArgs.Empty);
    private void DuplicateMenu_Click(object sender, RoutedEventArgs e) => DuplicateRequested?.Invoke(this, EventArgs.Empty);
    private void DeleteMenu_Click(object sender, RoutedEventArgs e) => DeleteRequested?.Invoke(this, EventArgs.Empty);
    private void OpenMenu_Click(object sender, RoutedEventArgs e) => OpenRequested?.Invoke(this, EventArgs.Empty);
    private void DetailsMenu_Click(object sender, RoutedEventArgs e) => DetailsRequested?.Invoke(this, EventArgs.Empty);
    private void QuickCopy_Click(object sender, RoutedEventArgs e) => CopyRequested?.Invoke(this, EventArgs.Empty);
    private void QuickDuplicate_Click(object sender, RoutedEventArgs e) => DuplicateRequested?.Invoke(this, EventArgs.Empty);
    private void QuickDetails_Click(object sender, RoutedEventArgs e) => DetailsRequested?.Invoke(this, EventArgs.Empty);
    private void QuickDelete_Click(object sender, RoutedEventArgs e) => DeleteRequested?.Invoke(this, EventArgs.Empty);
}

internal static class ItemCardVisualTreeExtensions
{
    public static IEnumerable<T> FindVisualChildren<T>(this DependencyObject parent)
        where T : DependencyObject
    {
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
