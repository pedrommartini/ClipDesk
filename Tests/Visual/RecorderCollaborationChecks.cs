using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ClipDesk.Plugin.AudioRecorder;
using ClipDesk.PluginSdk;
using ClipDesk.PluginSdk.Windows;

internal static class RecorderCollaborationChecks
{
    public static void Run(Application app)
    {
        var module = new AudioRecorderModule();
        var execution = new PluginExecutionContext(new OfflineNetwork());
        var config = module.ExecuteAsync(module.CreateDefaultState(), new PluginCommand("set-state", new Dictionary<string, string>
        {
            ["micEnabled"] = "false", ["micVolume"] = "42", ["micChannelMode"] = "stereo",
            ["systemEnabled"] = "true", ["systemVolume"] = "65", ["sysChannelMode"] = "mono"
        }), execution).Result.State;
        if (config.GetString("micEnabled") != "false" || config.GetInt32("micVolume") != 42
            || config.GetString("micChannelMode") != "stereo" || config.GetInt32("systemVolume") != 65
            || config.GetString("sysChannelMode") != "mono")
            throw new Exception("Recorder settings did not persist across clients.");
        var id = Guid.NewGuid().ToString("N");
        var source = Path.Combine(Path.GetTempPath(), "clipdesk-recording-" + id + ".wav");
        using (var stream = File.Create(source))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write("RIFF"u8); writer.Write(356); writer.Write("WAVEfmt "u8); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000);
            writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(320); writer.Write(new byte[320]);
        }
        var recorded = module.ExecuteAsync(config, new PluginCommand("set-state", new Dictionary<string, string>
        {
            ["status"] = "preview", ["duration"] = "17", ["audioAssetId"] = id,
            ["audioFileName"] = "shared.wav", ["lastFile"] = source
        }), execution).Result.State;
        if (recorded.Values.ContainsKey("lastFile") || recorded.GetString("audioAssetId") != id)
            throw new Exception("Recorder persisted a private path or lost its shared file ID.");
        var assets = new Assets(id, source);
        var context = new WindowsPluginViewContext(module, recorded, execution, true, false, 540, 180, 1, "#EF4444",
            () => throw new Exception("Receiving audio recorded an undo action."),
            (_, _) => throw new Exception("Receiving audio changed state or started capture."), _ => { }) { SharedAssets = assets };
        var body = new AudioRecorderPlugin().CreateBody(context);
        body.Measure(new Size(540, 180));
        body.Arrange(new Rect(0, 0, 540, 180));
        body.UpdateLayout();
        body.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        PumpUntil(() => Descendants(body).OfType<TextBlock>().Any(t => t.Text == "ÁUDIO COMPARTILHADO"));
        if (assets.Opens != 1 || !Descendants(body).OfType<Button>().Any(b => b.IsEnabled &&
                Descendants(b).OfType<TextBlock>().Any(t => t.Text == "Ouvir")))
            throw new Exception("Receiving client did not restore a playable, idle recording preview.");
        body.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Console.WriteLine("PASS: recorder shares settings/audio and restores a silent preview without capture or private paths.");
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
        { if (complete() || DateTime.UtcNow > deadline) frame.Continue = false; }, Application.Current.Dispatcher);
        Dispatcher.PushFrame(frame);
        timer.Stop();
        if (!complete()) throw new Exception("Shared recorder audio did not restore.");
    }
    private sealed class Assets(string id, string path) : IPluginSharedAssets
    {
        public int Opens;
        public ValueTask<PluginSharedAsset> ImportAsync(IPluginReadableFile source, CancellationToken cancellationToken = default) =>
            throw new Exception("Receiver imported or captured audio.");
        public ValueTask<IPluginReadableFile?> OpenAsync(string assetId, CancellationToken cancellationToken = default)
        {
            Opens++;
            return ValueTask.FromResult<IPluginReadableFile?>(assetId == id ? WindowsPluginFile.FromPath(path, "shared.wav") : null);
        }
    }
    private sealed class OfflineNetwork : IPluginNetworkClient
    {
        public Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken = default) =>
            throw new Exception("Receiving recorder audio used the network client.");
    }
}
