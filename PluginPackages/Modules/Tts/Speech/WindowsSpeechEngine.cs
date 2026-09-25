using System.Globalization;
using System.IO;
using System.Speech.Synthesis;

namespace ClipDesk.Plugin.Tts.Speech;

/// <summary>
/// Local speech synthesis engine utilizing Windows SAPI via System.Speech.
/// Acts as resilient offline fallback and provider of installed system voices.
/// </summary>
public sealed class WindowsSpeechEngine : IDisposable
{
    private readonly SpeechSynthesizer _synthesizer;
    private bool _disposed;

    public WindowsSpeechEngine()
    {
        _synthesizer = new SpeechSynthesizer();
    }

    /// <summary>
    /// Enumerates enabled voices installed in the Windows operating system.
    /// </summary>
    public IReadOnlyList<VoiceOption> GetInstalledVoices()
    {
        try
        {
            var result = new List<VoiceOption>();
            foreach (var voice in _synthesizer.GetInstalledVoices())
            {
                if (!voice.Enabled)
                    continue;

                var info = voice.VoiceInfo;
                var lang = info.Culture.TwoLetterISOLanguageName.ToLowerInvariant();
                var gender = info.Gender.ToString();
                var id = "local:" + info.Name;
                var displayName = $"[Local] {info.Name} ({info.Culture.DisplayName})";

                result.Add(new VoiceOption(id, displayName, lang, gender, IsLocal: true));
            }

            return result;
        }
        catch
        {
            return Array.Empty<VoiceOption>();
        }
    }

    /// <summary>
    /// Synthesizes text directly into standard uncompressed RIFF WAV PCM format.
    /// </summary>
    public async Task<byte[]> SynthesizeToWavAsync(
        string text,
        string? voiceName,
        int rate,
        int volume,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var synth = new SpeechSynthesizer();

            // SAPI rate: -10 to 10
            synth.Rate = Math.Clamp(rate, -10, 10);
            // SAPI volume: 0 to 100
            synth.Volume = Math.Clamp(volume, 0, 100);

            // Select voice if available
            if (!string.IsNullOrWhiteSpace(voiceName))
            {
                var cleanName = voiceName.StartsWith("local:", StringComparison.OrdinalIgnoreCase)
                    ? voiceName["local:".Length..]
                    : voiceName;

                try
                {
                    synth.SelectVoice(cleanName);
                }
                catch
                {
                    // Fall back to default system voice if requested voice is not found
                }
            }

            using var memStream = new MemoryStream();
            synth.SetOutputToWaveStream(memStream);

            using (cancellationToken.Register(() =>
            {
                try
                {
                    synth.SpeakAsyncCancelAll();
                }
                catch
                {
                    // Ignored on cancellation
                }
            }))
            {
                synth.Speak(text);
            }

            cancellationToken.ThrowIfCancellationRequested();

            synth.SetOutputToNull();
            return memStream.ToArray();
        }, cancellationToken);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _synthesizer.Dispose();
            _disposed = true;
        }
    }
}
