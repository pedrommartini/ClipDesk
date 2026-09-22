using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClipDesk.Plugin.Modules;
using ClipDesk.PluginSdk;
using ClipDesk.PluginSdk.Windows;

namespace ClipDesk.Plugin.Checklist;

public sealed class ChecklistPlugin : IWindowsPluginRenderer, IWindowsPlugin
{
    public FrameworkElement CreateBody(PluginViewContext context) =>
        CreateBody(LegacyPluginAdapter.CreateContext(new ChecklistModule(), context));

    public FrameworkElement CreateBody(WindowsPluginViewContext context)
    {
        var body = new Grid { Margin = new Thickness(13, 9, 13, 12) };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (context.IsEditing)
        {
            var title = WidgetUi.Input(context, context.State.GetString("title") ?? "Checklist");
            title.Margin = new Thickness(0, 0, 0, 7);
            title.TextChanged += async (_, _) => await context.ExecuteAsync(Command("set-title", "title", title.Text), false, recordUndo: false);
            body.Children.Add(title);
        }

        var stack = new StackPanel();
        foreach (var item in ChecklistModule.ReadItems(context.State))
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (context.IsEditing) row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var toggle = WidgetUi.Button(context, item.IsCompleted ? "✓" : "  ", item.IsCompleted);
            toggle.Width = Math.Clamp(29 * context.Scale, 27, 90); toggle.Margin = new Thickness(0, 0, 8, 0);
            toggle.Click += async (_, e) => { e.Handled = true; await context.ExecuteAsync(Command("toggle", "id", item.Id)); };
            row.Children.Add(toggle);
            FrameworkElement content;
            if (context.IsEditing)
            {
                var input = WidgetUi.Input(context, item.Text); input.BorderThickness = new Thickness(0);
                input.TextChanged += async (_, _) => await context.ExecuteAsync(Command("update", "id", item.Id, "text", input.Text), false, recordUndo: false);
                content = input;
                var remove = WidgetUi.Button(context, "×"); remove.Margin = new Thickness(7, 0, 0, 0);
                remove.Click += async (_, e) => { e.Handled = true; await context.ExecuteAsync(Command("remove", "id", item.Id)); };
                Grid.SetColumn(remove, 2); row.Children.Add(remove);
            }
            else
            {
                var label = WidgetUi.Text(context, item.Text);
                if (item.IsCompleted) { label.Foreground = WidgetUi.Brush(context.IsDarkMode ? "#8696AC" : "#8896A8"); label.TextDecorations = TextDecorations.Strikethrough; }
                content = label;
            }
            Grid.SetColumn(content, 1); row.Children.Add(content); stack.Children.Add(row);
        }
        if (stack.Children.Count == 0) stack.Children.Add(WidgetUi.Text(context, context.IsEditing ? "Adicione o primeiro item" : "Checklist vazio", 13, true));
        var scroll = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); body.Children.Add(scroll);
        if (context.IsEditing)
        {
            var add = WidgetUi.Button(context, "＋  Adicionar item", true); add.Margin = new Thickness(0, 7, 0, 0);
            add.Click += async (_, e) => { e.Handled = true; await context.ExecuteAsync(new PluginCommand("add")); };
            Grid.SetRow(add, 2); body.Children.Add(add);
        }
        return body;
    }

    private static PluginCommand Command(string name, params string[] arguments) => new(name,
        Enumerable.Range(0, arguments.Length / 2).ToDictionary(i => arguments[i * 2], i => arguments[i * 2 + 1]));
}
