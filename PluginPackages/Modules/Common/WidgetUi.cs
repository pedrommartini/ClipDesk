using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Globalization;
using ClipDesk.PluginSdk.Windows;

namespace ClipDesk.Plugin.Modules;

internal static class WidgetUi
{
    public static Brush Brush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    public static double Size(PluginViewContext context, double size) => Math.Clamp(size * context.Scale, 10, 100);
    public static double Size(WindowsPluginViewContext context, double size) => Math.Clamp(size * context.Scale, 10, 100);

    public static TextBlock Text(PluginViewContext context, string value, double size = 14, bool muted = false)
        => new()
        {
            Text = value, FontSize = Size(context, size), Tag = $"plugin-font:{size.ToString(CultureInfo.InvariantCulture)}",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(muted ? context.IsDarkMode ? "#94A3B8" : "#64748B" :
                context.IsDarkMode ? "#EEF4FF" : "#233149"), VerticalAlignment = VerticalAlignment.Center
        };

    public static TextBlock Text(WindowsPluginViewContext context, string value, double size = 14, bool muted = false)
        => new()
        {
            Text = value, FontSize = Size(context, size), Tag = $"plugin-font:{size.ToString(CultureInfo.InvariantCulture)}",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(muted ? context.IsDarkMode ? "#94A3B8" : "#64748B" :
                context.IsDarkMode ? "#EEF4FF" : "#233149"), VerticalAlignment = VerticalAlignment.Center
        };

    public static TextBox Input(PluginViewContext context, string value, double size = 14, bool multiline = false)
        => new()
        {
            Text = value, Tag = $"plugin-font:{size.ToString(CultureInfo.InvariantCulture)}", FontSize = Size(context, size),
            Foreground = Brush(context.IsDarkMode ? "#F2F6FF" : "#243247"),
            Background = Brush(context.IsDarkMode ? "#202D40" : "#F0F4F9"),
            BorderBrush = Brush(context.IsDarkMode ? "#3D5068" : "#CFD9E6"),
            BorderThickness = new Thickness(1), Padding = new Thickness(9, 5, 9, 5),
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            AcceptsReturn = multiline, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled
        };

    public static TextBox Input(WindowsPluginViewContext context, string value, double size = 14, bool multiline = false)
        => new()
        {
            Text = value, Tag = $"plugin-font:{size.ToString(CultureInfo.InvariantCulture)}", FontSize = Size(context, size),
            Foreground = Brush(context.IsDarkMode ? "#F2F6FF" : "#243247"),
            Background = Brush(context.IsDarkMode ? "#202D40" : "#F0F4F9"),
            BorderBrush = Brush(context.IsDarkMode ? "#3D5068" : "#CFD9E6"),
            BorderThickness = new Thickness(1), Padding = new Thickness(9, 5, 9, 5),
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            AcceptsReturn = multiline, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled
        };

    public static Button Button(PluginViewContext context, string value, bool accent = false)
        => new()
        {
            Content = Text(context, value, 13), Tag = "plugin-interactive", Cursor = Cursors.Hand,
            Background = Brush(accent ? context.IsDarkMode ? "#345B8D" : "#DCE9F8" :
                context.IsDarkMode ? "#29384D" : "#E9EFF6"),
            BorderThickness = new Thickness(0), Padding = new Thickness(8, 4, 8, 4),
            MinHeight = Math.Clamp(29 * context.Scale, 27, 90)
        };

    public static Button Button(WindowsPluginViewContext context, string value, bool accent = false)
    {
        var content = Text(context, value, 13);
        if (accent) content.Foreground = ContrastBrush(context.AccentColor);
        return new Button
        {
            Content = content, Tag = "plugin-interactive", Cursor = Cursors.Hand,
            Background = Brush(accent ? context.AccentColor : context.IsDarkMode ? "#29384D" : "#E9EFF6"),
            BorderThickness = new Thickness(0), Padding = new Thickness(8, 4, 8, 4),
            MinHeight = Math.Clamp(29 * context.Scale, 27, 90)
        };
    }

    public static Brush ContrastBrush(string color)
    {
        try
        {
            var parsed = (Color)ColorConverter.ConvertFromString(color);
            var luminance = (.2126 * parsed.R + .7152 * parsed.G + .0722 * parsed.B) / 255;
            return Brush(luminance > .62 ? "#172033" : "#FFFFFF");
        }
        catch (FormatException) { return Brushes.White; }
        catch (NotSupportedException) { return Brushes.White; }
    }

    public static Grid ChoiceRow(PluginViewContext context, string leftLabel, string rightLabel,
        out Button left, out Button right, out Button swap)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        left = Button(context, leftLabel + "  ▾"); right = Button(context, rightLabel + "  ▾");
        swap = Button(context, "↔"); swap.Margin = new Thickness(5, 0, 5, 0);
        left.HorizontalContentAlignment = HorizontalAlignment.Center;
        right.HorizontalContentAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(right, 2); Grid.SetColumn(swap, 1);
        row.Children.Add(left); row.Children.Add(swap); row.Children.Add(right);
        return row;
    }

    public static Grid ChoiceRow(WindowsPluginViewContext context, string leftLabel, string rightLabel,
        out Button left, out Button right, out Button swap)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        left = Button(context, leftLabel + "  ▾"); right = Button(context, rightLabel + "  ▾");
        swap = Button(context, "↔", true); swap.Margin = new Thickness(5, 0, 5, 0);
        left.HorizontalContentAlignment = HorizontalAlignment.Center;
        right.HorizontalContentAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(right, 2); Grid.SetColumn(swap, 1);
        row.Children.Add(left); row.Children.Add(swap); row.Children.Add(right);
        return row;
    }

    public static void ShowChoices(Button anchor, IEnumerable<(string Code, string Label)> choices,
        string selected, Action<string> select, PluginViewContext context)
    {
        var menu = new ContextMenu { MaxHeight = 360, Tag = "plugin-interactive" };
        foreach (var (code, label) in choices)
        {
            var item = new MenuItem { Header = label, IsChecked = code.Equals(selected, StringComparison.OrdinalIgnoreCase),
                Tag = "plugin-interactive", FontSize = Size(context, 13) };
            item.Click += (_, e) => { e.Handled = true; select(code); };
            menu.Items.Add(item);
        }
        anchor.ContextMenu = menu;
        menu.PlacementTarget = anchor;
        menu.IsOpen = true;
    }

    public static void ShowChoices(Button anchor, IEnumerable<(string Code, string Label)> choices,
        string selected, Action<string> select, WindowsPluginViewContext context)
    {
        var menu = new ContextMenu { MaxHeight = 360, Tag = "plugin-interactive" };
        foreach (var (code, label) in choices)
        {
            var item = new MenuItem { Header = label, IsChecked = code.Equals(selected, StringComparison.OrdinalIgnoreCase),
                Tag = "plugin-interactive", FontSize = Size(context, 13) };
            item.Click += (_, e) => { e.Handled = true; select(code); };
            menu.Items.Add(item);
        }
        anchor.ContextMenu = menu; menu.PlacementTarget = anchor; menu.IsOpen = true;
    }
}
