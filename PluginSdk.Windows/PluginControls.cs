using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace ClipDesk.PluginSdk.Windows;

/// <summary>A modern slider that can be clicked or dragged anywhere along its track.</summary>
public sealed class PluginSlider : Grid
{
    private readonly Border _fill;
    private readonly Border _thumb;
    private double _value;

    internal PluginSlider(WindowsPluginViewContext context, double minimum, double maximum, double value,
        Action<double>? changed, double step)
    {
        Minimum = minimum;
        Maximum = Math.Max(minimum, maximum);
        Step = Math.Max(0, step);
        Height = Math.Clamp(28 * context.Scale, 26, 56);
        MinWidth = 72;
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;
        Focusable = true;
        Tag = "plugin-interactive";

        var track = new Border
        {
            Height = Math.Clamp(4 * context.Scale, 3, 8),
            CornerRadius = new CornerRadius(99),
            Background = PluginControlBrushes.From(context.IsDarkMode ? "#2C3647" : "#E6EAF0"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(3, 0, 3, 0)
        };
        _fill = new Border
        {
            Height = track.Height,
            CornerRadius = new CornerRadius(99),
            Background = PluginControlBrushes.From(context.AccentColor, "#FF4B55"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = track.Margin
        };
        _thumb = new Border
        {
            Width = Math.Clamp(14 * context.Scale, 12, 25),
            Height = Math.Clamp(14 * context.Scale, 12, 25),
            CornerRadius = new CornerRadius(99),
            Background = Brushes.White,
            BorderBrush = PluginControlBrushes.From(context.AccentColor, "#FF4B55"),
            BorderThickness = new Thickness(Math.Clamp(2 * context.Scale, 1.5, 3)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = .18, Color = Colors.Black }
        };
        Children.Add(track);
        Children.Add(_fill);
        Children.Add(_thumb);

        ValueChanged += changed;
        SizeChanged += (_, _) => UpdateVisual();
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            Focus();
            CaptureMouse();
            InteractionStarted?.Invoke();
            SetFromPosition(e.GetPosition(this).X);
            e.Handled = true;
        };
        PreviewMouseMove += (_, e) =>
        {
            if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
            {
                SetFromPosition(e.GetPosition(this).X);
                e.Handled = true;
            }
        };
        PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (IsMouseCaptured) ReleaseMouseCapture();
            InteractionCompleted?.Invoke();
            e.Handled = true;
        };
        PreviewKeyDown += (_, e) =>
        {
            var increment = Step > 0 ? Step : Math.Max((Maximum - Minimum) / 100, .01);
            if (e.Key is Key.Left or Key.Down) Value -= increment;
            else if (e.Key is Key.Right or Key.Up) Value += increment;
            else if (e.Key == Key.Home) Value = Minimum;
            else if (e.Key == Key.End) Value = Maximum;
            else return;
            e.Handled = true;
        };
        context.LayoutChanged += () =>
        {
            Height = Math.Clamp(28 * context.Scale, 26, 56);
            _thumb.Width = _thumb.Height = Math.Clamp(14 * context.Scale, 12, 25);
            _thumb.BorderThickness = new Thickness(Math.Clamp(2 * context.Scale, 1.5, 3));
            track.Height = _fill.Height = Math.Clamp(4 * context.Scale, 3, 8);
            UpdateVisual();
        };
        _value = Coerce(value);
    }

    public double Minimum { get; }
    public double Maximum { get; }
    public double Step { get; }
    public event Action<double>? ValueChanged;
    public event Action? InteractionStarted;
    public event Action? InteractionCompleted;

    public double Value
    {
        get => _value;
        set
        {
            var next = Coerce(value);
            if (Math.Abs(next - _value) < .000001) return;
            _value = next;
            UpdateVisual();
            ValueChanged?.Invoke(next);
        }
    }

    private double Coerce(double value)
    {
        var bounded = Math.Clamp(value, Minimum, Maximum);
        if (Step <= 0) return bounded;
        return Math.Clamp(Minimum + Math.Round((bounded - Minimum) / Step) * Step, Minimum, Maximum);
    }

    private void SetFromPosition(double x)
    {
        var travel = Math.Max(1, ActualWidth - _thumb.ActualWidth);
        Value = Minimum + Math.Clamp((x - _thumb.ActualWidth / 2) / travel, 0, 1) * (Maximum - Minimum);
    }

    private void UpdateVisual()
    {
        var range = Math.Max(.000001, Maximum - Minimum);
        var ratio = Math.Clamp((Value - Minimum) / range, 0, 1);
        var thumbWidth = _thumb.ActualWidth > 0 ? _thumb.ActualWidth : _thumb.Width;
        var travel = Math.Max(0, ActualWidth - thumbWidth);
        _thumb.Margin = new Thickness(travel * ratio, 0, 0, 0);
        _fill.Width = Math.Max(0, thumbWidth / 2 + travel * ratio - _fill.Margin.Left);
    }
}

public static class PluginSliders
{
    public static PluginSlider Create(WindowsPluginViewContext context, double minimum, double maximum,
        double value, Action<double>? changed = null, double step = 0) =>
        new(context, minimum, maximum, value, changed, step);
}

/// <summary>One selectable segment. Label can be a letter or word; IconGlyph replaces it with an icon.</summary>
public sealed record PluginToggleOption(string Value, string Label, string? IconGlyph = null,
    string? IconFontFamily = null, string? ToolTip = null);

public sealed class PluginToggle : Border
{
    private readonly WindowsPluginViewContext _context;
    private readonly IReadOnlyList<PluginToggleOption> _options;
    private readonly IReadOnlyList<Button> _buttons;
    private readonly Action<string>? _changed;
    private string _selectedValue;

    internal PluginToggle(WindowsPluginViewContext context, IReadOnlyList<PluginToggleOption> options,
        string selectedValue, Action<string>? changed)
    {
        if (options.Count < 2) throw new ArgumentException("O toggle precisa de pelo menos duas opções.", nameof(options));
        _context = context;
        _options = options;
        _changed = changed;
        _selectedValue = options.Any(option => option.Value == selectedValue) ? selectedValue : options[0].Value;
        Tag = "plugin-interactive";
        Background = PluginControlBrushes.From(context.IsDarkMode ? "#172235" : "#E7EDF5");
        BorderBrush = PluginControlBrushes.From(context.IsDarkMode ? "#35465E" : "#C8D2E0");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(Math.Clamp(10 * context.Scale, 8, 22));
        Padding = new Thickness(2);

        var grid = new Grid();
        var buttons = new List<Button>(options.Count);
        for (var index = 0; index < options.Count; index++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var option = options[index];
            var text = new TextBlock
            {
                Text = option.IconGlyph ?? option.Label,
                FontFamily = option.IconGlyph is null ? new FontFamily("Segoe UI Variable Text")
                    : new FontFamily(option.IconFontFamily ?? "Segoe MDL2 Assets"),
                FontSize = Math.Clamp(13 * context.Scale, 11, 30),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var button = new Button
            {
                Content = text,
                ToolTip = option.ToolTip ?? option.Label,
                Tag = "plugin-interactive",
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(0),
                MinWidth = Math.Clamp(37 * context.Scale, 34, 100),
                MinHeight = Math.Clamp(33 * context.Scale, 30, 76),
                Padding = new Thickness(Math.Clamp(10 * context.Scale, 7, 26), 4, Math.Clamp(10 * context.Scale, 7, 26), 4),
                Template = SegmentTemplate(Math.Clamp(8 * context.Scale, 6, 18))
            };
            button.Click += (_, e) => { SelectedValue = option.Value; e.Handled = true; };
            Grid.SetColumn(button, index);
            grid.Children.Add(button);
            buttons.Add(button);
        }
        Child = grid;
        _buttons = buttons;
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is Button || FindParent<Button>(e.OriginalSource as DependencyObject) is not null) return;
            var index = Math.Max(0, _options.ToList().FindIndex(option => option.Value == SelectedValue));
            SelectedValue = _options[(index + 1) % _options.Count].Value;
            e.Handled = true;
        };
        context.LayoutChanged += ApplyResponsiveMetrics;
        UpdateSelection();
    }

    public string SelectedValue
    {
        get => _selectedValue;
        set
        {
            if (!_options.Any(option => option.Value == value) || value == _selectedValue) return;
            _selectedValue = value;
            UpdateSelection();
            _changed?.Invoke(value);
        }
    }

    private void ApplyResponsiveMetrics()
    {
        CornerRadius = new CornerRadius(Math.Clamp(10 * _context.Scale, 8, 22));
        for (var index = 0; index < _buttons.Count; index++)
        {
            var button = _buttons[index];
            button.MinWidth = Math.Clamp(37 * _context.Scale, 34, 100);
            button.MinHeight = Math.Clamp(33 * _context.Scale, 30, 76);
            button.Padding = new Thickness(Math.Clamp(10 * _context.Scale, 7, 26), 4, Math.Clamp(10 * _context.Scale, 7, 26), 4);
            button.Template = SegmentTemplate(Math.Clamp(8 * _context.Scale, 6, 18));
            if (button.Content is TextBlock text) text.FontSize = Math.Clamp(13 * _context.Scale, 11, 30);
        }
    }

    private void UpdateSelection()
    {
        var accent = PluginControlBrushes.From(_context.AccentColor, "#FF4B55");
        var selectedForeground = PluginControlBrushes.Contrast(_context.AccentColor);
        var neutral = PluginControlBrushes.From(_context.IsDarkMode ? "#A9B6C9" : "#56657A");
        for (var index = 0; index < _buttons.Count; index++)
        {
            var selected = _options[index].Value == _selectedValue;
            _buttons[index].Background = selected ? accent : Brushes.Transparent;
            _buttons[index].Foreground = selected ? selectedForeground : neutral;
        }
    }

    private static ControlTemplate SegmentTemplate(double radius)
    {
        var template = new ControlTemplate(typeof(Button));
        var surface = new FrameworkElementFactory(typeof(Border));
        surface.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        surface.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        surface.AppendChild(presenter);
        template.VisualTree = surface;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, .86));
        template.Triggers.Add(hover);
        return template;
    }

    private static T? FindParent<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }
}

public static class PluginToggles
{
    public static PluginToggle Create(WindowsPluginViewContext context, IEnumerable<PluginToggleOption> options,
        string selectedValue, Action<string>? changed = null) =>
        new(context, options.ToArray(), selectedValue, changed);
}

public sealed record PluginDropdownOption(string Value, string Label, string? SearchText = null,
    string? IconGlyph = null, string? IconFontFamily = null);

public static class PluginDropdowns
{
    public static Button Create(WindowsPluginViewContext context, IEnumerable<PluginDropdownOption> options,
        string selectedValue, Action<string> changed, string searchPlaceholder = "Pesquisar…")
    {
        var list = options.ToArray();
        var anchor = PluginButtons.Create(context, LabelFor(list, selectedValue) + "  ▾");
        anchor.Click += (_, e) =>
        {
            e.Handled = true;
            Show(anchor, context, list, selectedValue, value =>
            {
                selectedValue = value;
                if (anchor.Content is TextBlock text) text.Text = LabelFor(list, value) + "  ▾";
                changed(value);
            }, searchPlaceholder);
        };
        return anchor;
    }

    public static Popup Show(Button anchor, WindowsPluginViewContext context,
        IEnumerable<PluginDropdownOption> options, string selectedValue, Action<string> selected,
        string searchPlaceholder = "Pesquisar…")
    {
        var all = options.ToArray();
        var popup = new Popup
        {
            Placement = PlacementMode.Bottom,
            PlacementTarget = anchor,
            AllowsTransparency = true,
            StaysOpen = false,
            VerticalOffset = 7
        };
        var surface = new Border
        {
            Width = Math.Clamp(Math.Max(anchor.ActualWidth, 310 * context.Scale), 260, 460),
            MaxHeight = Math.Clamp(390 * context.Scale, 280, 560),
            Padding = new Thickness(9),
            CornerRadius = new CornerRadius(Math.Clamp(15 * context.Scale, 11, 26)),
            Background = PluginControlBrushes.From(context.IsDarkMode ? "#F51A2434" : "#FCFFFFFF"),
            BorderBrush = PluginControlBrushes.From(context.IsDarkMode ? "#52657E" : "#CCD6E3"),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 8, Opacity = .34, Color = Color.FromRgb(2, 6, 23) },
            Opacity = 0,
            RenderTransformOrigin = new Point(.5, 0),
            RenderTransform = new ScaleTransform(.97, .97),
            Tag = "plugin-interactive"
        };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var searchSurface = new Border
        {
            Margin = new Thickness(1, 1, 1, 8),
            Padding = new Thickness(11, 5, 11, 5),
            CornerRadius = new CornerRadius(10),
            Background = PluginControlBrushes.From(context.IsDarkMode ? "#263449" : "#EFF3F8"),
            BorderBrush = PluginControlBrushes.From(context.IsDarkMode ? "#3C4E67" : "#D5DDE8"),
            BorderThickness = new Thickness(1)
        };
        var searchGrid = new Grid();
        var placeholder = new TextBlock
        {
            Text = "⌕  " + searchPlaceholder,
            Foreground = PluginControlBrushes.From(context.IsDarkMode ? "#8998AC" : "#718096"),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            FontSize = Math.Clamp(13 * context.Scale, 12, 26)
        };
        var search = new TextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = PluginControlBrushes.From(context.IsDarkMode ? "#F2F6FF" : "#223047"),
            CaretBrush = PluginControlBrushes.From(context.AccentColor),
            FontSize = Math.Clamp(13 * context.Scale, 12, 26),
            Tag = "plugin-interactive"
        };
        searchGrid.Children.Add(placeholder);
        searchGrid.Children.Add(search);
        searchSurface.Child = searchGrid;
        layout.Children.Add(searchSurface);

        var rows = new StackPanel();
        var scroll = new ScrollViewer
        {
            Content = rows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = Math.Clamp(315 * context.Scale, 210, 450)
        };
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);
        surface.Child = layout;
        popup.Child = surface;

        var visibleButtons = new List<Button>();
        void Select(PluginDropdownOption option)
        {
            popup.IsOpen = false;
            selected(option.Value);
        }
        void RenderRows()
        {
            rows.Children.Clear();
            visibleButtons.Clear();
            var query = search.Text.Trim();
            var filtered = all.Where(option => query.Length == 0
                || option.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || option.Value.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (option.SearchText?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false)).ToArray();
            foreach (var option in filtered)
            {
                var selectedNow = option.Value == selectedValue;
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                if (option.IconGlyph is not null)
                {
                    var icon = new TextBlock
                    {
                        Text = option.IconGlyph,
                        FontFamily = new FontFamily(option.IconFontFamily ?? "Segoe MDL2 Assets"),
                        Width = 28,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = PluginControlBrushes.From(context.AccentColor)
                    };
                    row.Children.Add(icon);
                }
                var label = new TextBlock
                {
                    Text = option.Label,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = Math.Clamp(13 * context.Scale, 12, 27),
                    Foreground = PluginControlBrushes.From(context.IsDarkMode ? "#EEF3FB" : "#263449")
                };
                Grid.SetColumn(label, 1);
                row.Children.Add(label);
                if (selectedNow)
                {
                    var check = new TextBlock
                    {
                        Text = "✓",
                        FontWeight = FontWeights.Bold,
                        Foreground = PluginControlBrushes.From(context.AccentColor),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(12, 0, 4, 0)
                    };
                    Grid.SetColumn(check, 2);
                    row.Children.Add(check);
                }
                var button = new Button
                {
                    Content = row,
                    Tag = "plugin-interactive",
                    Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(10, 6, 10, 6),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Template = DropdownRowTemplate()
                };
                button.Background = selectedNow
                    ? PluginControlBrushes.From(context.IsDarkMode ? "#334159" : "#E6ECF5")
                    : Brushes.Transparent;
                button.Margin = new Thickness(0, 1, 0, 1);
                button.MinHeight = Math.Clamp(38 * context.Scale, 35, 80);
                button.Click += (_, e) => { e.Handled = true; Select(option); };
                rows.Children.Add(button);
                visibleButtons.Add(button);
            }
            if (filtered.Length == 0)
            {
                rows.Children.Add(new TextBlock
                {
                    Text = "Nenhuma opção encontrada",
                    Margin = new Thickness(12, 18, 12, 18),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = PluginControlBrushes.From(context.IsDarkMode ? "#91A0B5" : "#6B778B")
                });
            }
        }
        search.TextChanged += (_, _) => { placeholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; RenderRows(); };
        surface.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { popup.IsOpen = false; e.Handled = true; return; }
            if (e.Key == Key.Down && visibleButtons.Count > 0) { visibleButtons[0].Focus(); e.Handled = true; return; }
            if (e.Key == Key.Enter && search.IsKeyboardFocusWithin && visibleButtons.Count > 0)
            { visibleButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; }
        };
        popup.Opened += (_, _) =>
        {
            RenderRows();
            search.Focus();
            surface.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(115)));
            if (surface.RenderTransform is ScaleTransform scale)
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(145)) { EasingFunction = ease });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(145)) { EasingFunction = ease });
            }
        };
        anchor.Unloaded += (_, _) => popup.IsOpen = false;
        popup.IsOpen = true;
        return popup;
    }

    private static string LabelFor(IEnumerable<PluginDropdownOption> options, string value) =>
        options.FirstOrDefault(option => option.Value == value)?.Label ?? value;

    private static ControlTemplate DropdownRowTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var surface = new FrameworkElementFactory(typeof(Border));
        surface.Name = "Surface";
        surface.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        surface.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        surface.AppendChild(presenter);
        template.VisualTree = surface;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, .78));
        template.Triggers.Add(hover);
        return template;
    }
}

internal static class PluginControlBrushes
{
    public static Brush From(string value, string fallback = "#7C5CFC")
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); }
        catch (FormatException) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback)); }
        catch (NotSupportedException) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback)); }
    }

    public static Brush Contrast(string value)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(value);
            var luminance = (.2126 * color.R + .7152 * color.G + .0722 * color.B) / 255;
            return From(luminance > .62 ? "#172033" : "#FFFFFF");
        }
        catch (FormatException) { return Brushes.White; }
        catch (NotSupportedException) { return Brushes.White; }
    }
}
