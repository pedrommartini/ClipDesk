using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipDesk.Plugin.ImageCompressor;
using ClipDesk.Plugin.ImageCompressor.Models;
using ClipDesk.Plugin.ImageCompressor.UI;
using ClipDesk.PluginSdk;
using ClipDesk.PluginSdk.Windows;

internal static class CompressorCollaborationChecks
{
    public static void Run(Application app)
    {
        foreach (var batch in new[] { false, true }) Check(batch);
        Console.WriteLine("PASS: compressor shares individual/batch inputs, stable IDs, settings and identical outputs without receiver processing.");
    }
    private static void Check(bool batch)
    {
        var module = new ImageCompressorModule();
        var execution = new PluginExecutionContext(new OfflineNetwork());
        var state = module.ExecuteAsync(module.CreateDefaultState(), new PluginCommand(ImageCompressorCommands.SetSettings,
            new Dictionary<string, string> { [ImageCompressorState.Quality] = "55" }), execution).Result.State;
        var assets = new Assets();
        var context = new WindowsPluginViewContext(module, state, execution, true, false, 560, 340, 1, "#2563EB", () => { },
            (next, _) => state = next, _ => { }) { SharedAssets = assets };
        var local = new ImageCompressorControl(context);
        if (batch) Method("ToggleBatchMode").Invoke(local, [true]);
        var files = Enumerable.Range(0, batch ? 2 : 1).Select(CreateImage).ToArray();
        Await(context.DeliverFilesAsync(files));
        Await((Task)Method("StartCompressionAsync").Invoke(local, null)!);
        if (assets.Imports != files.Length * 2 || module.StateVersion != 2)
            throw new Exception("Compressor did not share each input/output or advertises the wrong schema.");
        var shared = batch ? CompressorSharedState.ReadBatch(state) : [CompressorSharedState.ReadSingle(state)!];
        if (shared.Count != files.Length || shared.Any(e => !Guid.TryParse(e.OutputAssetId, out _)))
            throw new Exception("Compressor state lost batch items or completed output references.");
        var reset = module.ExecuteAsync(state, new PluginCommand(ImageCompressorCommands.ResetSettings), execution).Result.State;
        if ((batch ? CompressorSharedState.ReadBatch(reset).Count : CompressorSharedState.ReadSingle(reset) is not null ? 1 : 0) != files.Length)
            throw new Exception("Resetting compression settings destroyed the shared inputs/results.");

        local.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        assets.ReceiveOnly = true;
        var recipient = new WindowsPluginViewContext(module, state, execution, true, false, 560, 340, 1, "#2563EB",
            () => throw new Exception("Receiving compression recorded undo."),
            (_, _) => throw new Exception("Receiving compression executed a command or changed state."), _ => { }) { SharedAssets = assets };
        var remote = new ImageCompressorControl(recipient);
        Await((Task)Method("RestoreSharedFilesAsync").Invoke(remote, null)!);
        var items = batch ? ((ObservableCollection<BatchQueueItem>)Field("_queue").GetValue(remote)!).ToList()
            : new List<BatchQueueItem> { (BatchQueueItem)Field("_singleItem").GetValue(remote)! };
        if (items.Count != files.Length || items.Any(i => i.Status != BatchItemStatus.Completed)
            || recipient.State.GetInt32(ImageCompressorState.Quality) != 55)
            throw new Exception("Receiver did not reconstruct compression queue, completion or settings.");
        foreach (var item in items)
        {
            var outputId = shared.Single(e => e.InputAssetId == item.Id).OutputAssetId;
            if (!File.ReadAllBytes(item.OutputPath!).SequenceEqual(assets.Bytes(outputId)))
                throw new Exception("Receiver reconstructed different compressed bytes.");
        }
        if (assets.Imports != files.Length * 2) throw new Exception("Receiver reprocessed images.");
        remote.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    }
    private static MethodInfo Method(string name) => typeof(ImageCompressorControl).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static FieldInfo Field(string name) => typeof(ImageCompressorControl).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static PluginDroppedFile CreateImage(int seed)
    {
        var path = Path.Combine(Path.GetTempPath(), "compressor-shared-" + Guid.NewGuid().ToString("N") + ".png");
        using (var file = File.Create(path))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(16, 12, 96, 96, PixelFormats.Bgra32, null,
                Enumerable.Repeat(new byte[] { (byte)(seed * 40), 50, 100, 255 }, 192).SelectMany(b => b).ToArray(), 64)));
            encoder.Save(file);
        }
        return new PluginDroppedFile(path, Path.GetFileName(path), new FileInfo(path).Length);
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
    private sealed class Assets : IPluginSharedAssets
    {
        private readonly Dictionary<string, (string Path, string Name)> _files = new();
        public int Imports;
        public bool ReceiveOnly;
        public byte[] Bytes(string id) => File.ReadAllBytes(_files[id].Path);
        public async ValueTask<PluginSharedAsset> ImportAsync(IPluginReadableFile source, CancellationToken cancellationToken = default)
        {
            if (ReceiveOnly) throw new Exception("Receiver repeated compression/import.");
            Imports++;
            var id = Guid.NewGuid().ToString("N");
            var path = Path.Combine(Path.GetTempPath(), id + Path.GetExtension(source.DisplayName));
            await using (var input = await source.OpenReadAsync(cancellationToken))
            await using (var output = File.Create(path)) await input.CopyToAsync(output, cancellationToken);
            _files[id] = (path, source.DisplayName);
            return new PluginSharedAsset(id, source.DisplayName, new FileInfo(path).Length, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        }
        public ValueTask<IPluginReadableFile?> OpenAsync(string assetId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IPluginReadableFile?>(_files.TryGetValue(assetId, out var file) ? WindowsPluginFile.FromPath(file.Path, file.Name) : null);
    }
    private sealed class OfflineNetwork : IPluginNetworkClient
    {
        public Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken = default) => throw new Exception("Unexpected compression network call.");
    }
}
