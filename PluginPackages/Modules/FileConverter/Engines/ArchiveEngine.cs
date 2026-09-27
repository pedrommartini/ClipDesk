using System.IO;
using System.IO.Compression;

namespace ClipDesk.Plugin.FileConverter.Engines;

public static class ArchiveEngine
{
    public static async Task<string> ConvertArchiveAsync(
        string sourcePath,
        string targetFormat,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outDir = Path.Combine(Path.GetTempPath(), "ClipDesk", "Converted");
            Directory.CreateDirectory(outDir);

            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);
            string srcExt = Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant();
            targetFormat = targetFormat.TrimStart('.').ToLowerInvariant();

            string outputPath = Path.Combine(outDir, $"{fileNameWithoutExt}.{targetFormat}");

            if (targetFormat == "zip")
            {
                // Se o arquivo for um arquivo qualquer ou .gz, empacota em .zip
                using var zipFile = ZipFile.Open(outputPath, ZipArchiveMode.Create);
                zipFile.CreateEntryFromFile(sourcePath, Path.GetFileName(sourcePath), CompressionLevel.Optimal);
                return outputPath;
            }

            if (targetFormat == "gz")
            {
                using var inStream = File.OpenRead(sourcePath);
                using var outStream = File.Create(outputPath);
                using var gzStream = new GZipStream(outStream, CompressionLevel.Optimal);
                inStream.CopyTo(gzStream);
                return outputPath;
            }

            // Fallback para zip
            using var fallbackZip = ZipFile.Open(outputPath, ZipArchiveMode.Create);
            fallbackZip.CreateEntryFromFile(sourcePath, Path.GetFileName(sourcePath));
            return outputPath;
        }, cancellationToken);
    }
}
