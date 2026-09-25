using System.Globalization;
using System.IO;
using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.Tts.Speech;

/// <summary>
/// Result of a speech synthesis operation containing audio payload and storage metadata.
/// </summary>
public sealed record TtsAudioResult(
    byte[] AudioBytes,
    string FileName,
    string FilePath,
    string ContentType,
    bool IsFallback);

/// <summary>
/// Hybrid speech synthesis engine that combines Edge online neural TTS with Windows SAPI offline fallback.
/// Handles audio serialization, persistent storage, and board file creation.
/// </summary>
public sealed class HybridTtsEngine : IDisposable
{
    private readonly EdgeTtsClient _edgeClient;
    private readonly WindowsSpeechEngine _localEngine;
    private bool _disposed;

    public HybridTtsEngine()
    {
        _edgeClient = new EdgeTtsClient();
        _localEngine = new WindowsSpeechEngine();
    }

    public WindowsSpeechEngine LocalEngine => _localEngine;

    /// <summary>
    /// Returns the active voice list (curated neural + local SAPI voices).
    /// </summary>
    public IReadOnlyList<VoiceOption> GetAllVoices(string? languageFilter = null)
    {
        var result = new List<VoiceOption>();

        // 1. Curated neural voices
        result.AddRange(VoiceCatalog.GetCuratedVoices(languageFilter));

        // 2. Installed local Windows voices
        var localVoices = _localEngine.GetInstalledVoices();
        if (string.IsNullOrWhiteSpace(languageFilter) || languageFilter.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            result.AddRange(localVoices);
        }
        else
        {
            var prefix = languageFilter.Trim().ToLowerInvariant();
            if (prefix.Contains('-'))
                prefix = prefix.Split('-')[0];

            result.AddRange(localVoices.Where(v => v.Language.Equals(prefix, StringComparison.OrdinalIgnoreCase)));
        }

        return result;
    }

    /// <summary>
    /// Synthesizes text with automatic online-to-offline fallback and saves output to plugin storage.
    /// </summary>
    public async Task<TtsAudioResult> SynthesizeAsync(
        string text,
        string voiceId,
        string language,
        int rate,
        int volume,
        bool hasNetworkPermission = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("O texto para síntese de voz não pode estar vazio.", nameof(text));
        }

        byte[]? audioBytes = null;
        string extension;
        string contentType;
        bool isFallback = false;

        bool isLocalExplicit = voiceId.StartsWith("local:", StringComparison.OrdinalIgnoreCase);

        // Try online neural synthesis if not explicitly local and network is permitted
        if (!isLocalExplicit && hasNetworkPermission)
        {
            try
            {
                audioBytes = await _edgeClient.SynthesizeToMp3Async(text, voiceId, language, rate, volume, cancellationToken);
                extension = "mp3";
                contentType = "audio/mpeg";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Fall back silently to Windows local speech synthesizer
                audioBytes = null;
                isFallback = true;
                extension = "wav";
                contentType = "audio/wav";
            }
        }
        else
        {
            extension = "wav";
            contentType = "audio/wav";
            isFallback = !isLocalExplicit;
        }

        // If online failed or offline requested, execute local synthesis
        if (audioBytes is null)
        {
            audioBytes = await _localEngine.SynthesizeToWavAsync(text, voiceId, rate, volume, cancellationToken);
        }

        // Save generated audio to official plugin directory
        var storageDirectory = PluginStoragePaths.GetDefaultDirectory("clipdesk.tts");
        Directory.CreateDirectory(storageDirectory);

        var timeStamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..4];
        var fileName = $"fala_{timeStamp}_{uniqueSuffix}.{extension}";
        var filePath = Path.Combine(storageDirectory, fileName);

        await File.WriteAllBytesAsync(filePath, audioBytes, cancellationToken);

        return new TtsAudioResult(audioBytes, fileName, filePath, contentType, isFallback);
    }

    /// <summary>
    /// Synthesizes a short sample preview phrase for the specified voice.
    /// </summary>
    public async Task<TtsAudioResult> SynthesizePreviewAsync(
        string voiceId,
        string language,
        int rate,
        int volume,
        bool hasNetworkPermission = true,
        CancellationToken cancellationToken = default)
    {
        var previewPhrase = VoiceCatalog.GetPreviewPhrase(language);
        return await SynthesizeAsync(previewPhrase, voiceId, language, rate, volume, hasNetworkPermission, cancellationToken);
    }

    /// <summary>
    /// Creates a PluginBoardFile ready for host action addition.
    /// </summary>
    public static PluginBoardFile CreateBoardFile(TtsAudioResult result)
    {
        return PluginBoardFile.FromPath(result.FilePath, result.FileName);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _localEngine.Dispose();
            _disposed = true;
        }
    }
}
