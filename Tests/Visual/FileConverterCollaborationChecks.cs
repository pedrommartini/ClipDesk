using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ClipDesk.Plugin.FileConverter;
using ClipDesk.PluginSdk;
using ClipDesk.PluginSdk.Windows;

internal static class FileConverterCollaborationChecks
{
    public static void Run(Application app)
    {
        var module = new FileConverterModule();
        var sourceId = Guid.NewGuid().ToString("N");
        var outputId = Guid.NewGuid().ToString("N");
        var source = Path.Combine(Path.GetTempPath(), "clipdesk-converter-" + sourceId + ".json");
        File.WriteAllText(source, "{\"shared\":true}");
        var output = Path.ChangeExtension(source, ".txt");
        File.WriteAllText(output, "shared: true");
        var execution = new PluginExecutionContext(new OfflineNetwork());
        var state = module.ExecuteAsync(module.CreateDefaultState(), new PluginCommand("set-source",
            new Dictionary<string, string> { ["assetId"] = sourceId, ["fileName"] = "entrada.json", ["size"] = new FileInfo(source).Length.ToString() }), execution).Result.State;
        state = module.ExecuteAsync(state, new PluginCommand("set-option", new Dictionary<string, string> { ["key"] = "quality", ["value"] = "65" }), execution).Result.State;
        state = module.ExecuteAsync(state, new PluginCommand("set-status", new Dictionary<string, string>
            { ["status"] = "success", ["assetId"] = outputId, ["fileName"] = "resultado.txt" }), execution).Result.State;
        var legacy = module.NormalizeState(state.With("sourcePath", source).With("lastConvertedFile", output));
        if (legacy.Values.ContainsKey("sourcePath") || legacy.Values.ContainsKey("lastConvertedFile")
            || legacy.GetInt32("quality") != 65 || module.GetClipboardText(legacy) != "resultado.txt")
            throw new Exception("Converter leaked a local path or lost shared options/output metadata.");
        var denied = module.ExecuteAsync(state, new PluginCommand("set-option",
            new Dictionary<string, string> { ["key"] = "sourcePath", ["value"] = source }), execution).Result;
        if (denied.Succeeded) throw new Exception("Options can inject a local path into shared state.");

        var assets = new Assets(sourceId, source, outputId, output);
        var context = new WindowsPluginViewContext(module, legacy, execution, true, false, 680, 360, 1, "#6366F1",
            () => throw new Exception("Receiving files recorded undo."),
            (_, _) => throw new Exception("Receiving files executed conversion or changed state."), _ => { }) { SharedAssets = assets };
        var view = new FileConverterPlugin().CreateBody(context);
        void Layout()
        {
            view.Measure(new Size(680, 360));
            view.Arrange(new Rect(0, 0, 680, 360));
            view.UpdateLayout();
        }
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        PumpUntil(() => { Layout(); return Descendants(view).OfType<TextBlock>().Any(t => t.Text == "resultado.txt"); });
        if (assets.Opens != 2 || !Descendants(view).OfType<TextBlock>().Any(t => t.Text == "entrada.json"))
            throw new Exception("Receiving client did not reconstruct both converter files.");
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Console.WriteLine("PASS: converter reconstructs input/output and settings without conversion or local paths in state.");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private static void PumpUntil(Func<bool> complete)
    {
        var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background, (_, _) =>
        {
            if (complete() || DateTime.UtcNow > deadline) frame.Continue = false;
        }, Application.Current.Dispatcher);
        Dispatcher.PushFrame(frame);
        timer.Stop();
        if (!complete()) throw new Exception("Shared converter reconstruction did not complete.");
    }
    private sealed class Assets(string inputId, string input, string outputId, string output) : IPluginSharedAssets
    {
        public int Opens;
        public ValueTask<PluginSharedAsset> ImportAsync(IPluginReadableFile source, CancellationToken cancellationToken = default) =>
            throw new Exception("Receiving a file attempted to convert/import it again.");
        public ValueTask<IPluginReadableFile?> OpenAsync(string assetId, CancellationToken cancellationToken = default)
        {
            Opens++;
            return ValueTask.FromResult<IPluginReadableFile?>(assetId == inputId ? WindowsPluginFile.FromPath(input, "entrada.json")
                : assetId == outputId ? WindowsPluginFile.FromPath(output, "resultado.txt") : null);
        }
    }
    private sealed class OfflineNetwork : IPluginNetworkClient
    {
        public Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken = default) =>
            throw new Exception("Receiving files made an external processing request.");
    }
}
