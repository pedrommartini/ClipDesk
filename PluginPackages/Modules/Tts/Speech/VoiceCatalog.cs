namespace ClipDesk.Plugin.Tts.Speech;

/// <summary>
/// Represents an available voice option (either online neural or local Windows SAPI).
/// </summary>
public sealed record VoiceOption(
    string Id,
    string DisplayName,
    string Language,
    string Gender,
    bool IsLocal)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// Curated voice catalog containing high-quality neural voices for Portuguese, English,
/// and Spanish, along with preview phrases and helper query methods.
/// </summary>
public static class VoiceCatalog
{
    private static readonly IReadOnlyList<VoiceOption> CuratedNeuralVoices =
    [
        // Portuguese (Brazil & Portugal)
        new("pt-BR-FranciscaNeural", "Francisca (Brasil - Natural)", "pt", "Female", false),
        new("pt-BR-AntonioNeural", "Antônio (Brasil - Natural)", "pt", "Male", false),
        new("pt-BR-ThalitaNeural", "Thalita (Brasil)", "pt", "Female", false),
        new("pt-PT-RaquelNeural", "Raquel (Portugal)", "pt", "Female", false),
        new("pt-PT-DuarteNeural", "Duarte (Portugal)", "pt", "Male", false),

        // English (US & UK)
        new("en-US-JennyNeural", "Jenny (US - Expressiva)", "en", "Female", false),
        new("en-US-GuyNeural", "Guy (US - Natural)", "en", "Male", false),
        new("en-US-AriaNeural", "Aria (US)", "en", "Female", false),
        new("en-US-ChristopherNeural", "Christopher (US)", "en", "Male", false),
        new("en-GB-SoniaNeural", "Sonia (Reino Unido)", "en", "Female", false),
        new("en-GB-RyanNeural", "Ryan (Reino Unido)", "en", "Male", false),

        // Spanish (Spain & Mexico)
        new("es-ES-ElviraNeural", "Elvira (Espanha)", "es", "Female", false),
        new("es-ES-AlvaroNeural", "Álvaro (Espanha)", "es", "Male", false),
        new("es-MX-DaliaNeural", "Dalia (México)", "es", "Female", false),
        new("es-MX-JorgeNeural", "Jorge (México)", "es", "Male", false)
    ];

    /// <summary>
    /// Gets all curated neural voices, optionally filtered by two-letter language code (pt, en, es).
    /// </summary>
    public static IReadOnlyList<VoiceOption> GetCuratedVoices(string? language = null)
    {
        if (string.IsNullOrWhiteSpace(language) || language.Equals("all", StringComparison.OrdinalIgnoreCase))
            return CuratedNeuralVoices;

        var langPrefix = language.Trim().ToLowerInvariant();
        if (langPrefix.Contains('-'))
            langPrefix = langPrefix.Split('-')[0];

        return CuratedNeuralVoices
            .Where(v => v.Language.Equals(langPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Returns default voice ID for the given language.
    /// </summary>
    public static string GetDefaultVoiceId(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return "pt-BR-FranciscaNeural";

        var langPrefix = language.Trim().ToLowerInvariant();
        if (langPrefix.Contains('-'))
            langPrefix = langPrefix.Split('-')[0];

        return langPrefix switch
        {
            "en" => "en-US-JennyNeural",
            "es" => "es-ES-ElviraNeural",
            _ => "pt-BR-FranciscaNeural"
        };
    }

    /// <summary>
    /// Gets a representative preview sample phrase for the specified language.
    /// </summary>
    public static string GetPreviewPhrase(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return "Olá! Esta é uma prévia da minha voz.";

        var langPrefix = language.Trim().ToLowerInvariant();
        if (langPrefix.Contains('-'))
            langPrefix = langPrefix.Split('-')[0];

        return langPrefix switch
        {
            "en" => "Hello! This is a preview of my voice.",
            "es" => "¡Hola! Esta es una vista previa de mi voz.",
            _ => "Olá! Esta é uma prévia da minha voz."
        };
    }

    /// <summary>
    /// Finds a voice by ID in curated voices or the supplied local voices collection.
    /// </summary>
    public static VoiceOption? FindVoice(string voiceId, IEnumerable<VoiceOption>? localVoices = null)
    {
        if (string.IsNullOrWhiteSpace(voiceId))
            return null;

        var neural = CuratedNeuralVoices.FirstOrDefault(v => v.Id.Equals(voiceId, StringComparison.OrdinalIgnoreCase));
        if (neural is not null)
            return neural;

        if (localVoices is not null)
        {
            return localVoices.FirstOrDefault(v => v.Id.Equals(voiceId, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }
}
