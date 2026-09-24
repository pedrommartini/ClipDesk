using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using ClipDesk.PluginSdk;
using ClipDesk.PluginSdk.Windows;

namespace ClipDesk.Plugin.Starter;

public sealed class StarterPlugin : IWindowsPluginRenderer
{
    public FrameworkElement CreateBody(WindowsPluginViewContext context)
    {
        context.FilesDropped += async files =>
        {
            var file = files.FirstOrDefault();
            if (file is null) return;
            if (!file.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || file.Length > 1024 * 1024)
            {
                context.RequestHostAction(new(PluginHostActionKind.ShowMessage, "Solte um arquivo .txt de até 1 MiB."));
                return;
            }
            try
            {
                var text = await File.ReadAllTextAsync(file.Path);
                await context.ExecuteAsync(new PluginCommand("set-text", new Dictionary<string, string> { ["value"] = text }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                context.RequestHostAction(new(PluginHostActionKind.ShowMessage, "Não foi possível ler o arquivo."));
            }
        };
        var foreground = GetBrush(context.IsDarkMode ? "#F3F6FA" : "#182230");
        var surface = GetBrush(context.IsDarkMode ? "#202936" : "#F5F7FA");
        var accent = GetBrush(context.AccentColor, "#7C5CFC");

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        if (context.IsEditing)
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void UpdateSpacing()
        {
            body.Margin = context.Width < 280 || body.ActualWidth is > 0 and < 280
                ? new Thickness(8, 7, 8, 8)
                : new Thickness(12, 9, 12, 10);
        }
        body.SizeChanged += (_, _) => UpdateSpacing();
        context.LayoutChanged += UpdateSpacing;
        UpdateSpacing();

        if (context.IsEditing)
        {
            AddEditor(context, body, foreground, surface, accent);
        }
        else
        {
            AddViewer(context, body, foreground);
        }

        return body;
    }

    private static void AddEditor(
        WindowsPluginViewContext context,
        Grid body,
        Brush foreground,
        Brush surface,
        Brush accent)
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
            FontSize = Math.Clamp(17 * context.Scale, 15, 32),
            Tag = "plugin-interactive"
        };
        context.LayoutChanged += () => input.FontSize = Math.Clamp(17 * context.Scale, 15, 32);
        Grid.SetRow(input, 0);
        body.Children.Add(input);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 9, 0, 0)
        };
        var clear = PluginButtons.Create(context, "Limpar");
        var save = PluginButtons.Create(context, "Salvar", true);
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
        Grid.SetRow(actions, 1);
        body.Children.Add(actions);
    }

    private static void AddViewer(
        WindowsPluginViewContext context,
        Grid body,
        Brush foreground)
    {
        var text = context.State.GetString("text") ?? "";
        var result = new Grid();
        result.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        result.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(text) ? "Sem conteúdo" : text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = foreground,
                FontSize = Math.Clamp(19 * context.Scale, 16, 36)
            }
        };
        var output = (TextBlock)scroll.Content;
        context.LayoutChanged += () => output.FontSize = Math.Clamp(19 * context.Scale, 16, 36);
        result.Children.Add(scroll);

        var copy = PluginButtons.Create(context, "Copiar");
        copy.Content = new TextBlock
        {
            Text = "\uE8C8",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = Math.Clamp(17 * context.Scale, 16, 30)
        };
        copy.MinWidth = 36;
        copy.MinHeight = 36;
        copy.ToolTip = "Copiar conteúdo";
        AutomationProperties.SetName(copy, "Copiar conteúdo");
        copy.IsEnabled = !string.IsNullOrWhiteSpace(text);
        copy.VerticalAlignment = VerticalAlignment.Top;
        copy.Margin = new Thickness(6, 0, 0, 0);
        copy.Click += (_, e) =>
        {
            e.Handled = true;
            var value = context.Module.GetClipboardText(context.State);
            if (!string.IsNullOrWhiteSpace(value))
                context.RequestHostAction(new(PluginHostActionKind.CopyToClipboard, value));
        };
        Grid.SetColumn(copy, 1);
        result.Children.Add(copy);
        body.Children.Add(result);
    }

    private static Brush GetBrush(string color, string fallback = "#000000")
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
        catch { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback)); }
    }

}
