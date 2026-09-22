using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ClipDesk.Plugin.Modules;
using ClipDesk.PluginSdk;
using ClipDesk.PluginSdk.Windows;

namespace ClipDesk.Plugin.Translator;

public sealed class TranslatorPlugin : IWindowsPluginRenderer, IWindowsPlugin
{
    public FrameworkElement CreateBody(PluginViewContext context) =>
        CreateBody(LegacyPluginAdapter.CreateContext(new TranslatorModule(), context));

    public FrameworkElement CreateBody(WindowsPluginViewContext context)
    {
        var body = new Grid { Margin = new Thickness(13, 9, 13, 11) };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var source = context.State.GetString("sourceLanguage") ?? "auto";
        var target = context.State.GetString("targetLanguage") ?? "en";
        var row = WidgetUi.ChoiceRow(context, TranslatorModule.Label(source), TranslatorModule.Label(target), out var sourceButton, out var targetButton, out var swap);
        var output = WidgetUi.Text(context, context.State.GetString("output") ?? "A tradução aparece aqui");
        output.Foreground = WidgetUi.Brush(context.AccentColor); output.Margin = new Thickness(4, 6, 4, 3);
        var status = WidgetUi.Text(context, "Atualização automática", 11, true); status.Margin = new Thickness(2, 3, 0, 0);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
        CancellationTokenSource? pending = null; var disposed = false;
        void Schedule()
        {
            timer.Stop(); pending?.Cancel();
            if (string.IsNullOrWhiteSpace(context.State.GetString("input")))
            { output.Text = "A tradução aparece aqui"; status.Text = "Atualização automática"; return; }
            status.Text = "Traduzindo…"; timer.Start();
        }
        timer.Tick += async (_, _) =>
        {
            timer.Stop(); pending?.Dispose(); pending = new CancellationTokenSource();
            try
            {
                var result = await context.ExecuteAsync(new PluginCommand("translate"), false, pending.Token, recordUndo: false);
                if (disposed || pending.IsCancellationRequested) return;
                if (result.Succeeded) { output.Text = result.State.GetString("output") ?? ""; status.Text = "Atualização automática"; }
                else status.Text = result.Message ?? "Não foi possível traduzir agora";
            }
            catch (OperationCanceledException) { }
        };
        body.Unloaded += (_, _) => { disposed = true; timer.Stop(); pending?.Cancel(); pending?.Dispose(); };
        body.Loaded += (_, _) => { disposed = false; if (!string.IsNullOrWhiteSpace(context.State.GetString("input"))) Schedule(); };
        async void Choose(string key, string value) { await context.ExecuteAsync(Set(key, value)); }
        sourceButton.Click += (_, e) => { e.Handled = true; WidgetUi.ShowChoices(sourceButton, TranslatorModule.Languages.Select(x => (x.Value, x.Label)), source, value => Choose("sourceLanguage", value), context); };
        targetButton.Click += (_, e) => { e.Handled = true; WidgetUi.ShowChoices(targetButton, TranslatorModule.Languages.Where(x => x.Value != "auto").Select(x => (x.Value, x.Label)), target, value => Choose("targetLanguage", value), context); };
        swap.Click += async (_, e) => { e.Handled = true; await context.ExecuteAsync(new PluginCommand("swap")); };
        body.Children.Add(row);
        var input = WidgetUi.Input(context, context.State.GetString("input") ?? "", 14, true);
        input.Margin = new Thickness(0, 7, 0, 5); input.ToolTip = "Digite para traduzir automaticamente";
        input.TextChanged += async (_, _) => { await context.ExecuteAsync(Set("input", input.Text), false, recordUndo: false); Schedule(); };
        Grid.SetRow(input, 1); body.Children.Add(input);
        var outputScroll = new ScrollViewer { Content = output, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(outputScroll, 2); body.Children.Add(outputScroll); Grid.SetRow(status, 3); body.Children.Add(status);
        return body;
    }
    private static PluginCommand Set(string key, string value) => new("set", new Dictionary<string, string> { ["key"] = key, ["value"] = value });
}
