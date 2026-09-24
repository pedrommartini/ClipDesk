using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ClipDesk.Views;

namespace ClipDesk;

public partial class MainWindow
{
    private bool _initializingCustomization;
    private static readonly FontFamily MontserratFont = new("/ClipDesk;component/Resources/Fonts/#Montserrat");
    private static readonly FontFamily DefaultFont = new("Segoe UI Variable Text");

    private void InitializeCustomization()
    {
        _initializingCustomization = true;
        BoardBackgroundNewOption.IsChecked = _appearanceSettings.BoardBackground != "classic";
        BoardBackgroundClassicOption.IsChecked = _appearanceSettings.BoardBackground == "classic";
        InterfaceDefaultOption.IsChecked = _appearanceSettings.InterfaceFont != "montserrat";
        InterfaceMontserratOption.IsChecked = _appearanceSettings.InterfaceFont == "montserrat";
        BoardDefaultOption.IsChecked = _appearanceSettings.BoardFont != "montserrat";
        BoardMontserratOption.IsChecked = _appearanceSettings.BoardFont == "montserrat";
        _initializingCustomization = false;
        ApplyCustomizationFonts();
    }

    private void ApplyCustomizationFonts()
    {
        FontFamily = _appearanceSettings.InterfaceFont == "montserrat" ? MontserratFont : DefaultFont;
        var boardFont = _appearanceSettings.BoardFont == "montserrat" ? MontserratFont : DefaultFont;
        TextElement.SetFontFamily(WorkspaceCanvas, boardFont);
        BoardObjectView.BoardTextFontFamily = boardFont.Source;
        foreach (var view in WorkspaceCanvas.Children.OfType<BoardObjectView>()) view.InvalidateVisual();
    }

    private void BrandButton_Click(object sender, RoutedEventArgs e)
    {
        CustomizationOverlay.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    private void CloseCustomization_Click(object sender, RoutedEventArgs e)
    {
        CustomizationOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private void CustomizationOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, CustomizationOverlay))
            CustomizationOverlay.Visibility = Visibility.Collapsed;
    }

    private void CustomizationDialog_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void CustomizationOption_Checked(object sender, RoutedEventArgs e)
    {
        if (_initializingCustomization || _appearanceSettings is null) return;
        if (sender is not FrameworkElement { Tag: string tag }) return;
        var pair = tag.Split(':', 2);
        if (pair.Length != 2) return;
        switch (pair[0])
        {
            case "board": _appearanceSettings.BoardBackground = pair[1]; break;
            case "interface": _appearanceSettings.InterfaceFont = pair[1]; break;
            case "canvas": _appearanceSettings.BoardFont = pair[1]; break;
            default: return;
        }
        ApplyCustomizationFonts();
        WorkspaceCanvas.Background = _appearanceSettings.BoardBackground == "classic"
            ? CreateClassicBoardBrush(_isDarkMode) : CreateBoardBrush(_isDarkMode);
        SaveAppearanceSettings();
    }

    private static DrawingBrush CreateClassicBoardBrush(bool dark)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(dark ? Color.FromRgb(22, 29, 42) : Color.FromRgb(244, 244, 242)),
            null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
        var lines = new GeometryGroup();
        lines.Children.Add(new LineGeometry(new Point(32, 0), new Point(32, 32)));
        lines.Children.Add(new LineGeometry(new Point(0, 32), new Point(32, 32)));
        group.Children.Add(new GeometryDrawing(
            null, new Pen(new SolidColorBrush(dark ? Color.FromRgb(42, 53, 72) : Color.FromRgb(227, 229, 232)), 1), lines));
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 32, 32),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
        brush.Freeze();
        return brush;
    }
}
