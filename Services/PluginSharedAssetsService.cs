using System.IO;
using System.Security.Cryptography;
using System.Windows.Threading;
using ClipDesk.Core;
using ClipDesk.PluginSdk;
using ClipDesk.PluginSdk.Windows;

namespace ClipDesk.Services;

/// <summary>Private board storage for one plugin instance. Paths remain inside the host.</summary>
public sealed class PluginSharedAssetsService(BoardObject instance, StorageService storage, Dispatcher dispatcher,
    Func<string?> ownerId, Func<bool> instanceExists, Action changed,
    Func<CloudAttachment, CancellationToken, Task> download) : IPluginSharedAssets
{
    // Larger files require the Drive grant flow, which is not yet part of this capability.
    public const long MaximumAssetBytes = 30_000_000;

    public async ValueTask<PluginSharedAsset> ImportAsync(IPluginReadableFile source, CancellationToken cancellationToken = default)
    {
        var name = Path.GetFileName(source.DisplayName);
        if (string.IsNullOrWhiteSpace(name)) throw new IOException("Nome de arquivo inválido.");
        var id = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(storage.AssetsDirectory, id);
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, name);
        var temporary = target + ".import";
        var registered = false;
        try
        {
            long size = 0;
            await using (var input = await source.OpenReadAsync(cancellationToken))
            await using (var output = File.Create(temporary))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    size += read;
                    if (size > MaximumAssetBytes) throw new IOException("O anexo compartilhado do plugin deve ter até 30 MB.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
            string hash;
            await using (var file = File.OpenRead(temporary))
                hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, target);
            var attachment = new CloudAttachment { Id = id, Name = name, Size = size, Sha256 = hash, LocalPath = target };
            await dispatcher.InvokeAsync(() =>
            {
                if (!instanceExists()) throw new InvalidOperationException("A instância do plugin não está mais nesta mesa.");
                attachment.OwnerId = ownerId() ?? "";
                instance.Attachments.Add(attachment);
                registered = true;
                instance.UpdatedAt = DateTimeOffset.UtcNow;
                changed();
            });
            return new PluginSharedAsset(id, name, size, hash);
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            // Delete only this newly created, known file; no recursive cleanup.
            if (!registered && File.Exists(target)) File.Delete(target);
            throw;
        }
    }

    public async ValueTask<IPluginReadableFile?> OpenAsync(string assetId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(assetId, out var parsed)) return null;
        var id = parsed.ToString("N");
        var attachment = await dispatcher.InvokeAsync(() =>
            instanceExists() ? instance.Attachments.FirstOrDefault(a => a.Id == id) : null);
        if (attachment is null) return null;
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.Combine(storage.AssetsDirectory, id, Path.GetFileName(attachment.Name));
        if (!await ValidAsync(path, attachment, cancellationToken))
        {
            if (!attachment.Uploaded) return null;
            await download(attachment, cancellationToken);
            if (!await ValidAsync(path, attachment, cancellationToken)) throw new IOException("O anexo do plugin falhou na verificação de integridade.");
        }
        return WindowsPluginFile.FromPath(path, attachment.Name);
    }

    private static async Task<bool> ValidAsync(string path, CloudAttachment attachment, CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != attachment.Size) return false;
        await using var file = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken))
            .Equals(attachment.Sha256, StringComparison.OrdinalIgnoreCase);
    }
}
