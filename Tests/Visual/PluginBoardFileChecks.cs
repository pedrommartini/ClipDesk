using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ClipDesk;
using ClipDesk.Core;
using ClipDesk.Models;
using ClipDesk.PluginSdk;
using ClipDesk.PluginSdk.Windows;
using ClipDesk.Services;
using ClipDesk.Views;

internal static class PluginBoardFileChecks
{
    public static void Run()
    {
        var dataRoot = AppEnvironment.DataRoot;
        Directory.CreateDirectory(dataRoot);
        var sourcePath = Path.Combine(dataRoot, "arquivo-existente.txt");
        File.WriteAllText(sourcePath, "arquivo local");

        var window = new MainWindow(startHidden: true);
        try
        {
            var workspace = (WorkspaceBoard)typeof(MainWindow)
                .GetField("_activeWorkspace", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var source = new BoardObject
            {
                Kind = BoardObjectKind.Plugin, PluginId = "clipdesk.file-test", PluginVersion = "1.0.0",
                X = 180, Y = 190, Width = 300, Height = 280,
                Content = new Dictionary<string, string>(), Style = new Dictionary<string, string>()
            };
            workspace.Objects.Add(source);
            var sourceView = (BoardObjectView)typeof(MainWindow)
                .GetMethod("AddBoardObjectView", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [source])!;

            var forwarded = false;
            sourceView.PluginHostActionRequested += (_, action) =>
                forwarded = action.Kind == PluginHostActionKind.AddFilesToBoard && action.BoardFiles?.Files.Count == 2;
            var request = new PluginBoardFileRequest([
                PluginBoardFile.FromPath(sourcePath, "Referência.txt"),
                PluginBoardFile.FromContent("Relatório.csv", "nome,valor\nClipDesk,1"u8.ToArray())
            ]);
            var action = PluginHostAction.AddFiles(request);
            typeof(BoardObjectView).GetMethod("HandlePluginHostAction", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(sourceView, [action]);
            if (!forwarded) throw new Exception("The board-file host action was not forwarded by the plugin view.");

            var manifest = new PluginManifest
            {
                ManifestVersion = 2, Id = source.PluginId, Name = "File test", Version = "1.0.0",
                Runtime = PluginRuntimes.PortableV2, PluginApiVersion = 2,
                Capabilities = [PluginCapabilities.BoardWidget, PluginCapabilities.AddFilesToBoard],
                Permissions = [PluginPermissions.FileRead, PluginPermissions.FileWrite]
            };
            var add = typeof(MainWindow).GetMethod("AddPluginFilesToBoardAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var count = AwaitWithDispatcher((Task<int>)add.Invoke(window, [sourceView, manifest, request])!);
            if (count != 2 || workspace.Items.Count != 2)
                throw new Exception("The plugin request did not create both file cards.");
            if (workspace.Items.Any(item => item.X < source.X + source.Width + 20)
                || workspace.Items[0].Y == workspace.Items[1].Y)
                throw new Exception("Plugin file cards were not positioned in a non-overlapping column beside the plugin.");
            if (workspace.Items[0].DisplayName != "Referência.txt"
                || workspace.Items[1].DisplayName != "Relatório.csv")
                throw new Exception("Plugin-provided display names were not preserved.");
            var generatedPath = workspace.Items.Single(item => item.DisplayName == "Relatório.csv").FilePaths.Single();
            if (!generatedPath.StartsWith(new StorageService().AssetsDirectory + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)
                || File.ReadAllText(generatedPath) != "nome,valor\nClipDesk,1")
                throw new Exception("Generated plugin content was not materialized in managed storage.");
            var canvas = (Canvas)window.FindName("WorkspaceCanvas");
            if (canvas.Children.OfType<ItemCard>().Count(card => workspace.Items.Contains(card.Item)) != 2)
                throw new Exception("File cards created by the plugin are not visible on the board.");

            var denied = manifest.WithPermissions([]);
            try
            {
                AwaitWithDispatcher((Task<int>)add.Invoke(window, [sourceView, denied, new PluginBoardFileRequest([
                    PluginBoardFile.FromPath(sourcePath)
                ])])!);
                throw new Exception("A plugin without file permissions added a local file.");
            }
            catch (InvalidDataException) { }
            if (workspace.Items.Count != 2) throw new Exception("A rejected plugin request changed the board.");

            Console.WriteLine("PASS: plugins add validated local and generated files beside their own board instance.");
        }
        finally { window.Close(); }
    }

    private static PluginManifest WithPermissions(this PluginManifest manifest, IReadOnlyList<string> permissions) => new()
    {
        ManifestVersion = manifest.ManifestVersion, Id = manifest.Id, Name = manifest.Name,
        Version = manifest.Version, Runtime = manifest.Runtime, PluginApiVersion = manifest.PluginApiVersion,
        Capabilities = manifest.Capabilities, Permissions = permissions
    };

    private static T AwaitWithDispatcher<T>(Task<T> task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => Application.Current.Dispatcher.BeginInvoke(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
        return task.GetAwaiter().GetResult();
    }
}
