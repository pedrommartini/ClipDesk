using System.IO;

namespace ClipDesk.PluginSdk.Windows;

/// <summary>Materializes a shared file for Windows engines. The returned path is local and must not be saved in PluginState.</summary>
public static class WindowsSharedAssetFiles
{
    public static async Task<string?> MaterializeAsync(WindowsPluginViewContext context, string? assetId,
        CancellationToken cancellationToken = default)
    {
        if (context.SharedAssets is null || !Guid.TryParse(assetId, out var id)) return null;
        await using var asset = await context.SharedAssets.OpenAsync(id.ToString("N"), cancellationToken);
        if (asset is null) return null;
        var directory = Path.Combine(WindowsPluginFiles.DefaultDocumentDirectory(context.Module.Id), "shared", id.ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Path.GetFileName(asset.DisplayName));
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var input = await asset.OpenReadAsync(cancellationToken))
            await using (var output = File.Create(temporary))
                await input.CopyToAsync(output, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
            return path;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
