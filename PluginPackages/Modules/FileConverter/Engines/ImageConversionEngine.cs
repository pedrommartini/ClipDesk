using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipDesk.Plugin.FileConverter.Services;

namespace ClipDesk.Plugin.FileConverter.Engines;

public static class ImageConversionEngine
{
    public static async Task<string> ConvertImageAsync(
        string sourcePath,
        string targetFormat,
        int quality = 85,
        int icoSize = 32,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outDir = Path.Combine(Path.GetTempPath(), "ClipDesk", "Converted");
            Directory.CreateDirectory(outDir);

            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);
            targetFormat = targetFormat.TrimStart('.').ToLowerInvariant();

            // Abre o arquivo de imagem via BitmapDecoder
            using var fileStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(fileStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];

            // 1. PDF
            if (targetFormat == "pdf")
            {
                byte[] imgBytes;
                string formatForPdf;
                // Se já for JPEG, preserva os bytes diretamente para o PDF
                string srcExt = Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant();
                if (srcExt is "jpg" or "jpeg")
                {
                    fileStream.Position = 0;
                    using var ms = new MemoryStream();
                    fileStream.CopyTo(ms);
                    imgBytes = ms.ToArray();
                    formatForPdf = "jpeg";
                }
                else
                {
                    // Converte para JPEG de alta qualidade para embutir no PDF
                    using var ms = new MemoryStream();
                    var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
                    encoder.Frames.Add(BitmapFrame.Create(frame));
                    encoder.Save(ms);
                    imgBytes = ms.ToArray();
                    formatForPdf = "jpeg";
                }

                var pdfBytes = LightweightPdfBuilder.CreateImagePdf(imgBytes, formatForPdf, frame.PixelWidth, frame.PixelHeight);
                string outPath = Path.Combine(outDir, $"{fileNameWithoutExt}.pdf");
                File.WriteAllBytes(outPath, pdfBytes);
                return outPath;
            }

            // 2. Base64 / TXT
            if (targetFormat == "txt")
            {
                fileStream.Position = 0;
                using var ms = new MemoryStream();
                fileStream.CopyTo(ms);
                var base64 = Convert.ToBase64String(ms.ToArray());
                string mime = targetFormat switch
                {
                    "png" => "image/png",
                    "jpg" or "jpeg" => "image/jpeg",
                    "webp" => "image/webp",
                    "gif" => "image/gif",
                    _ => "image/png"
                };
                string dataUri = $"data:{mime};base64,{base64}";
                string outPath = Path.Combine(outDir, $"{fileNameWithoutExt}_base64.txt");
                File.WriteAllText(outPath, dataUri);
                return outPath;
            }

            // 3. ICO (Windows Icon multi-size)
            if (targetFormat == "ico")
            {
                string outPath = Path.Combine(outDir, $"{fileNameWithoutExt}.ico");
                CreateIcoFile(frame, outPath, icoSize);
                return outPath;
            }

            // 4. Encoders padrão WIC (PNG, JPG, BMP, GIF, TIFF)
            BitmapEncoder encoderObj = targetFormat switch
            {
                "png" => new PngBitmapEncoder(),
                "jpg" or "jpeg" => new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 10, 100) },
                "bmp" => new BmpBitmapEncoder(),
                "gif" => new GifBitmapEncoder(),
                "tiff" or "tif" => new TiffBitmapEncoder(),
                _ => new PngBitmapEncoder()
            };

            encoderObj.Frames.Add(BitmapFrame.Create(frame));

            string outputPath = Path.Combine(outDir, $"{fileNameWithoutExt}.{targetFormat}");
            using (var outStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                encoderObj.Save(outStream);
            }

            return outputPath;
        }, cancellationToken);
    }

    private static void CreateIcoFile(BitmapSource source, string outputPath, int requestedSize)
    {
        // Se requestedSize for 0, cria multi-resolução [16, 32, 48, 64, 128, 256]
        int[] sizes = requestedSize switch
        {
            16 => new[] { 16 },
            32 => new[] { 32 },
            48 => new[] { 48 },
            64 => new[] { 64 },
            128 => new[] { 128 },
            256 => new[] { 256 },
            _ => new[] { 16, 32, 48, 64, 128, 256 }
        };

        var imagesData = new List<(int width, int height, byte[] pngData)>();

        foreach (var size in sizes)
        {
            var resized = new TransformedBitmap(source, new ScaleTransform(
                (double)size / source.PixelWidth,
                (double)size / source.PixelHeight));

            using var ms = new MemoryStream();
            var pngEncoder = new PngBitmapEncoder();
            pngEncoder.Frames.Add(BitmapFrame.Create(resized));
            pngEncoder.Save(ms);
            imagesData.Add((size, size, ms.ToArray()));
        }

        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // ICONDIR Header
        bw.Write((short)0); // Reserved
        bw.Write((short)1); // Type 1 = Icon
        bw.Write((short)imagesData.Count); // Image count

        int offset = 6 + (imagesData.Count * 16);

        // ICONDIRENTRY entries
        foreach (var (w, h, data) in imagesData)
        {
            bw.Write((byte)(w >= 256 ? 0 : w));
            bw.Write((byte)(h >= 256 ? 0 : h));
            bw.Write((byte)0); // Color count
            bw.Write((byte)0); // Reserved
            bw.Write((short)1); // Color planes
            bw.Write((short)32); // Bits per pixel
            bw.Write(data.Length); // Image size
            bw.Write(offset); // Image offset
            offset += data.Length;
        }

        // Image data (PNG streams)
        foreach (var (_, _, data) in imagesData)
        {
            bw.Write(data);
        }
    }
}
