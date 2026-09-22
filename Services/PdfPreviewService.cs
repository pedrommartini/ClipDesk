using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using System.Collections.Concurrent;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace ClipDesk.Services;

public sealed class PdfPreviewService
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<BitmapImage?>>> PreviewCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim RenderSlots = new(2);

    public async Task<BitmapImage?> RenderFirstPageAsync(string? path, uint targetWidth = 1000)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        var info = new FileInfo(path);
        var key = $"{Path.GetFullPath(path)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{targetWidth}";
        if (PreviewCache.Count >= 24)
            foreach (var cached in PreviewCache.Where(entry => entry.Value.IsValueCreated && entry.Value.Value.IsCompleted).Take(PreviewCache.Count - 23))
                PreviewCache.TryRemove(cached.Key, out _);
        var preview = PreviewCache.GetOrAdd(key, _ => new Lazy<Task<BitmapImage?>>(() => RenderUncachedAsync(path, targetWidth), LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            var result = await preview.Value;
            // A burst can queue more than 24 unfinished previews. Trim again on completion.
            if (PreviewCache.Count > 24) PreviewCache.TryRemove(key, out _);
            return result;
        }
        catch
        {
            PreviewCache.TryRemove(key, out _);
            return null;
        }
    }

    private static async Task<BitmapImage?> RenderUncachedAsync(string path, uint targetWidth)
    {
        await RenderSlots.WaitAsync();
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
            var document = await PdfDocument.LoadFromFileAsync(file);
            if (document.PageCount == 0) return null;

            using var page = document.GetPage(0);
            var ratio = page.Dimensions.MediaBox.Height / Math.Max(1, page.Dimensions.MediaBox.Width);
            using var output = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(output, new PdfPageRenderOptions
            {
                DestinationWidth = targetWidth,
                DestinationHeight = (uint)Math.Max(1, Math.Round(targetWidth * ratio))
            });
            output.Seek(0);
            using var source = output.AsStreamForRead();
            using var copy = new MemoryStream();
            await source.CopyToAsync(copy);
            copy.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = copy;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or COMException)
        {
            return null;
        }
        finally { RenderSlots.Release(); }
    }
}
