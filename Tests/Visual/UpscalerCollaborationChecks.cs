using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipDesk.Plugin.ImageUpscaler;
using ClipDesk.Plugin.ImageUpscaler.Models;
using ClipDesk.Plugin.ImageUpscaler.UI;
using ClipDesk.PluginSdk;
using ClipDesk.PluginSdk.Windows;

internal static class UpscalerCollaborationChecks
{
    public static void Run(Application app)
    {
        var module = new ImageUpscalerModule();
        var execution = new PluginExecutionContext(new OfflineNetwork());
        var state = module.ExecuteAsync(module.CreateDefaultState(), new PluginCommand(ImageUpscalerCommands.SetSettings,
            new Dictionary<string, string> { [ImageUpscalerState.ScaleFactor] = "2x", [ImageUpscalerState.Sharpness] = "40" }), execution).Result.State;
        var path = Path.Combine(Path.GetTempPath(), "upscaler-shared-" + Guid.NewGuid().ToString("N") + ".png");
        using (var file = File.Create(path))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(4, 3, 96, 96, PixelFormats.Bgra32, null,
                Enumerable.Repeat(new byte[] { 20, 50, 100, 255 }, 12).SelectMany(b => b).ToArray(), 16)));
            encoder.Save(file);
        }
        var assets = new Assets();
        var context = new WindowsPluginViewContext(module, state, execution, true, false, 560, 340, 1, "#8B5CF6", () => { },
            (next, _) => state = next, _ => { }) { SharedAssets = assets };
        var local = new ImageUpscalerControl(context);
        Await(context.DeliverFilesAsync([new PluginDroppedFile(path, Path.GetFileName(path), new FileInfo(path).Length)]));
        Await(Invoke(local, "RunUpscaleAsync"));
        var localItem = Item(local);
        if (assets.Imports != 2 || localItem?.Result?.Succeeded != true || UpscaleSharedState.ReadOutput(state) is null)
            throw new Exception("User-initiated upscale did not publish both assets and result metadata.");
        var outputId = state.GetString(UpscaleSharedState.OutputAsset)!;
        var stale = module.ExecuteAsync(state, new PluginCommand(ImageUpscalerCommands.RecordUpscale,
            new Dictionary<string, string> { [UpscaleSharedState.InputAsset] = Guid.NewGuid().ToString("N"),
                [UpscaleSharedState.OutputAsset] = outputId, [UpscaleSharedState.OutputInfo] = state.GetString(UpscaleSharedState.OutputInfo)! }), execution).Result;
        if (stale.Succeeded) throw new Exception("An old result can overwrite a different input.");

        assets.ReceiveOnly = true;
        var remoteContext = new WindowsPluginViewContext(module, state, execution, true, false, 560, 340, 1, "#8B5CF6",
            () => throw new Exception("Receiving an upscale recorded undo."),
            (_, _) => throw new Exception("Receiving an upscale repeated processing or changed state."), _ => { }) { SharedAssets = assets };
        var remote = new ImageUpscalerControl(remoteContext);
        Await(Invoke(remote, "RestoreSharedFilesAsync"));
        var remoteItem = Item(remote);
        if (remoteItem?.Result is not { Succeeded: true, UpscaledWidth: 8, UpscaledHeight: 6 }
            || !File.ReadAllBytes(remoteItem.Result.OutputPath).SequenceEqual(File.ReadAllBytes(localItem!.Result!.OutputPath))
            || remoteContext.State.GetInt32(ImageUpscalerState.Sharpness) != 40 || assets.Imports != 2)
            throw new Exception("Remote upscale did not reconstruct the same output, dimensions and options.");
        local.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        remote.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Console.WriteLine("PASS: upscale executes on its author and reconstructs identical input/output/options on the receiver; stale results are rejected.");
    }
    private static UpscaleItem? Item(ImageUpscalerControl view) =>
        (UpscaleItem?)typeof(ImageUpscalerControl).GetField("_currentItem", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view);
    private static Task Invoke(ImageUpscalerControl view, string name) =>
        (Task)typeof(ImageUpscalerControl).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, null)!;
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
        public async ValueTask<PluginSharedAsset> ImportAsync(IPluginReadableFile source, CancellationToken cancellationToken = default)
        {
            if (ReceiveOnly) throw new Exception("Receiving a result attempted another upscale/import.");
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
        public Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken = default) =>
            throw new Exception("This fixture must not execute a cloud upscale.");
    }
}
