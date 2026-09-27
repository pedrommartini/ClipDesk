using System.IO;
using System.Text;
using ClipDesk.Plugin.FileConverter.Models;
using ClipDesk.Plugin.FileConverter.Services;

namespace ClipDesk.Plugin.FileConverter.Engines;

public static class UniversalConversionCoordinator
{
    public static async Task<string> ConvertFileAsync(
        string sourcePath,
        string targetFormat,
        int imageQuality = 85,
        int icoSize = 32,
        bool prettyPrint = true,
        int audioBitrate = 192000,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Arquivo de origem não encontrado: {sourcePath}");

        string srcExt = Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant();
        targetFormat = targetFormat.TrimStart('.').ToLowerInvariant();

        var srcCat = FormatRegistry.DetectCategory(srcExt);
        var tgtDef = FormatRegistry.GetFormat(targetFormat);

        if (!FormatRegistry.IsConversionAllowed(srcExt, targetFormat, out string? error))
        {
            throw new InvalidOperationException(error ?? "Conversão não permitida pela matriz semântica de regras.");
        }

        var outDir = Path.Combine(Path.GetTempPath(), "ClipDesk", "Converted");
        Directory.CreateDirectory(outDir);
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);

        // 1. IMAGEM
        if (srcCat == FileCategory.Image)
        {
            return await ImageConversionEngine.ConvertImageAsync(
                sourcePath, targetFormat, imageQuality, icoSize, cancellationToken);
        }

        // 2. ÁUDIO
        if (srcCat == FileCategory.Audio)
        {
            return await AudioMediaConversionEngine.ConvertAudioAsync(
                sourcePath, targetFormat, audioBitrate, cancellationToken);
        }

        // 3. VÍDEO
        if (srcCat == FileCategory.Video)
        {
            // Extração de áudio
            if (targetFormat is "mp3" or "wav")
            {
                return await AudioMediaConversionEngine.ExtractAudioFromVideoAsync(
                    sourcePath, targetFormat, audioBitrate, cancellationToken);
            }

            // Transcodificação de vídeo para MP4 / WMV / AVI via WMF
            return await AudioMediaConversionEngine.ConvertAudioAsync(
                sourcePath, targetFormat, audioBitrate, cancellationToken);
        }

        // 4. DADOS (JSON, CSV, XML, YAML, TXT)
        if (srcCat == FileCategory.Data)
        {
            string content = await File.ReadAllTextAsync(sourcePath, Encoding.UTF8, cancellationToken);
            string convertedText = "";

            if (srcExt == "json")
            {
                convertedText = targetFormat switch
                {
                    "csv" => DataDocumentConverter.JsonToCsv(content),
                    "xml" => DataDocumentConverter.JsonToXml(content),
                    "yaml" or "yml" => DataDocumentConverter.JsonToYaml(content),
                    _ => content
                };
            }
            else if (srcExt == "csv")
            {
                convertedText = targetFormat switch
                {
                    "json" => DataDocumentConverter.CsvToJson(content, prettyPrint),
                    "xml" => DataDocumentConverter.JsonToXml(DataDocumentConverter.CsvToJson(content, false)),
                    "yaml" => DataDocumentConverter.JsonToYaml(DataDocumentConverter.CsvToJson(content, false)),
                    _ => content
                };
            }
            else if (srcExt == "xml")
            {
                convertedText = targetFormat switch
                {
                    "json" => DataDocumentConverter.XmlToJson(content, prettyPrint),
                    "csv" => DataDocumentConverter.JsonToCsv(DataDocumentConverter.XmlToJson(content, false)),
                    "yaml" => DataDocumentConverter.JsonToYaml(DataDocumentConverter.XmlToJson(content, false)),
                    _ => content
                };
            }
            else if (srcExt is "yaml" or "yml")
            {
                // Se for YAML, salva como TXT/JSON
                convertedText = content;
            }

            string outPath = Path.Combine(outDir, $"{fileNameWithoutExt}.{targetFormat}");
            await File.WriteAllTextAsync(outPath, convertedText, Encoding.UTF8, cancellationToken);
            return outPath;
        }

        // 5. DOCUMENTO & TEXTO (MD, HTML, TXT, PDF)
        if (srcCat == FileCategory.Document)
        {
            if (targetFormat == "pdf")
            {
                string text = await File.ReadAllTextAsync(sourcePath, Encoding.UTF8, cancellationToken);
                var pdfBytes = LightweightPdfBuilder.CreateTextPdf(text, fileNameWithoutExt);
                string outPath = Path.Combine(outDir, $"{fileNameWithoutExt}.pdf");
                await File.WriteAllBytesAsync(outPath, pdfBytes, cancellationToken);
                return outPath;
            }

            if (srcExt is "md" or "markdown" && targetFormat == "html")
            {
                string md = await File.ReadAllTextAsync(sourcePath, Encoding.UTF8, cancellationToken);
                string html = DataDocumentConverter.MarkdownToHtml(md, fileNameWithoutExt);
                string outPath = Path.Combine(outDir, $"{fileNameWithoutExt}.html");
                await File.WriteAllTextAsync(outPath, html, Encoding.UTF8, cancellationToken);
                return outPath;
            }

            if (srcExt is "html" or "htm" && targetFormat == "txt")
            {
                string html = await File.ReadAllTextAsync(sourcePath, Encoding.UTF8, cancellationToken);
                string txt = DataDocumentConverter.HtmlToText(html);
                string outPath = Path.Combine(outDir, $"{fileNameWithoutExt}.txt");
                await File.WriteAllTextAsync(outPath, txt, Encoding.UTF8, cancellationToken);
                return outPath;
            }

            // Fallback para cópia / renomeação de texto
            string raw = await File.ReadAllTextAsync(sourcePath, Encoding.UTF8, cancellationToken);
            string fallbackPath = Path.Combine(outDir, $"{fileNameWithoutExt}.{targetFormat}");
            await File.WriteAllTextAsync(fallbackPath, raw, Encoding.UTF8, cancellationToken);
            return fallbackPath;
        }

        // 6. COMPACTAÇÃO (ZIP, GZ)
        if (srcCat == FileCategory.Archive)
        {
            return await ArchiveEngine.ConvertArchiveAsync(sourcePath, targetFormat, cancellationToken);
        }

        throw new NotSupportedException($"Categoria '{srcCat}' não suportada para conversão.");
    }
}
