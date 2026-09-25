using System.Text;

namespace ClipDesk.Plugin.Tts;

/// <summary>
/// Ultra-fast, zero-dependency heuristic language detector for Portuguese (pt), English (en), and Spanish (es).
/// Uses diacritics, character n-grams/digraphs, and high-frequency stopword scoring.
/// </summary>
public static class LanguageDetector
{
    public const string Portuguese = "pt";
    public const string English = "en";
    public const string Spanish = "es";

    public static IReadOnlyList<string> SupportedLanguages { get; } = [Portuguese, English, Spanish];

    // Characteristic diacritics
    private static readonly HashSet<char> PortugueseDiacritics = ['ã', 'õ', 'ç', 'â', 'ê', 'ô', 'à'];
    private static readonly HashSet<char> SpanishDiacritics = ['ñ', '¿', '¡', 'ü'];

    // High-frequency distinctive stopwords
    private static readonly HashSet<string> PortugueseExclusiveWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "não", "nao", "são", "sao", "estão", "estao", "em", "um", "uma", "uns", "umas",
        "com", "do", "da", "dos", "das", "no", "na", "nos", "nas",
        "ao", "aos", "pelo", "pela", "pelos", "pelas", "num", "numa",
        "você", "voce", "vocês", "voces", "ele", "ela", "eles", "elas",
        "nós", "nos", "também", "tambem", "muito", "mais", "mas",
        "quando", "depois", "mesmo", "quem", "esse", "essa", "esses", "essas",
        "este", "esta", "estes", "estas", "isto", "isso", "aquilo",
        "meu", "minha", "meus", "minhas", "seu", "sua", "seus", "suas",
        "nosso", "nossa", "nossos", "nossas", "dele", "dela", "deles", "delas",
        "olá", "ola", "obrigado", "obrigada", "bom", "boa", "dia", "tarde", "noite",
        "fazer", "pode", "sobre", "entre", "assim", "como", "porque", "porquê",
        "para", "todos", "todo", "todas", "toda", "por", "dias", "tardes", "noites", "os"
    };

    private static readonly HashSet<string> SpanishExclusiveWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "el", "la", "los", "las", "del", "al", "en", "un", "una", "unos", "unas",
        "con", "pero", "por", "para", "su", "sus", "es", "son", "está", "esta",
        "están", "estan", "yo", "tú", "tu", "él", "el", "ella", "ellos", "ellas",
        "nosotros", "nosotras", "vosotros", "vosotras", "usted", "ustedes",
        "muy", "más", "mas", "gracias", "hola", "bueno", "buena", "buenos", "buenas",
        "días", "dias", "tardes", "noches", "hacer", "puede", "sobre", "entre",
        "porque", "porqué", "también", "tambien", "siempre", "nunca", "nada",
        "todo", "todos", "todas", "donde", "cuando", "quien", "quienes",
        "este", "esta", "estos", "estas", "esto", "ese", "esa", "esos", "esas", "aquel"
    };

    private static readonly HashSet<string> EnglishExclusiveWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "be", "to", "of", "and", "a", "in", "that", "have", "has", "had",
        "it", "for", "not", "on", "with", "he", "as", "you", "do", "does", "did",
        "at", "this", "but", "his", "by", "from", "they", "we", "say", "her",
        "she", "or", "an", "will", "would", "my", "one", "all", "there", "their",
        "what", "so", "up", "out", "if", "about", "who", "which", "when", "where",
        "can", "could", "like", "time", "no", "just", "him", "know", "take",
        "people", "into", "year", "your", "good", "some", "them", "see", "other",
        "than", "then", "now", "look", "only", "come", "its", "over", "think",
        "also", "back", "after", "use", "two", "how", "our", "work", "first",
        "well", "way", "even", "new", "want", "because", "any", "these", "give",
        "day", "most", "us", "hello", "hi", "thank", "thanks", "please", "speech",
        "text", "audio", "voice", "player", "speak", "reading", "listen"
    };

    /// <summary>
    /// Detects whether the given text is most likely Portuguese ("pt"), English ("en"), or Spanish ("es").
    /// Falls back to "pt" if input is empty, whitespace, or indeterminate.
    /// </summary>
    public static string DetectLanguage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Portuguese;

        var lower = text.ToLowerInvariant();
        int ptScore = 0;
        int esScore = 0;
        int enScore = 0;

        // 1. Diacritics and distinctive characters analysis
        foreach (var ch in lower)
        {
            if (PortugueseDiacritics.Contains(ch))
                ptScore += 5;
            else if (SpanishDiacritics.Contains(ch))
                esScore += 6;
        }

        // 2. Distinctive n-grams / digraphs
        if (lower.Contains("ção") || lower.Contains("ções") || lower.Contains("nh") || lower.Contains("lh"))
            ptScore += 4;

        if (lower.Contains("ción") || lower.Contains("ciones"))
            esScore += 3;

        if (lower.Contains("por favor"))
            esScore += 3;

        if (lower.Contains("th") || lower.Contains("sh") || lower.Contains("wh") || lower.Contains("ing") || lower.Contains("ed "))
            enScore += 2;

        // 3. Tokenize words
        var words = ExtractWords(lower);
        foreach (var word in words)
        {
            if (PortugueseExclusiveWords.Contains(word))
                ptScore += 3;

            if (SpanishExclusiveWords.Contains(word))
                esScore += 3;

            if (EnglishExclusiveWords.Contains(word))
                enScore += 3;
        }

        // 4. Decision logic
        if (enScore > ptScore && enScore > esScore)
            return English;

        if (esScore > ptScore && esScore > enScore)
            return Spanish;

        if (ptScore > esScore && ptScore > enScore)
            return Portuguese;

        // Tie-breaking logic
        if (enScore == esScore && enScore > ptScore)
        {
            if (enScore > 0)
                return English;
        }

        if (ptScore == esScore && ptScore > enScore)
        {
            if (lower.Contains('ñ') || lower.Contains('¿') || lower.Contains('¡') || lower.Contains('ü'))
                return Spanish;

            return Portuguese;
        }

        if (ptScore == enScore && ptScore > esScore)
            return Portuguese;

        return Portuguese;
    }

    private static List<string> ExtractWords(string text)
    {
        var words = new List<string>();
        var sb = new StringBuilder();

        foreach (var c in text)
        {
            if (char.IsLetter(c) || c is '\'' or '-')
            {
                sb.Append(c);
            }
            else
            {
                if (sb.Length > 0)
                {
                    words.Add(sb.ToString());
                    sb.Clear();
                }
            }
        }

        if (sb.Length > 0)
            words.Add(sb.ToString());

        return words;
    }
}
