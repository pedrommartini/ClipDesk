using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using ClipDesk.Plugin.Tts;
using ClipDesk.Plugin.Tts.Views;
using ClipDesk.PluginSdk;
using ClipDesk.PluginSdk.Windows;
using ClipDesk.Services;

internal static class TtsCollaborationChecks
{
    public static void RunPackage(string installedRoot)
    {
        var catalog = new PluginCatalogService(Path.Combine(installedRoot, "no-bundled"), installedRoot,
            new Version(0, 4, 4), Path.Combine(installedRoot, "no-updates"));
        var loader = new WindowsPluginLoader(catalog);
        if (loader.Manifest("clipdesk.tts")?.Version != "1.1.0")
            throw new Exception("The candidate TTS package did not become active.");
        var body = loader.CreateBody("clipdesk.tts", new TtsModule().CreateDefaultState(), true, false,
            360, 300, 1, "#2563EB", () => { }, (_, _) => { }, _ => { });
        if (body?.GetType().FullName != typeof(TtsView).FullName)
            throw new Exception($"The packaged TTS renderer failed: {loader.LastFailure("clipdesk.tts")}");
        (body as IDisposable)?.Dispose();
        Console.WriteLine("PASS: packaged TTS 1.1.0 loads its Windows renderer from an isolated install.");
    }

    public static void Run(Application app)
    {
        var module = new TtsModule();
        var assetId = Guid.NewGuid().ToString("N");
        var state = module.ExecuteAsync(module.CreateDefaultState(), new PluginCommand("generate-speech",
            new Dictionary<string, string> { ["text"] = "Áudio compartilhado", ["audio_file_name"] = "fixture.wav",
                [TtsPluginState.KeyAudioAssetId] = assetId }), new PluginExecutionContext(new OfflineNetworkClient())).Result.State;
        if (module.NormalizeState(state).GetString(TtsPluginState.KeyAudioAssetId) != assetId)
            throw new Exception("TTS normalization lost its shared output reference.");
        var source = Path.Combine(Path.GetTempPath(), "clipdesk-tts-fixture-" + assetId + ".wav");
        using (var stream = File.Create(source))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write("RIFF"u8); writer.Write(356); writer.Write("WAVEfmt "u8); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000);
            writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(320); writer.Write(new byte[320]);
        }
        var assets = new Assets(assetId, source);
        var context = new WindowsPluginViewContext(module, state, new PluginExecutionContext(new OfflineNetworkClient()),
            true, false, 360, 300, 1, "#2563EB", () => throw new Exception("Receiving output recorded an undo action."),
            (_, _) => throw new Exception("Receiving output repeated a TTS command."), _ => { }) { SharedAssets = assets };
        using var view = new TtsView(context);
        var restore = (Task)typeof(TtsView).GetMethod("RestoreSharedAudioAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null)!;
        Await(restore);
        var player = view.Children.OfType<AudioPlayerControl>().Single();
        if (assets.Opens != 1 || player.IsPlaying || player.CurrentFilePath is null
            || !File.ReadAllBytes(player.CurrentFilePath).SequenceEqual(File.ReadAllBytes(source)))
            throw new Exception("TTS did not reconstruct the shared audio silently and intact.");
        Console.WriteLine("PASS: TTS reconstructs shared output without synthesis, playback or state mutation.");
    }

    private sealed class Assets(string id, string path) : IPluginSharedAssets
    {
        public int Opens;
        public ValueTask<PluginSharedAsset> ImportAsync(IPluginReadableFile source, CancellationToken cancellationToken = default) =>
            throw new Exception("Receiving shared audio attempted to import or synthesize it again.");
        public ValueTask<IPluginReadableFile?> OpenAsync(string assetId, CancellationToken cancellationToken = default)
        {
            Opens++;
            return ValueTask.FromResult<IPluginReadableFile?>(assetId == id ? WindowsPluginFile.FromPath(path, "fixture.wav", "audio/wav") : null);
        }
    }
    private sealed class OfflineNetworkClient : IPluginNetworkClient
    {
        public Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken = default) =>
            throw new Exception("Receiving shared output made an external request.");
    }
    private static void Await(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => Application.Current.Dispatcher.BeginInvoke(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }
}
