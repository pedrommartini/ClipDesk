using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using ClipDesk.Plugin.ImageUpscaler.Models;

namespace ClipDesk.Plugin.ImageUpscaler.Engines;

public static class CloudAiUpscaleEngine
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(60) };

    public static async Task<UpscaleResult> UpscaleAsync(
        string sourcePath,
        ImageUpscaleOptions options,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Se nenhum endpoint de nuvem estiver configurado, executa localmente com aviso transparente
        if (string.IsNullOrWhiteSpace(options.CloudEndpoint))
        {
            var localFallback = await WicUpscaleEngine.UpscaleAsync(sourcePath, options, progress, cancellationToken);
            return localFallback with
            {
                EngineUsed = "Nuvem (Fallback Local: nenhum endpoint de IA configurado)"
            };
        }

        var sw = Stopwatch.StartNew();
        progress?.Report(0.10);

        try
        {
            var originalBytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken);
            progress?.Report(0.30);

            using var content = new MultipartFormDataContent();
            var imageContent = new ByteArrayContent(originalBytes);
            imageContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
            content.Add(imageContent, "image", Path.GetFileName(sourcePath));
            content.Add(new StringContent(options.Factor.ToWireString()), "scale");
            content.Add(new StringContent(options.Model.ToWireString()), "model");

            progress?.Report(0.50);

            var response = await HttpClient.PostAsync(options.CloudEndpoint, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // Se a API externa falhar, fallback seguro para o motor local
                var fallback = await WicUpscaleEngine.UpscaleAsync(sourcePath, options, progress, cancellationToken);
                return fallback with
                {
                    EngineUsed = $"Nuvem falhou ({response.StatusCode}) -> Fallback Local WIC"
                };
            }

            var resultBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            progress?.Report(0.85);

            string outputDir = options.OutputDirectory;
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

            string sourceNameNoExt = Path.GetFileNameWithoutExtension(sourcePath);
            string outputPath = Path.Combine(outputDir, $"{sourceNameNoExt}_{options.Factor.ToWireString()}_ai_upscaled.png");

            await File.WriteAllBytesAsync(outputPath, resultBytes, cancellationToken);
            progress?.Report(1.0);
            sw.Stop();

            return new UpscaleResult(
                sourcePath,
                outputPath,
                0, 0, 0, 0,
                originalBytes.Length,
                resultBytes.Length,
                sw.Elapsed,
                options.Factor,
                options.Model,
                "Cloud AI (Real-ESRGAN/Clarity)",
                true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fallback resiliente para WIC Local
            var fallback = await WicUpscaleEngine.UpscaleAsync(sourcePath, options, progress, cancellationToken);
            return fallback with
            {
                EngineUsed = $"Erro na Nuvem ({ex.Message}) -> Fallback Local WIC"
            };
        }
    }
}
