using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ClipDesk.Plugin.Modules;
using ClipDesk.PluginSdk;
using ClipDesk.PluginSdk.Windows;

namespace ClipDesk.Plugin.CurrencyConverter;

public sealed class CurrencyConverterPlugin : IWindowsPluginRenderer, IWindowsPlugin
{
    private static IReadOnlyList<PluginOption>? _currencies;

    public FrameworkElement CreateBody(PluginViewContext context) =>
        CreateBody(LegacyPluginAdapter.CreateContext(new CurrencyConverterModule(), context));

    public FrameworkElement CreateBody(WindowsPluginViewContext context)
    {
        var body = new Grid { Margin = new Thickness(13, 9, 13, 11) };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(.55, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(.45, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var source = context.State.GetString("sourceCurrency") ?? "BRL"; var target = context.State.GetString("targetCurrency") ?? "USD";
        var row = WidgetUi.ChoiceRow(context, source, target, out var sourceButton, out var targetButton, out var swap);
        var resultText = WidgetUi.Text(context, context.State.GetString("result") ?? "Escolha as moedas e converta", 17);
        resultText.Foreground = WidgetUi.Brush(context.AccentColor); resultText.FontWeight = FontWeights.SemiBold; resultText.Margin = new Thickness(3, 2, 3, 2);
        var status = WidgetUi.Text(context, "Atualização automática", 11, true); status.Margin = new Thickness(2, 3, 0, 0);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
        CancellationTokenSource? pending = null; var disposed = false;
        void Schedule() { timer.Stop(); pending?.Cancel(); status.Text = "Convertendo…"; timer.Start(); }
        timer.Tick += async (_, _) =>
        {
            timer.Stop(); pending?.Dispose(); pending = new CancellationTokenSource();
            try
            {
                var converted = await context.ExecuteAsync(new PluginCommand("convert"), false, pending.Token, recordUndo: false);
                if (disposed || pending.IsCancellationRequested) return;
                if (!converted.Succeeded) { status.Text = converted.Message ?? "Cotação indisponível"; return; }
                resultText.Text = converted.State.GetString("result") ?? "";
                var rate = converted.Data?.GetValueOrDefault("rate");
                status.Text = decimal.TryParse(rate, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                    ? $"1 {context.State.GetString("sourceCurrency")} = {parsed.ToString("0.######", CultureInfo.GetCultureInfo("pt-BR"))} {context.State.GetString("targetCurrency")}" : "Atualização automática";
            }
            catch (OperationCanceledException) { }
        };
        body.Unloaded += (_, _) => { disposed = true; timer.Stop(); pending?.Cancel(); pending?.Dispose(); };
        body.Loaded += (_, _) => { disposed = false; Schedule(); };
        async void OpenChoices(Button anchor, string key, string selected)
        {
            var choices = _currencies ?? CurrencyConverterModule.FallbackCurrencies;
            void Show(IReadOnlyList<PluginOption> options) => WidgetUi.ShowChoices(anchor, options.Select(x => (x.Value, $"{x.Value} — {x.Label}")), selected,
                async value => { await context.ExecuteAsync(Set(key, value)); }, context);
            Show(choices);
            if (_currencies is not null) return;
            try
            {
                var response = await context.Module.ExecuteAsync(context.State, new PluginCommand("currencies"), context.Execution);
                if (disposed || !response.Succeeded || response.Data?.GetValueOrDefault("options") is not { } json) return;
                _currencies = JsonSerializer.Deserialize<List<PluginOption>>(json);
                // The next opening uses the complete remote list; keep the current searchable menu stable.
            }
            catch (OperationCanceledException) { }
        }
        sourceButton.Click += (_, e) => { e.Handled = true; OpenChoices(sourceButton, "sourceCurrency", source); };
        targetButton.Click += (_, e) => { e.Handled = true; OpenChoices(targetButton, "targetCurrency", target); };
        swap.Click += async (_, e) => { e.Handled = true; await context.ExecuteAsync(new PluginCommand("swap")); };
        body.Children.Add(row);
        var input = WidgetUi.Input(context, context.State.GetString("amount") ?? "1", 21);
        input.Margin = new Thickness(0, 8, 0, 4); input.ToolTip = "Digite para converter automaticamente";
        input.TextChanged += async (_, _) => { await context.ExecuteAsync(Set("amount", input.Text), false, recordUndo: false); Schedule(); };
        Grid.SetRow(input, 1); body.Children.Add(input); Grid.SetRow(resultText, 2); body.Children.Add(resultText); Grid.SetRow(status, 3); body.Children.Add(status);
        return body;
    }
    private static PluginCommand Set(string key, string value) => new("set", new Dictionary<string, string> { ["key"] = key, ["value"] = value });
}
