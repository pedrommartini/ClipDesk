using ClipDesk.Plugin.Calculator;
using ClipDesk.Plugin.Checklist;
using ClipDesk.Plugin.CurrencyConverter;
using ClipDesk.Plugin.Translator;
using ClipDesk.PluginSdk;
using ClipDesk.PluginSdk.Windows;
using ClipDesk.Services;
using ClipDesk.Core;
using ClipDesk.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

internal static class PluginV2Checks
{
    public static void Run()
    {
        Calculator(); ChecklistMigration(); Translator(); Currency(); Compatibility(); Loader();
        Console.WriteLine("PASS: portable plugin v2 contracts, migrations and commands.");
    }

    private static void Calculator()
    {
        var module = new CalculatorModule(); var state = module.CreateDefaultState();
        foreach (var key in new[] { "1", "2", "+", "3", "=" })
            state = module.ExecuteAsync(state, Command("press", "key", key), Context()).Result.State;
        Equal("15", state.GetString("display"), "calculator result");
    }

    private static void ChecklistMigration()
    {
        var module = new ChecklistModule();
        var legacy = new PluginState(new Dictionary<string, string> { ["title"] = "Tarefas", ["items"] = "A\nB", ["checked"] = "1" });
        var migrated = module.NormalizeState(legacy); var first = ChecklistModule.ReadItems(migrated);
        if (first.Count != 2 || !first[1].IsCompleted || first.Any(item => string.IsNullOrWhiteSpace(item.Id))) throw new Exception("Checklist migration failed.");
        var second = ChecklistModule.ReadItems(module.NormalizeState(migrated));
        Equal(first[0].Id, second[0].Id, "stable checklist id");
        var toggled = module.ExecuteAsync(migrated, Command("toggle", "id", first[0].Id), Context()).Result.State;
        if (!ChecklistModule.ReadItems(toggled)[0].IsCompleted) throw new Exception("Checklist toggle failed.");
    }

    private static void Translator()
    {
        var network = new FakeNetwork("{\"responseData\":{\"translatedText\":\"Ol&amp;á\"}}");
        var module = new TranslatorModule(); var state = module.CreateDefaultState();
        state = module.ExecuteAsync(state, Command("set", "key", "input", "value", "Hello"), Context(network)).Result.State;
        var result = module.ExecuteAsync(state, new PluginCommand("translate"), Context(network)).Result;
        if (!result.Succeeded) throw new Exception("Translator command failed.");
        Equal("Ol&á", result.State.GetString("output"), "decoded translation");
        Equal("api.mymemory.translated.net", network.LastUri?.Host, "translator host");
    }

    private static void Currency()
    {
        var network = new FakeNetwork("[{\"date\":\"2026-01-01\",\"base\":\"BRL\",\"quote\":\"USD\",\"rate\":0.2}]");
        var module = new CurrencyConverterModule(); var state = module.CreateDefaultState();
        state = module.ExecuteAsync(state, Command("set", "key", "amount", "value", "10"), Context(network)).Result.State;
        var result = module.ExecuteAsync(state, new PluginCommand("convert"), Context(network)).Result;
        if (!result.Succeeded || result.State.GetString("result") != "2,00 USD") throw new Exception("Currency conversion failed.");
    }

    private static void Compatibility()
    {
        var manifest = new PluginManifest
        {
            ManifestVersion = 2, Id = "clipdesk.test", Name = "Test", Version = "1.0.0",
            Runtime = PluginRuntimes.PortableV2, PluginApiVersion = 2, StateVersion = 1,
            MinimumHostVersion = "0.3.3", Platforms = [PluginPlatforms.Windows],
            Module = new PluginEntryPoint { Assembly = "Test.Core.dll", Type = "Test.Module" },
            Renderers = [new PluginRendererEntryPoint { Platform = PluginPlatforms.Windows, Runtime = PluginRuntimes.WpfV2, Assembly = "Test.Windows.dll", Type = "Test.Renderer" }]
        };
        if (!PluginCompatibility.Supports(manifest, new Version(1, 0), 2, PluginPlatforms.Windows)) throw new Exception("v2 manifest rejected.");
        if (PluginCompatibility.Supports(manifest, new Version(1, 0), 2, PluginPlatforms.Android)) throw new Exception("Missing Android renderer accepted.");
    }

    private static void Loader()
    {
        var loader = new WindowsPluginLoader();
        var state = new PluginState(new Dictionary<string, string> { ["expression"] = "", ["display"] = "0" });
        var before = 0; var commits = 0;
        var body = loader.CreateBody(BuiltInPluginIds.Calculator, state, true, false, 300, 390, 1, "#FBBF24",
            () => before++, (next, _) => { state = next; commits++; }, _ => { });
        if (body is not Grid grid || grid.Children.OfType<UniformGrid>().SingleOrDefault() is not { } keys)
            throw new Exception("Bundled v2 renderer was not loaded.");
        var equals = keys.Children.OfType<Button>().Single(button => (button.Content as TextBlock)?.Text == "=");
        if (equals.Background is not System.Windows.Media.SolidColorBrush accent
            || accent.Color != (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FBBF24"))
            throw new Exception("The per-instance accent was not forwarded to the renderer.");
        var seven = keys.Children.OfType<Button>().Single(button => (button.Content as TextBlock)?.Text == "7");
        seven.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (state.GetString("display") != "7" || before != 1 || commits != 1)
            throw new Exception("Bundled v2 renderer did not commit through the host.");

        var boardView = new BoardObjectView(new BoardObject
        {
            Kind = BoardObjectKind.Calculator, PluginId = BuiltInPluginIds.Calculator,
            PluginVersion = "2.0.0", Width = 300, Height = 390,
            Style = new Dictionary<string, string> { [PluginStyleKeys.AccentColor] = "#FB7185" },
            Content = new Dictionary<string, string> { ["expression"] = "", ["display"] = "0" }
        });
        var icon = Descendants<TextBlock>(boardView).FirstOrDefault(text => text.Text == "∑");
        if (icon?.Foreground is not System.Windows.Media.SolidColorBrush headerAccent
            || headerAccent.Color != (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FB7185"))
            throw new Exception("The board did not render the persisted plugin accent.");
    }

    private static PluginExecutionContext Context(IPluginNetworkClient? network = null) =>
        new(network ?? new FakeNetwork(""), [PluginPermissions.Network], () => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private static PluginCommand Command(string name, params string[] pairs) => new(name,
        Enumerable.Range(0, pairs.Length / 2).ToDictionary(index => pairs[index * 2], index => pairs[index * 2 + 1]));
    private static void Equal(string? expected, string? actual, string name)
    { if (expected != actual) throw new Exception($"{name}: expected '{expected}', got '{actual}'."); }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private sealed class FakeNetwork(string response) : IPluginNetworkClient
    {
        public Uri? LastUri { get; private set; }
        public Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); LastUri = uri; return Task.FromResult(response); }
    }
}
