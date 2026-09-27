using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using ClipDesk.PluginSdk.Windows;

namespace ClipDesk.Plugin.ImageUpscaler.UI;

/// <summary>
/// Overlay interativo de comparativo Antes/Depois com divisor deslizante (split-slider).
/// Permite ao usuário arrastar a barra divisória para comparar a nitidez entre a imagem original e a ampliada.
/// </summary>
public sealed class BeforeAfterSplitOverlay : Grid
{
    private readonly Action _onClose;
    private readonly Image _originalImage;
    private readonly Image _upscaledImage;
    private readonly RectangleGeometry _clipGeometry;
    private readonly Border _dividerLine;
    private readonly Border _dividerThumb;
    private double _splitRatio = 0.5; // 50% inicial
    private bool _isDragging;

    public BeforeAfterSplitOverlay(
        WindowsPluginViewContext context,
        BitmapSource originalBitmap,
        BitmapSource upscaledBitmap,
        string originalDimensions,
        string upscaledDimensions,
        Action onClose)
    {
        _onClose = onClose;
        Tag = "plugin-interactive";

        bool dark = context.IsDarkMode;
        double scale = Math.Max(1.0, context.Scale);

        Background = dark ? UiStyles.GetBrush("#F20F172A") : UiStyles.GetBrush("#F8FFFFFF");

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Split View
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer info

        // =========================================================================
        // HEADER
        // =========================================================================
        var header = new Grid
        {
            Margin = new Thickness(12 * scale, 8 * scale, 12 * scale, 6 * scale)
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var iconText = new TextBlock
        {
            Text = "\uE790", // Magic Wand / Enhance
            FontFamily = UiStyles.IconFont,
            FontSize = 16 * scale,
            Foreground = UiStyles.GetBrush(context.AccentColor),
            Margin = new Thickness(0, 0, 8 * scale, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        titleStack.Children.Add(iconText);

        var titleBlock = new TextBlock
        {
            Text = "Comparativo Antes / Depois",
            FontFamily = UiStyles.TextFont,
            FontWeight = FontWeights.Bold,
            FontSize = 14 * scale,
            Foreground = dark ? UiStyles.GetBrush("#F8FAFC") : UiStyles.GetBrush("#0F172A"),
            VerticalAlignment = VerticalAlignment.Center
        };
        titleStack.Children.Add(titleBlock);
        Grid.SetColumn(titleStack, 0);
        header.Children.Add(titleStack);

        var closeBtn = new Button
        {
            Content = "✕ Fechar",
            FontFamily = UiStyles.TextFont,
            FontSize = 12 * scale,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(10 * scale, 4 * scale, 10 * scale, 4 * scale),
            Background = dark ? UiStyles.GetBrush("#334155") : UiStyles.GetBrush("#E2E8F0"),
            Foreground = dark ? UiStyles.GetBrush("#F8FAFC") : UiStyles.GetBrush("#0F172A"),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Tag = "plugin-interactive"
        };
        closeBtn.Click += (_, _) => _onClose();
        Grid.SetColumn(closeBtn, 1);
        header.Children.Add(closeBtn);

        SetRow(header, 0);
        Children.Add(header);

        // =========================================================================
        // SPLIT VIEW CANVAS
        // =========================================================================
        var canvas = new Grid
        {
            Margin = new Thickness(10 * scale, 4 * scale, 10 * scale, 6 * scale),
            ClipToBounds = true,
            Background = dark ? UiStyles.GetBrush("#090D16") : UiStyles.GetBrush("#E2E8F0"),
            Cursor = Cursors.SizeWE
        };

        // Camada 1: Imagem Ampliada (Base / Direita)
        _upscaledImage = new Image
        {
            Source = upscaledBitmap,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        RenderOptions.SetBitmapScalingMode(_upscaledImage, BitmapScalingMode.HighQuality);
        canvas.Children.Add(_upscaledImage);

        // Camada 2: Imagem Original (Esquerda) com recorte
        _clipGeometry = new RectangleGeometry();
        _originalImage = new Image
        {
            Source = originalBitmap,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Clip = _clipGeometry
        };
        RenderOptions.SetBitmapScalingMode(_originalImage, BitmapScalingMode.HighQuality);
        canvas.Children.Add(_originalImage);

        // Camada 3: Linha Divisória
        _dividerLine = new Border
        {
            Width = 2.5 * scale,
            Background = UiStyles.GetBrush(context.AccentColor),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false
        };
        canvas.Children.Add(_dividerLine);

        // Camada 4: Handle do Slider central
        _dividerThumb = new Border
        {
            Width = 32 * scale,
            Height = 32 * scale,
            CornerRadius = new CornerRadius(16 * scale),
            Background = UiStyles.GetBrush(context.AccentColor),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect
            {
                BlurRadius = 8,
                ShadowDepth = 1,
                Opacity = 0.45,
                Color = Colors.Black
            }
        };
        var arrowsText = new TextBlock
        {
            Text = "◀ ▶",
            FontFamily = UiStyles.TextFont,
            FontSize = 10 * scale,
            FontWeight = FontWeights.Bold,
            Foreground = UiStyles.GetContrastBrush(context.AccentColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _dividerThumb.Child = arrowsText;
        canvas.Children.Add(_dividerThumb);

        // Interação de arrasto do divisor
        canvas.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _isDragging = true;
            canvas.CaptureMouse();
            UpdateSplitFromPosition(e.GetPosition(canvas).X, canvas.ActualWidth);
            e.Handled = true;
        };

        canvas.PreviewMouseMove += (_, e) =>
        {
            if (_isDragging)
            {
                UpdateSplitFromPosition(e.GetPosition(canvas).X, canvas.ActualWidth);
                e.Handled = true;
            }
        };

        canvas.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (_isDragging)
            {
                _isDragging = false;
                canvas.ReleaseMouseCapture();
                e.Handled = true;
            }
        };

        canvas.SizeChanged += (_, _) => UpdateSplitPositions(canvas.ActualWidth, canvas.ActualHeight);

        SetRow(canvas, 1);
        Children.Add(canvas);

        // =========================================================================
        // FOOTER / LABELS
        // =========================================================================
        var footer = new Grid
        {
            Margin = new Thickness(12 * scale, 4 * scale, 12 * scale, 8 * scale)
        };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var leftBadge = new TextBlock
        {
            Text = $"◀ Original ({originalDimensions})",
            FontFamily = UiStyles.TextFont,
            FontSize = 11 * scale,
            FontWeight = FontWeights.SemiBold,
            Foreground = dark ? UiStyles.GetBrush("#94A3B8") : UiStyles.GetBrush("#64748B"),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        Grid.SetColumn(leftBadge, 0);
        footer.Children.Add(leftBadge);

        var rightBadge = new TextBlock
        {
            Text = $"Ampliado ({upscaledDimensions}) ▶",
            FontFamily = UiStyles.TextFont,
            FontSize = 11 * scale,
            FontWeight = FontWeights.Bold,
            Foreground = UiStyles.GetBrush(context.AccentColor),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(rightBadge, 1);
        footer.Children.Add(rightBadge);

        SetRow(footer, 2);
        Children.Add(footer);
    }

    private void UpdateSplitFromPosition(double mouseX, double actualWidth)
    {
        if (actualWidth <= 0) return;
        _splitRatio = Math.Clamp(mouseX / actualWidth, 0.02, 0.98);
        UpdateSplitPositions(actualWidth, ActualHeight);
    }

    private void UpdateSplitPositions(double width, double height)
    {
        if (width <= 0 || height <= 0) return;

        double splitX = width * _splitRatio;

        _clipGeometry.Rect = new Rect(0, 0, splitX, height);
        _dividerLine.Margin = new Thickness(splitX - (_dividerLine.Width / 2.0), 0, 0, 0);
        _dividerThumb.Margin = new Thickness(splitX - (_dividerThumb.Width / 2.0), 0, 0, 0);
    }
}
