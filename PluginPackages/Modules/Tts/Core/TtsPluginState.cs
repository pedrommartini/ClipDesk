using System.Globalization;
using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.Tts;

/// <summary>
/// State model, key constants, and immutable helper extensions for the ClipDesk TTS plugin.
/// </summary>
public static class TtsPluginState
{
    public const string KeySchema = PluginStateKeys.SchemaVersion;
    public const string KeyText = "text";
    public const string KeyLanguage = "language";
    public const string KeyVoiceId = "voice_id";
    public const string KeyLegacyVoiceId = "voiceId";
    public const string KeyVolume = "volume";
    public const string KeyRate = "rate";
    public const string KeyAudioFileName = "audio_file_name";
    public const string KeyLastGeneratedText = "last_generated_text";

    public const string CurrentSchemaVersion = "1";
    public const string DefaultLanguage = "pt";
    public const string DefaultVoiceId = "pt-BR-FranciscaNeural";
    public const int DefaultVolume = 100;
    public const int DefaultRate = 0;
    public const int MinVolume = 0;
    public const int MaxVolume = 100;
    public const int MinRate = -10;
    public const int MaxRate = 10;
    public const int MaximumTextLength = 5_000;

    /// <summary>
    /// Creates the default initial state for the TTS plugin.
    /// </summary>
    public static PluginState CreateDefault() => new(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [KeySchema] = CurrentSchemaVersion,
        [KeyText] = "",
        [KeyLanguage] = DefaultLanguage,
        [KeyVoiceId] = DefaultVoiceId,
        [KeyVolume] = DefaultVolume.ToString(CultureInfo.InvariantCulture),
        [KeyRate] = DefaultRate.ToString(CultureInfo.InvariantCulture),
        [KeyAudioFileName] = "",
        [KeyLastGeneratedText] = ""
    });

    /// <summary>
    /// Normalizes any input state, guaranteeing idempotency, safe fallbacks,
    /// clamping of numeric values, preservation of unknown keys, and handling of legacy keys.
    /// </summary>
    public static PluginState Normalize(PluginState? state)
    {
        if (state is null)
            return CreateDefault();

        var values = state.ToDictionary();

        // 1. Ensure schema version is set
        values[KeySchema] = CurrentSchemaVersion;

        // 2. Text
        values[KeyText] = state.GetString(KeyText) ?? "";

        // 3. Language
        var language = state.GetString(KeyLanguage)?.Trim();
        values[KeyLanguage] = string.IsNullOrWhiteSpace(language) ? DefaultLanguage : language;

        // 4. Voice ID (handle legacy "voiceId" key if "voice_id" is absent)
        var voiceId = state.GetString(KeyVoiceId)?.Trim();
        if (string.IsNullOrWhiteSpace(voiceId))
        {
            var legacyVoiceId = state.GetString(KeyLegacyVoiceId)?.Trim();
            voiceId = !string.IsNullOrWhiteSpace(legacyVoiceId) ? legacyVoiceId : DefaultVoiceId;
        }
        values[KeyVoiceId] = voiceId;

        // 5. Volume (clamped between 0 and 100)
        var volumeRaw = state.GetString(KeyVolume);
        int volume = int.TryParse(volumeRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedVol)
            ? Math.Clamp(parsedVol, MinVolume, MaxVolume)
            : DefaultVolume;
        values[KeyVolume] = volume.ToString(CultureInfo.InvariantCulture);

        // 6. Rate (clamped between -10 and 10)
        var rateRaw = state.GetString(KeyRate);
        int rate = int.TryParse(rateRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRate)
            ? Math.Clamp(parsedRate, MinRate, MaxRate)
            : DefaultRate;
        values[KeyRate] = rate.ToString(CultureInfo.InvariantCulture);

        // 7. Audio file name
        values[KeyAudioFileName] = state.GetString(KeyAudioFileName)?.Trim() ?? "";

        // 8. Last generated text
        values[KeyLastGeneratedText] = state.GetString(KeyLastGeneratedText)?.Trim() ?? "";

        return new PluginState(values);
    }

    // Strongly-typed accessors
    public static string GetText(this PluginState state) => state.GetString(KeyText) ?? "";
    public static string GetLanguage(this PluginState state) => state.GetString(KeyLanguage) ?? DefaultLanguage;
    public static string GetVoiceId(this PluginState state) => state.GetString(KeyVoiceId) ?? DefaultVoiceId;
    public static int GetVolume(this PluginState state) => Math.Clamp(state.GetInt32(KeyVolume, DefaultVolume), MinVolume, MaxVolume);
    public static int GetRate(this PluginState state) => Math.Clamp(state.GetInt32(KeyRate, DefaultRate), MinRate, MaxRate);
    public static string GetAudioFileName(this PluginState state) => state.GetString(KeyAudioFileName) ?? "";
    public static string GetLastGeneratedText(this PluginState state) => state.GetString(KeyLastGeneratedText) ?? "";

    // Strongly-typed mutators returning new immutable instances
    public static PluginState WithText(this PluginState state, string? text) =>
        state.With(KeyText, text ?? "");

    public static PluginState WithLanguage(this PluginState state, string? language) =>
        state.With(KeyLanguage, string.IsNullOrWhiteSpace(language) ? DefaultLanguage : language.Trim());

    public static PluginState WithVoiceId(this PluginState state, string? voiceId) =>
        state.With(KeyVoiceId, string.IsNullOrWhiteSpace(voiceId) ? DefaultVoiceId : voiceId.Trim());

    public static PluginState WithVolume(this PluginState state, int volume) =>
        state.With(KeyVolume, Math.Clamp(volume, MinVolume, MaxVolume).ToString(CultureInfo.InvariantCulture));

    public static PluginState WithRate(this PluginState state, int rate) =>
        state.With(KeyRate, Math.Clamp(rate, MinRate, MaxRate).ToString(CultureInfo.InvariantCulture));

    public static PluginState WithAudioFileName(this PluginState state, string? audioFileName) =>
        state.With(KeyAudioFileName, audioFileName?.Trim() ?? "");

    public static PluginState WithLastGeneratedText(this PluginState state, string? lastGeneratedText) =>
        state.With(KeyLastGeneratedText, lastGeneratedText?.Trim() ?? "");
}
