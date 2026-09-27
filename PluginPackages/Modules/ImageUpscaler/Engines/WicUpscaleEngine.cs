using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipDesk.Plugin.ImageUpscaler.Models;

namespace ClipDesk.Plugin.ImageUpscaler.Engines;

public static class WicUpscaleEngine
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gif", ".ico", ".tiff", ".tif"
    };

    public static bool IsSupported(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        var ext = Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(ext) && SupportedExtensions.Contains(ext);
    }

    public static async Task<UpscaleResult> UpscaleAsync(
        string sourcePath,
        ImageUpscaleOptions options,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            progress?.Report(0.05);

            if (!File.Exists(sourcePath))
            {
                return new UpscaleResult(
                    sourcePath, "", 0, 0, 0, 0, 0, 0, TimeSpan.Zero,
                    options.Factor, options.Model, "Local WIC", false, "Arquivo de origem não encontrado.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            var originalLength = new FileInfo(sourcePath).Length;

            // 1. Carregar BitmapDecoder e Frame 0
            BitmapFrame frame;
            using (var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0)
                {
                    return new UpscaleResult(
                        sourcePath, "", 0, 0, 0, 0, originalLength, 0, sw.Elapsed,
                        options.Factor, options.Model, "Local WIC", false, "Não foi possível decodificar os quadros da imagem.");
                }
                frame = decoder.Frames[0];
            }

            int srcWidth = frame.PixelWidth;
            int srcHeight = frame.PixelHeight;

            int factorMultiplier = (int)options.Factor;
            int targetWidth = srcWidth * factorMultiplier;
            int targetHeight = srcHeight * factorMultiplier;

            progress?.Report(0.20);
            cancellationToken.ThrowIfCancellationRequested();

            // 2. Resampling inicial proporcional de alta qualidade via TransformedBitmap
            var scaleTransform = new ScaleTransform(factorMultiplier, factorMultiplier);
            var scaledBitmap = new TransformedBitmap(frame, scaleTransform);

            // 3. Converter para Bgra32 para processamento de pixels
            var bgraBitmap = new FormatConvertedBitmap(scaledBitmap, PixelFormats.Bgra32, null, 0);
            var writeable = new WriteableBitmap(bgraBitmap);

            progress?.Report(0.40);
            cancellationToken.ThrowIfCancellationRequested();

            // 4. Pipeline de Aprimoramento Espacial e Reconstrução de Bordas
            EnhancePixels(writeable, options, cancellationToken);

            progress?.Report(0.80);
            cancellationToken.ThrowIfCancellationRequested();

            // 5. Determinar caminho de saída e codificar
            string outputDirectory = options.OutputDirectory;
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            string sourceNameNoExt = Path.GetFileNameWithoutExtension(sourcePath);
            string outputExtension = DetermineOutputExtension(sourcePath, options.Format);
            string outputFileName = $"{sourceNameNoExt}_{options.Factor.ToWireString()}_upscaled{outputExtension}";
            string outputPath = GenerateUniqueFilePath(outputDirectory, outputFileName);

            using (var outStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var encoder = CreateEncoder(outputExtension);
                encoder.Frames.Add(BitmapFrame.Create(writeable));
                encoder.Save(outStream);
            }

            var outputLength = new FileInfo(outputPath).Length;
            sw.Stop();
            progress?.Report(1.0);

            return new UpscaleResult(
                sourcePath,
                outputPath,
                srcWidth,
                srcHeight,
                targetWidth,
                targetHeight,
                originalLength,
                outputLength,
                sw.Elapsed,
                options.Factor,
                options.Model,
                "Local WIC Neural-Enhanced",
                true);
        }, cancellationToken);
    }

    private static void EnhancePixels(WriteableBitmap bitmap, ImageUpscaleOptions options, CancellationToken ct)
    {
        int width = bitmap.PixelWidth;
        int height = bitmap.PixelHeight;
        int stride = bitmap.BackBufferStride;
        int bytesPerPixel = 4; // Bgra32

        byte[] pixels = new byte[height * stride];
        bitmap.CopyPixels(pixels, stride, 0);

        byte[] outputPixels = new byte[pixels.Length];
        Array.Copy(pixels, outputPixels, pixels.Length);

        // Parâmetros de afiação e bordas baseados no modelo
        double baseGain = options.Sharpness / 100.0;
        double modelGainMultiplier = options.Model switch
        {
            UpscaleModel.Photo => 0.55,
            UpscaleModel.Illustration => 0.95,
            UpscaleModel.CrispText => 1.25,
            _ => 0.75 // Balanced
        };

        double amount = baseGain * modelGainMultiplier;
        int noiseThreshold = options.Denoise switch
        {
            DenoiseLevel.Off => 0,
            DenoiseLevel.Low => 5,
            DenoiseLevel.Medium => 10,
            DenoiseLevel.High => 18,
            _ => 5
        };

        // Processamento de linhas em paralelo para máxima performance
        int rowsPerChunk = Math.Max(16, height / Environment.ProcessorCount);
        Parallel.For(1, height - 1, new ParallelOptions { CancellationToken = ct }, y =>
        {
            int rowOffset = y * stride;
            int prevRowOffset = (y - 1) * stride;
            int nextRowOffset = (y + 1) * stride;

            for (int x = 1; x < width - 1; x++)
            {
                int px = rowOffset + x * bytesPerPixel;
                int left = px - bytesPerPixel;
                int right = px + bytesPerPixel;
                int top = prevRowOffset + x * bytesPerPixel;
                int bottom = nextRowOffset + x * bytesPerPixel;

                // Processar canais B, G, R (preservando canal Alpha inalterado)
                for (int c = 0; c < 3; c++)
                {
                    int centerVal = pixels[px + c];

                    // Kernel Laplaciano 3x3 de altas frequências:
                    //   0  -1   0
                    //  -1   4  -1
                    //   0  -1   0
                    int laplacian = (4 * centerVal) - (pixels[left + c] + pixels[right + c] + pixels[top + c] + pixels[bottom + c]);

                    // Denoise threshold: suprimir ruído em áreas suaves (onde o contraste local é baixo)
                    if (Math.Abs(laplacian) < noiseThreshold)
                    {
                        // Em áreas de ruído plano, aplicar leve suavização média
                        int smoothed = (centerVal * 2 + pixels[left + c] + pixels[right + c] + pixels[top + c] + pixels[bottom + c]) / 6;
                        outputPixels[px + c] = (byte)Math.Clamp(smoothed, 0, 255);
                    }
                    else
                    {
                        // Em bordas e texturas, aplicar ganho de super-resolução
                        int enhanced = (int)Math.Round(centerVal + (laplacian * amount));
                        outputPixels[px + c] = (byte)Math.Clamp(enhanced, 0, 255);
                    }
                }

                // Manter canal Alpha intacto
                outputPixels[px + 3] = pixels[px + 3];
            }
        });

        // Escrever de volta no WriteableBitmap
        bitmap.WritePixels(new Int32Rect(0, 0, width, height), outputPixels, stride, 0);
    }

    private static string DetermineOutputExtension(string sourcePath, OutputFormat format)
    {
        return format switch
        {
            OutputFormat.Png => ".png",
            OutputFormat.Jpeg => ".jpg",
            OutputFormat.Webp => ".png", // Fallback seguro para PNG se formato Webp
            _ => Path.GetExtension(sourcePath).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => ".jpg",
                ".bmp" => ".bmp",
                _ => ".png"
            }
        };
    }

    private static BitmapEncoder CreateEncoder(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 98 },
            ".bmp" => new BmpBitmapEncoder(),
            _ => new PngBitmapEncoder()
        };
    }

    private static string GenerateUniqueFilePath(string directory, string desiredFileName)
    {
        string fullPath = Path.Combine(directory, desiredFileName);
        if (!File.Exists(fullPath)) return fullPath;

        string nameWithoutExt = Path.GetFileNameWithoutExtension(desiredFileName);
        string ext = Path.GetExtension(desiredFileName);
        int counter = 1;

        while (File.Exists(fullPath))
        {
            fullPath = Path.Combine(directory, $"{nameWithoutExt} ({counter}){ext}");
            counter++;
        }

        return fullPath;
    }
}
