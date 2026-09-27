using System.IO;
using System.Windows;
using System.Windows.Threading;
using ClipDesk.Core;
using ClipDesk.PluginSdk;
using ClipDesk.PluginSdk.Windows;
using ClipDesk.Services;

internal static class PluginSharedAssetsChecks
{
    public static void Run(Application app)
    {
        var root = Path.Combine(Path.GetTempPath(), "ClipDesk-Shared-Assets", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("CLIPDESK_DEV_DATA_ROOT", Path.Combine(root, "owner"));
        var ownerStorage = new StorageService();
        var instance = new BoardObject { Kind = BoardObjectKind.Plugin, PluginId = "clipdesk.fixture" };
        var changes = 0;
        var owner = new PluginSharedAssetsService(instance, ownerStorage, app.Dispatcher, () => "owner", () => true,
            () => changes++, (_, _) => throw new Exception("Owner should not download its own managed asset."));
        var source = Path.Combine(root, "selected.bin");
        Directory.CreateDirectory(root); File.WriteAllBytes(source, [3, 5, 7, 11]);
        var asset = Await(owner.ImportAsync(WindowsPluginFile.FromPath(source)).AsTask());
        if (changes != 1 || instance.Attachments.Single().Id != asset.Id) throw new Exception("Import did not persist one instance asset.");
        instance.Content["inputAsset"] = asset.Id;
        instance.Attachments[0].Uploaded = true;
        instance.Attachments[0].StoragePath = "fixture/" + asset.Id;
        var entity = CloudProjection.ProjectBoardObject(instance, Guid.NewGuid().ToString("N"));
        if (entity.Data.ToJsonString().Contains(source, StringComparison.Ordinal)) throw new Exception("Shared asset exposes its source path.");

        Environment.SetEnvironmentVariable("CLIPDESK_DEV_DATA_ROOT", Path.Combine(root, "recipient"));
        var recipientStorage = new StorageService();
        var recipientInstance = CloudProjection.MaterializeBoardObject(entity);
        var downloads = 0;
        var recipient = new PluginSharedAssetsService(recipientInstance, recipientStorage, app.Dispatcher, () => "recipient", () => true,
            () => throw new Exception("Reading a remote asset must not create a document edit."), (attachment, cancellation) =>
            {
                cancellation.ThrowIfCancellationRequested(); downloads++;
                var destination = Path.Combine(recipientStorage.AssetsDirectory, attachment.Id, attachment.Name);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(instance.Attachments.Single().LocalPath!, destination);
                attachment.LocalPath = destination;
                return Task.CompletedTask;
            });
        var restored = Await(recipient.OpenAsync(recipientInstance.Content["inputAsset"]).AsTask())
            ?? throw new Exception("Recipient could not open the shared asset.");
        var stream = Await(restored.OpenReadAsync().AsTask());
        using (stream)
        {
            var bytes = new MemoryStream(); stream.CopyTo(bytes);
            if (!bytes.ToArray().SequenceEqual(new byte[] { 3, 5, 7, 11 })) throw new Exception("Shared asset bytes diverged.");
        }
        Await(recipient.OpenAsync(asset.Id).AsTask());
        if (downloads != 1 || Await(recipient.OpenAsync(Guid.NewGuid().ToString("N")).AsTask()) is not null)
            throw new Exception("Shared asset cache or instance isolation failed.");
        Console.WriteLine("PASS: plugin asset import, portable projection, recipient download, integrity and instance isolation.");
    }

    private static T Await<T>(Task<T> task)
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
