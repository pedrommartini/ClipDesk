using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace ClipDesk.Services;

public sealed class PdfPreviewService
{
    public async Task<BitmapImage?> RenderFirstPageAsync(string? path, uint targetWidth = 1000)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

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
    }
}
