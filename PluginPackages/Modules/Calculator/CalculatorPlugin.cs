using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ClipDesk.PluginSdk;
using ClipDesk.PluginSdk.Windows;

namespace ClipDesk.Plugin.Calculator;

public sealed class CalculatorPlugin : IWindowsPluginRenderer, IWindowsPlugin
{
    private static readonly string[][] Keys =
    [
        ["C", "±", "%", "÷"], ["7", "8", "9", "×"], ["4", "5", "6", "-"],
        ["1", "2", "3", "+"], ["0", ",", "back", "="]
    ];

    public FrameworkElement CreateBody(WindowsPluginViewContext context)
    {
        var body = new Grid { Margin = new Thickness(12, 10, 12, 12) };
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(.22, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(.78, GridUnitType.Star) });
        var display = new TextBlock
        {
            Text = context.State.GetString("display") ?? "0",
            FontSize = Math.Clamp(27 * context.Scale, 16, 80), FontWeight = FontWeights.SemiBold,
            Foreground = Color(context.IsDarkMode ? "#FAFBFF" : "#172033"),
            TextAlignment = TextAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        body.Children.Add(new Border
        {
            Background = Color(context.IsDarkMode ? "#A70B1320" : "#EEF2F7"),
            BorderBrush = Color(context.IsDarkMode ? "#344962" : "#D5DEE9"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11),
            Padding = new Thickness(11, 2, 11, 2), Margin = new Thickness(0, 0, 0, 9), Child = display
        });
        var keys = new UniformGrid { Rows = 5, Columns = 4 };
        foreach (var key in Keys.SelectMany(row => row))
        {
            var accent = key is "÷" or "×" or "-" or "+" or "=";
            var button = new Button
            {
                Content = new TextBlock
                {
                    Text = key == "back" ? "⌫" : key,
                    FontSize = Math.Clamp(13.5 * context.Scale, 10, 48), FontWeight = FontWeights.SemiBold,
                    Foreground = accent ? Contrast(context.AccentColor)
                        : Color(context.IsDarkMode ? "#E8EDF7" : "#263449"),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                },
                Margin = new Thickness(3.5),
                Background = Color(accent ? context.AccentColor : context.IsDarkMode ? "#293750" : "#E6EDF6"),
                BorderThickness = new Thickness(0), Padding = new Thickness(7, 3, 7, 3),
                Cursor = System.Windows.Input.Cursors.Hand, Tag = "plugin-interactive"
            };
            button.Click += async (_, e) =>
            {
                e.Handled = true;
                var result = await context.ExecuteAsync(new PluginCommand("press",
                    new Dictionary<string, string> { ["key"] = key }));
                display.Text = result.State.GetString("display") ?? "0";
            };
            keys.Children.Add(button);
        }
        Grid.SetRow(keys, 1); body.Children.Add(keys);
        return body;
    }

    public FrameworkElement CreateBody(PluginViewContext context) =>
        CreateBody(LegacyPluginAdapter.CreateContext(new CalculatorModule(), context));

    private static SolidColorBrush Color(string value) => new((Color)ColorConverter.ConvertFromString(value));

    private static SolidColorBrush Contrast(string color)
    {
        var parsed = (Color)ColorConverter.ConvertFromString(color);
        var luminance = (.2126 * parsed.R + .7152 * parsed.G + .0722 * parsed.B) / 255;
        return Color(luminance > .62 ? "#172033" : "#FFFFFF");
    }
}
