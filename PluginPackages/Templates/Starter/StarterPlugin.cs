using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClipDesk.PluginSdk;
using ClipDesk.PluginSdk.Windows;

namespace ClipDesk.Plugin.Starter;

public sealed class StarterPlugin : IWindowsPluginRenderer
{
    public FrameworkElement CreateBody(WindowsPluginViewContext context)
    {
        var foreground = GetBrush(context.IsDarkMode ? "#F3F6FA" : "#182230");
        var muted = GetBrush(context.IsDarkMode ? "#A9B4C3" : "#66758A");
        var surface = GetBrush(context.IsDarkMode ? "#202936" : "#F5F7FA");
        var accent = GetBrush(context.AccentColor, "#7C5CFC");
        var accentForeground = GetContrastBrush(context.AccentColor);

        var body = new Grid { Margin = new Thickness(14, 10, 14, 13) };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        body.Children.Add(new TextBlock
        {
            Text = "Conteúdo",
            Foreground = muted,
            FontSize = Math.Clamp(12 * context.Scale, 11, 24),
            Margin = new Thickness(0, 0, 0, 7)
        });

        if (context.IsEditing)
        {
            AddEditor(context, body, foreground, surface, accent, accentForeground);
        }
        else
        {
            AddViewer(context, body, foreground, accent, accentForeground);
        }

        return body;
    }

    private static void AddEditor(
        WindowsPluginViewContext context,
        Grid body,
        Brush foreground,
        Brush surface,
        Brush accent,
        Brush accentForeground)
    {
        var input = new TextBox
        {
            Text = context.State.GetString("text") ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(10, 8, 10, 8),
            Foreground = foreground,
            Background = surface,
            BorderBrush = accent,
            BorderThickness = new Thickness(1),
            FontSize = Math.Clamp(14 * context.Scale, 12, 30),
            Tag = "plugin-interactive"
        };
        Grid.SetRow(input, 1);
        body.Children.Add(input);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 9, 0, 0)
        };
        var clear = CreateButton("Limpar", surface, foreground);
        var save = CreateButton("Salvar", accent, accentForeground);
        save.Margin = new Thickness(7, 0, 0, 0);

        clear.Click += async (_, e) =>
        {
            e.Handled = true;
            await context.ExecuteAsync(new PluginCommand("clear"));
        };
        save.Click += async (_, e) =>
        {
            e.Handled = true;
            var arguments = new Dictionary<string, string> { ["value"] = input.Text };
            var result = await context.ExecuteAsync(new PluginCommand("set-text", arguments));
            if (!result.Succeeded && !string.IsNullOrWhiteSpace(result.Message))
                context.RequestHostAction(new(PluginHostActionKind.ShowMessage, result.Message));
        };

        actions.Children.Add(clear);
        actions.Children.Add(save);
        Grid.SetRow(actions, 2);
        body.Children.Add(actions);
    }

    private static void AddViewer(
        WindowsPluginViewContext context,
        Grid body,
        Brush foreground,
        Brush accent,
        Brush accentForeground)
    {
        var text = context.State.GetString("text") ?? "";
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(text) ? "Sem conteúdo" : text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = foreground,
                FontSize = Math.Clamp(15 * context.Scale, 13, 32)
            }
        };
        Grid.SetRow(scroll, 1);
        body.Children.Add(scroll);

        var copy = CreateButton("Copiar", accent, accentForeground);
        copy.HorizontalAlignment = HorizontalAlignment.Right;
        copy.Margin = new Thickness(0, 9, 0, 0);
        copy.Click += (_, e) =>
        {
            e.Handled = true;
            context.RequestHostAction(new(PluginHostActionKind.CopyToClipboard, text));
        };
        Grid.SetRow(copy, 2);
        body.Children.Add(copy);
    }

    private static Button CreateButton(string label, Brush background, Brush foreground) => new()
    {
        Content = label,
        Background = background,
        Foreground = foreground,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(13, 7, 13, 7),
        MinHeight = 34,
        Tag = "plugin-interactive",
        Cursor = System.Windows.Input.Cursors.Hand
    };

    private static Brush GetBrush(string color, string fallback = "#000000")
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
        catch { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback)); }
    }

    private static Brush GetContrastBrush(string color)
    {
        try
        {
            var parsed = (Color)ColorConverter.ConvertFromString(color);
            var luminance = (0.299 * parsed.R) + (0.587 * parsed.G) + (0.114 * parsed.B);
            return GetBrush(luminance > 160 ? "#152033" : "#FFFFFF");
        }
        catch { return Brushes.White; }
    }
}
