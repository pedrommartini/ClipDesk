using System.IO;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace ClipDesk.Plugin.FileConverter.Engines;

public static class AudioMediaConversionEngine
{
    private static bool _mfInitialized;

    private static void EnsureMediaFoundation()
    {
        if (!_mfInitialized)
        {
            MediaFoundationApi.Startup();
            _mfInitialized = true;
        }
    }

    public static async Task<string> ConvertAudioAsync(
        string sourcePath,
        string targetFormat,
        int bitRate = 192000,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureMediaFoundation();

            var outDir = Path.Combine(Path.GetTempPath(), "ClipDesk", "Converted");
            Directory.CreateDirectory(outDir);

            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);
            targetFormat = targetFormat.TrimStart('.').ToLowerInvariant();
            string outputPath = Path.Combine(outDir, $"{fileNameWithoutExt}.{targetFormat}");

            using var reader = new MediaFoundationReader(sourcePath);

            switch (targetFormat)
            {
                case "wav":
                    WaveFileWriter.CreateWaveFile(outputPath, reader);
                    break;

                case "mp3":
                    MediaFoundationEncoder.EncodeToMp3(reader, outputPath, bitRate);
                    break;

                case "aac" or "m4a":
                    MediaFoundationEncoder.EncodeToAac(reader, outputPath, bitRate);
                    break;

                case "wma":
                    MediaFoundationEncoder.EncodeToWma(reader, outputPath, bitRate);
                    break;

                default:
                    // Fallback para MP3 ou WAV
                    WaveFileWriter.CreateWaveFile(outputPath, reader);
                    break;
            }

            return outputPath;
        }, cancellationToken);
    }

    /// <summary>
    /// Extrai áudio de arquivos de vídeo (MP4, MKV, AVI, WMV) usando Windows Media Foundation.
    /// </summary>
    public static async Task<string> ExtractAudioFromVideoAsync(
        string sourcePath,
        string targetFormat,
        int bitRate = 192000,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureMediaFoundation();

            var outDir = Path.Combine(Path.GetTempPath(), "ClipDesk", "Converted");
            Directory.CreateDirectory(outDir);

            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);
            targetFormat = targetFormat.TrimStart('.').ToLowerInvariant();
            string outputPath = Path.Combine(outDir, $"{fileNameWithoutExt}.{targetFormat}");

            using var reader = new MediaFoundationReader(sourcePath);

            if (targetFormat == "wav")
            {
                WaveFileWriter.CreateWaveFile(outputPath, reader);
            }
            else
            {
                MediaFoundationEncoder.EncodeToMp3(reader, outputPath, bitRate);
            }

            return outputPath;
        }, cancellationToken);
    }
}
