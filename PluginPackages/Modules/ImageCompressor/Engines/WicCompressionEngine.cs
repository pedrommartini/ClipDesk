using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipDesk.Plugin.ImageCompressor.Models;

namespace ClipDesk.Plugin.ImageCompressor.Engines;

/// <summary>
/// Motor nativo de compressão, redimensionamento e otimização de imagens baseado no Windows Imaging Component (WIC).
/// </summary>
public static class WicCompressionEngine
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif", ".tif", ".tiff"
    };

    public static bool IsSupportedExtension(string path)
    {
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && SupportedExtensions.Contains(ext);
    }

    /// <summary>
    /// Lê as dimensões (largura e altura) de uma imagem sem decodificar todos os pixels na memória.
    /// </summary>
    public static (int Width, int Height) ReadImageDimensions(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (decoder.Frames.Count > 0)
            {
                var frame = decoder.Frames[0];
                return (frame.PixelWidth, frame.PixelHeight);
            }
        }
        catch
        {
            // Fallback se não conseguir ler metadados rápidos
        }
        return (0, 0);
    }

    /// <summary>
    /// Gera uma miniatura leve congelada (DecodePixelWidth = 96) para visualização na fila sem consumir memória excessiva.
    /// </summary>
    public static BitmapSource? LoadThumbnail(string path, int decodeWidth = 96)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var image = new BitmapImage();
            image.BeginInit();
            image.DecodePixelWidth = decodeWidth;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Comprime um lote de itens da fila com paralelismo controlado (2 a 4 workers) via SemaphoreSlim.
    /// </summary>
    public static async Task ProcessBatchAsync(
        IReadOnlyList<BatchQueueItem> items,
        ImageCompressionOptions options,
        IProgress<(int Completed, int Total)>? progress = null,
        Action<BatchQueueItem, CompressionResult>? onItemProcessed = null,
        CancellationToken cancellationToken = default)
    {
        int workerCount = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);
        using var semaphore = new SemaphoreSlim(workerCount, workerCount);

        int total = items.Count;
        int completed = 0;

        var tasks = items.Select(async item =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                item.Status = BatchItemStatus.Compressing;

                var result = await Task.Run(() => CompressImage(item.SourcePath, options, cancellationToken), cancellationToken)
                    .ConfigureAwait(false);

                item.ApplyResult(result);
                onItemProcessed?.Invoke(item, result);

                int currentCompleted = Interlocked.Increment(ref completed);
                progress?.Report((currentCompleted, total));
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Executa o pipeline de compressão síncrono para uma única imagem no thread de trabalho.
    /// </summary>
    public static CompressionResult CompressImage(
        string sourcePath,
        ImageCompressionOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(sourcePath))
        {
            return CompressionResult.Failed(sourcePath, "Arquivo de origem não encontrado.");
        }

        try
        {
            var sourceInfo = new FileInfo(sourcePath);
            long originalBytes = sourceInfo.Length;

            // 1. Carrega a imagem com BitmapCacheOption.OnLoad para liberar o arquivo imediatamente
            using var fileStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(
                fileStream,
                BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count == 0)
            {
                return CompressionResult.Failed(sourcePath, "Nenhum quadro de imagem pôde ser decodificado.");
            }

            var sourceFrame = decoder.Frames[0];
            int originalWidth = sourceFrame.PixelWidth;
            int originalHeight = sourceFrame.PixelHeight;

            // 2. Orientação EXIF antes de qualquer transformação
            BitmapSource currentBitmap = ApplyExifOrientation(sourceFrame);

            // 3. Redimensionamento proporcional bicúbico via TransformedBitmap
            int maxLimit = options.Dimension switch
            {
                MaxDimension.FullHd => 1920,
                MaxDimension.Hd => 1280,
                _ => 0
            };

            if (maxLimit > 0 && (currentBitmap.PixelWidth > maxLimit || currentBitmap.PixelHeight > maxLimit))
            {
                double scale = Math.Min((double)maxLimit / currentBitmap.PixelWidth, (double)maxLimit / currentBitmap.PixelHeight);
                var scaled = new TransformedBitmap(currentBitmap, new ScaleTransform(scale, scale));
                scaled.Freeze();
                currentBitmap = scaled;
            }

            int outputWidth = currentBitmap.PixelWidth;
            int outputHeight = currentBitmap.PixelHeight;

            // 4. Determina formato de saída
            string targetExt = DetermineTargetExtension(sourcePath, options.Format);
            bool isJpeg = targetExt is ".jpg" or ".jpeg";

            BitmapEncoder encoder;
            if (isJpeg)
            {
                // Para JPEG: se houver transparência, compõe sobre fundo branco sólido
                currentBitmap = PrepareForJpeg(currentBitmap);
                var jpegEncoder = new JpegBitmapEncoder
                {
                    QualityLevel = Math.Clamp(options.Quality, 10, 100)
                };
                encoder = jpegEncoder;
            }
            else
            {
                // Para PNG: Deflate não-entrelaçado (mais compacto) e redução de 32bpp para 24bpp se for opaco
                currentBitmap = OptimizePngPixelFormat(currentBitmap);
                var pngEncoder = new PngBitmapEncoder
                {
                    Interlace = PngInterlaceOption.Off
                };
                encoder = pngEncoder;
            }

            // 5. Adiciona o quadro sem metadados para descarte de cabeçalhos EXIF inchados
            encoder.Frames.Add(BitmapFrame.Create(currentBitmap));

            // 6. Grava saída
            Directory.CreateDirectory(options.OutputDirectory);
            string baseName = Path.GetFileNameWithoutExtension(sourcePath);
            string outputFileName = $"{baseName}_compressed{targetExt}";
            string outputPath = Path.Combine(options.OutputDirectory, outputFileName);

            // Garante nome único se arquivo já existir
            int counter = 1;
            while (File.Exists(outputPath))
            {
                outputFileName = $"{baseName}_compressed_{counter}{targetExt}";
                outputPath = Path.Combine(options.OutputDirectory, outputFileName);
                counter++;
            }

            using (var outStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                encoder.Save(outStream);
            }

            long compressedBytes = new FileInfo(outputPath).Length;

            return CompressionResult.Succeeded(
                sourcePath,
                outputPath,
                originalBytes,
                compressedBytes,
                originalWidth,
                originalHeight,
                outputWidth,
                outputHeight);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CompressionResult.Failed(sourcePath, ex.Message);
        }
    }

    /// <summary>
    /// Detecta a orientação EXIF (tag 0x0112) e aplica a rotação correspondente.
    /// </summary>
    private static BitmapSource ApplyExifOrientation(BitmapFrame frame)
    {
        ushort orientation = 1;
        try
        {
            if (frame.Metadata is BitmapMetadata meta && meta.ContainsQuery("/app1/ifd/{ushort=274}"))
            {
                var queryResult = meta.GetQuery("/app1/ifd/{ushort=274}");
                if (queryResult is ushort val)
                {
                    orientation = val;
                }
            }
        }
        catch
        {
            orientation = 1;
        }

        Transform? transform = orientation switch
        {
            3 => new RotateTransform(180),
            6 => new RotateTransform(90),
            8 => new RotateTransform(270),
            2 => new ScaleTransform(-1, 1),
            4 => new ScaleTransform(1, -1),
            _ => null
        };

        if (transform == null)
        {
            return frame;
        }

        var transformed = new TransformedBitmap(frame, transform);
        transformed.Freeze();
        return transformed;
    }

    /// <summary>
    /// Prepara o bitmap para codificação JPEG: se contiver canal alfa, compõe sobre um fundo branco puro.
    /// Utiliza DrawingVisual + RenderTargetBitmap, com fallback direto para software compositing.
    /// </summary>
    public static BitmapSource PrepareForJpeg(BitmapSource source)
    {
        bool hasAlpha = source.Format == PixelFormats.Bgra32 ||
                        source.Format == PixelFormats.Pbgra32 ||
                        source.Format == PixelFormats.Rgba64 ||
                        source.Format == PixelFormats.Prgba64 ||
                        source.Format == PixelFormats.Rgba128Float ||
                        source.Format == PixelFormats.Prgba128Float;

        if (!hasAlpha)
        {
            if (source.Format != PixelFormats.Bgr24)
            {
                var converted = new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0);
                converted.Freeze();
                return converted;
            }
            return source;
        }

        try
        {
            // DrawingVisual + RenderTargetBitmap compositing over solid white
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
                dc.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            }

            var rtb = new RenderTargetBitmap(
                source.PixelWidth,
                source.PixelHeight,
                source.DpiX > 0 ? source.DpiX : 96,
                source.DpiY > 0 ? source.DpiY : 96,
                PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();

            var converted = new FormatConvertedBitmap(rtb, PixelFormats.Bgr24, null, 0);
            converted.Freeze();
            return converted;
        }
        catch
        {
            // Fallback de software para ambiente onde RenderTargetBitmap não puder renderizar
            return SoftwareCompositeOverWhite(source);
        }
    }

    /// <summary>
    /// Composição em software por varredura de bytes de pixels sobre fundo branco.
    /// </summary>
    private static BitmapSource SoftwareCompositeOverWhite(BitmapSource source)
    {
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        bgra.Freeze();

        int width = bgra.PixelWidth;
        int height = bgra.PixelHeight;
        int srcStride = width * 4;
        byte[] srcPixels = new byte[srcStride * height];
        bgra.CopyPixels(srcPixels, srcStride, 0);

        int dstStride = width * 3;
        byte[] dstPixels = new byte[dstStride * height];

        for (int i = 0, j = 0; i < srcPixels.Length; i += 4, j += 3)
        {
            byte b = srcPixels[i];
            byte g = srcPixels[i + 1];
            byte r = srcPixels[i + 2];
            byte a = srcPixels[i + 3];

            if (a == 255)
            {
                dstPixels[j] = b;
                dstPixels[j + 1] = g;
                dstPixels[j + 2] = r;
            }
            else if (a == 0)
            {
                dstPixels[j] = 255;
                dstPixels[j + 1] = 255;
                dstPixels[j + 2] = 255;
            }
            else
            {
                double alpha = a / 255.0;
                double invAlpha = 1.0 - alpha;
                dstPixels[j] = (byte)Math.Clamp((b * alpha) + (255.0 * invAlpha), 0, 255);
                dstPixels[j + 1] = (byte)Math.Clamp((g * alpha) + (255.0 * invAlpha), 0, 255);
                dstPixels[j + 2] = (byte)Math.Clamp((r * alpha) + (255.0 * invAlpha), 0, 255);
            }
        }

        var result = BitmapSource.Create(
            width,
            height,
            source.DpiX > 0 ? source.DpiX : 96,
            source.DpiY > 0 ? source.DpiY : 96,
            PixelFormats.Bgr24,
            null,
            dstPixels,
            dstStride);
        result.Freeze();
        return result;
    }

    /// <summary>
    /// Otimiza o formato do bitmap para PNG: se for 32bpp totalmente opaco, converte para 24bpp Bgr24 economizando 25% de dados.
    /// </summary>
    private static BitmapSource OptimizePngPixelFormat(BitmapSource source)
    {
        if (source.Format == PixelFormats.Bgr24)
        {
            return source;
        }

        if (source.Format == PixelFormats.Bgra32 || source.Format == PixelFormats.Pbgra32)
        {
            // Verifica se possui pixels translúcidos
            int width = source.PixelWidth;
            int height = source.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            source.CopyPixels(pixels, stride, 0);

            bool hasTransparency = false;
            for (int i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] < 255)
                {
                    hasTransparency = true;
                    break;
                }
            }

            if (!hasTransparency)
            {
                var bgr24 = new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0);
                bgr24.Freeze();
                return bgr24;
            }
        }

        return source;
    }

    private static string DetermineTargetExtension(string sourcePath, OutputFormat format)
    {
        return format switch
        {
            OutputFormat.Jpeg => ".jpg",
            OutputFormat.Png => ".png",
            _ => (Path.GetExtension(sourcePath).ToLowerInvariant()) switch
            {
                ".png" => ".png",
                ".jpg" or ".jpeg" => ".jpg",
                _ => ".jpg"
            }
        };
    }

    /// <summary>
    /// Realiza amostragem rápida e genuína em memória (sem gravar em disco) para medir o tamanho projetado exato do arquivo.
    /// </summary>
    public static long SampleEncodeInMemory(string sourcePath, ImageCompressionOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.OnLoad);

        if (decoder.Frames.Count == 0) return new FileInfo(sourcePath).Length;

        var frame = decoder.Frames[0];
        BitmapSource bitmap = ApplyExifOrientation(frame);

        int maxLimit = options.Dimension switch
        {
            MaxDimension.FullHd => 1920,
            MaxDimension.Hd => 1280,
            _ => 0
        };

        if (maxLimit > 0 && (bitmap.PixelWidth > maxLimit || bitmap.PixelHeight > maxLimit))
        {
            double scale = Math.Min((double)maxLimit / bitmap.PixelWidth, (double)maxLimit / bitmap.PixelHeight);
            var scaled = new TransformedBitmap(bitmap, new ScaleTransform(scale, scale));
            scaled.Freeze();
            bitmap = scaled;
        }

        string targetExt = DetermineTargetExtension(sourcePath, options.Format);
        bool isJpeg = targetExt is ".jpg" or ".jpeg";

        BitmapEncoder encoder;
        if (isJpeg)
        {
            bitmap = PrepareForJpeg(bitmap);
            encoder = new JpegBitmapEncoder
            {
                QualityLevel = Math.Clamp(options.Quality, 10, 100)
            };
        }
        else
        {
            bitmap = OptimizePngPixelFormat(bitmap);
            encoder = new PngBitmapEncoder
            {
                Interlace = PngInterlaceOption.Off
            };
        }

        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.Length;
    }

    /// <summary>
    /// Calcula em tempo real a projeção do tamanho final da fila antes de disparar o processamento completo.
    /// </summary>
    public static async Task<(long ProjectedBytes, double ReductionPercent)> EstimateProjectedSizeAsync(
        IReadOnlyList<BatchQueueItem> items,
        ImageCompressionOptions options,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0) return (0, 0.0);

        return await Task.Run(() =>
        {
            long totalOriginalBytes = items.Sum(i => i.OriginalSize);
            if (totalOriginalBytes <= 0) return (0, 0.0);

            // Amostra até 3 itens representativos da fila para cálculo instantâneo sem sobrecarregar a UI
            int sampleCount = Math.Min(items.Count, 3);
            long sampleOriginalBytes = 0;
            long sampleCompressedBytes = 0;

            for (int i = 0; i < sampleCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = items[i];
                long orig = item.OriginalSize;
                sampleOriginalBytes += orig;
                try
                {
                    long comp = SampleEncodeInMemory(item.SourcePath, options, cancellationToken);
                    sampleCompressedBytes += comp;
                }
                catch
                {
                    sampleCompressedBytes += orig;
                }
            }

            if (sampleOriginalBytes <= 0) return (totalOriginalBytes, 0.0);

            double ratio = (double)sampleCompressedBytes / sampleOriginalBytes;
            long projectedTotalBytes = (long)Math.Round(totalOriginalBytes * ratio);
            double reductionPercent = Math.Clamp((totalOriginalBytes - projectedTotalBytes) / (double)totalOriginalBytes * 100.0, 0.0, 100.0);

            return (projectedTotalBytes, reductionPercent);
        }, cancellationToken).ConfigureAwait(false);
    }
}
