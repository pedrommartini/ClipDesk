using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipDesk.PluginSdk;
using ClipDesk.PluginSdk.Windows;
using ClipDesk.Services;

internal static class PluginCollaboratorChecks
{
    private static readonly string[] PluginNames = ["fileconverter", "imagecompressor", "imageupscaler"];

    public static void Run()
    {
        // Loaded plugin assemblies remain locked until the test process exits, so keep them under build output.
        var root = Path.Combine(AppContext.BaseDirectory, "collaborator-checks");
        var installedRoot = Path.Combine(root, "plugins");
        Directory.CreateDirectory(installedRoot);
        {
            foreach (var name in PluginNames)
            {
                var id = "clipdesk." + name;
                var pluginRoot = Path.Combine(installedRoot, id);
                var versionRoot = Path.Combine(pluginRoot, "1.0.0");
                ZipFile.ExtractToDirectory(Path.Combine(AppContext.BaseDirectory, "collaborator-fixtures", id + "-1.0.0.zip"), versionRoot, overwriteFiles: true);
                File.WriteAllText(Path.Combine(pluginRoot, "current-version.txt"), "1.0.0");
            }

            var fixture = Path.Combine(root, "clipdesk-check-" + Guid.NewGuid().ToString("N") + ".png");
            var pixels = Enumerable.Repeat((byte)127, 32 * 32 * 4).ToArray();
            var bitmap = BitmapSource.Create(32, 32, 96, 96, PixelFormats.Bgra32, null, pixels, 32 * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(fixture)) encoder.Save(output);

            var catalog = new PluginCatalogService(Path.Combine(root, "no-bundled"), installedRoot,
                Version.Parse(UpdateService.CurrentVersion));
            var loader = new WindowsPluginLoader(catalog);
            foreach (var name in PluginNames)
            {
                var id = "clipdesk." + name;
                var package = catalog.GetInstalledPackage(id) ?? throw new Exception(id + " was not installed from its published package.");
                WindowsPluginViewContext? context = null;
                PluginState? changedState = null;
                var body = loader.CreateBody(id, new PluginState(package.Manifest.DefaultContent), true, false,
                    package.Manifest.DefaultSize.Width, package.Manifest.DefaultSize.Height, 1,
                    package.Manifest.AccentColor, () => { }, (state, _) => changedState = state, _ => { }, c => context = c);
                if (body is null || context?.AcceptsFileDrops != true)
                    throw new Exception(id + " did not load its file-drop interface with the current Windows SDK.");

                if (name is "imagecompressor" or "imageupscaler")
                    VerifyMinimalSlider(body, context);

                AwaitWithDispatcher(context.DeliverFilesAsync([new PluginDroppedFile(fixture, Path.GetFileName(fixture), new FileInfo(fixture).Length)]));
                if (name == "fileconverter")
                {
                    if (changedState?.GetString("sourcePath") != fixture)
                        throw new Exception("The file converter did not accept the dropped image.");
                    var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "ClipDesk.Plugin.FileConverter");
                    var engine = assembly.GetType("ClipDesk.Plugin.FileConverter.Engines.UniversalConversionCoordinator")!;
                    var conversion = (Task<string>)engine.GetMethod("ConvertFileAsync")!.Invoke(null,
                        [fixture, "jpg", 85, 32, true, 192000, CancellationToken.None])!;
                    var result = AwaitWithDispatcher(conversion);
                    if (!File.Exists(result) || new FileInfo(result).Length == 0)
                        throw new Exception("The file converter produced no image.");
                    File.Delete(result);
                }
                else if (name == "imagecompressor")
                {
                    var assembly = body.GetType().Assembly;
                    var options = Activator.CreateInstance(assembly.GetType("ClipDesk.Plugin.ImageCompressor.Engines.ImageCompressionOptions")!)!;
                    options.GetType().GetProperty("OutputDirectory")!.SetValue(options, Path.Combine(root, "compressed"));
                    var engine = assembly.GetType("ClipDesk.Plugin.ImageCompressor.Engines.WicCompressionEngine")!;
                    var result = engine.GetMethod("CompressImage")!.Invoke(null, [fixture, options, CancellationToken.None])!;
                    if (result.GetType().GetProperty("Success")?.GetValue(result) is not true
                        || result.GetType().GetProperty("OutputPath")?.GetValue(result) is not string path || !File.Exists(path))
                        throw new Exception("The image compressor did not produce an image.");
                }
                else
                {
                    var assembly = body.GetType().Assembly;
                    var options = Activator.CreateInstance(assembly.GetType("ClipDesk.Plugin.ImageUpscaler.Engines.ImageUpscaleOptions")!)!;
                    options.GetType().GetProperty("OutputDirectory")!.SetValue(options, Path.Combine(root, "upscaled"));
                    var engine = assembly.GetType("ClipDesk.Plugin.ImageUpscaler.Engines.WicUpscaleEngine")!;
                    var upscale = (Task)engine.GetMethod("UpscaleAsync")!.Invoke(null, [fixture, options, null, CancellationToken.None])!;
                    AwaitWithDispatcher(upscale);
                    var result = upscale.GetType().GetProperty("Result")!.GetValue(upscale)!;
                    if (result.GetType().GetProperty("Succeeded")?.GetValue(result) is not true
                        || result.GetType().GetProperty("OutputPath")?.GetValue(result) is not string path || !File.Exists(path))
                        throw new Exception("The image upscaler did not produce an image.");
                }
            }
            Console.WriteLine("PASS: collaborator file plugins load, accept drops and convert, compress and upscale a fixture image.");
        }
    }

    private static void VerifyMinimalSlider(FrameworkElement body, WindowsPluginViewContext context)
    {
        static PluginSlider? Find(DependencyObject root)
        {
            if (root is PluginSlider slider) return slider;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
                if (Find(VisualTreeHelper.GetChild(root, index)) is { } found) return found;
            return null;
        }

        var slider = Find(body) ?? throw new Exception("A collaborator plugin no longer uses the global SDK slider.");
        var track = (Border)slider.Children[0];
        var thumb = (Border)slider.Children[2];
        context.UpdateLayout(context.Width * 1.6, context.Height * 1.6, 1.6);
        if (track.Height > 4 || thumb.Width > 14 || thumb.Height > 14 || thumb.BorderThickness.Left > 2)
            throw new Exception("The Minimal slider grows back to the old design on an enlarged plugin card.");
        context.UpdateLayout(context.Width * 2, context.Height * 2, 3.2);
        if (track.Height > 4 || thumb.Width > 14 || slider.Height < 28)
            throw new Exception("The Minimal slider loses its compact visuals or pointer target at large scales.");
        context.UpdateViewportZoom(.54);
        if (Math.Abs(track.Height * context.ViewportZoom - 4) > .01
            || Math.Abs(thumb.Width * context.ViewportZoom - 14) > .01
            || Math.Abs(thumb.BorderThickness.Left * context.ViewportZoom - 2) > .01
            || Math.Abs(slider.Height * context.ViewportZoom - 34) > .01)
            throw new Exception("The Minimal slider no longer matches its HTML dimensions at 54% board zoom.");
        context.UpdateViewportZoom(1);
        if (Math.Abs(track.Height - 4) > .01 || Math.Abs(thumb.Width - 14) > .01)
            throw new Exception("The Minimal slider did not return to its HTML dimensions at 100% board zoom.");
    }

    private static void AwaitWithDispatcher(Task task)
    {
        if (task.IsCompleted) { task.GetAwaiter().GetResult(); return; }
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => Application.Current.Dispatcher.BeginInvoke(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }

    private static T AwaitWithDispatcher<T>(Task<T> task)
    {
        AwaitWithDispatcher((Task)task);
        return task.GetAwaiter().GetResult();
    }
}
