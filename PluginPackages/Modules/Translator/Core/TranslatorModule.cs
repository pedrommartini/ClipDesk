using System.Net;
using System.Text;
using System.Text.Json;
using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.Translator;

public sealed class TranslatorModule : IClipDeskPluginModule
{
    public static readonly IReadOnlyList<PluginOption> Languages =
    [
        new("auto", "Detectar idioma"), new("en", "Inglês"), new("pt-BR", "Português (Brasil)"),
        new("pt-PT", "Português (Portugal)"), new("es", "Espanhol"), new("fr", "Francês"),
        new("de", "Alemão"), new("it", "Italiano"), new("nl", "Holandês"), new("pl", "Polonês"),
        new("ru", "Russo"), new("uk", "Ucraniano"), new("tr", "Turco"), new("ar", "Árabe"),
        new("he", "Hebraico"), new("hi", "Hindi"), new("bn", "Bengali"), new("zh-CN", "Chinês simplificado"),
        new("zh-TW", "Chinês tradicional"), new("ja", "Japonês"), new("ko", "Coreano"),
        new("id", "Indonésio"), new("ms", "Malaio"), new("th", "Tailandês"), new("vi", "Vietnamita"),
        new("sv", "Sueco"), new("no", "Norueguês"), new("da", "Dinamarquês"), new("fi", "Finlandês"),
        new("cs", "Tcheco"), new("sk", "Eslovaco"), new("ro", "Romeno"), new("hu", "Húngaro"),
        new("el", "Grego"), new("bg", "Búlgaro"), new("hr", "Croata"), new("sr", "Sérvio"),
        new("ca", "Catalão"), new("eu", "Basco"), new("gl", "Galego")
    ];
    public string Id => BuiltInPluginIds.Translator;
    public int StateVersion => 1;
    public PluginState CreateDefaultState() => new(new Dictionary<string, string>
    { [PluginStateKeys.SchemaVersion] = "1", ["sourceLanguage"] = "auto", ["targetLanguage"] = "en", ["input"] = "", ["output"] = "A tradução aparece aqui" });
    public PluginState NormalizeState(PluginState state)
    {
        var values = state.ToDictionary(); values[PluginStateKeys.SchemaVersion] = "1";
        values.TryAdd("sourceLanguage", "auto"); values.TryAdd("targetLanguage", "en"); values.TryAdd("input", ""); values.TryAdd("output", "A tradução aparece aqui");
        if (!Languages.Any(x => x.Value.Equals(values["sourceLanguage"], StringComparison.OrdinalIgnoreCase))) values["sourceLanguage"] = "auto";
        if (!Languages.Any(x => x.Value != "auto" && x.Value.Equals(values["targetLanguage"], StringComparison.OrdinalIgnoreCase))) values["targetLanguage"] = "en";
        return new PluginState(values);
    }
    public async ValueTask<PluginCommandResult> ExecuteAsync(PluginState state, PluginCommand command,
        IPluginExecutionContext context, CancellationToken cancellationToken = default)
    {
        state = NormalizeState(state);
        if (command.Name == "set")
        {
            var key = command.Argument("key"); var value = command.Argument("value") ?? "";
            if (key is not ("input" or "sourceLanguage" or "targetLanguage")) return PluginCommandResult.Invalid(state, "Campo inválido.");
            return new PluginCommandResult(NormalizeState(state.With(key, value)));
        }
        if (command.Name == "swap")
        {
            var source = state.GetString("sourceLanguage")!; var target = state.GetString("targetLanguage")!;
            var input = state.GetString("input")!; var output = state.GetString("output")!;
            state = state.With("sourceLanguage", target).With("targetLanguage", source == "auto" ? "en" : source);
            if (!string.IsNullOrWhiteSpace(output) && output != "A tradução aparece aqui") state = state.With("input", output).With("output", input);
            return new PluginCommandResult(state);
        }
        if (command.Name != "translate") return PluginCommandResult.Invalid(state, "Comando desconhecido.");
        var text = state.GetString("input")!.Trim();
        if (text.Length == 0) return new PluginCommandResult(state.With("output", "A tradução aparece aqui"));
        if (!context.HasPermission(PluginPermissions.Network)) return new PluginCommandResult(state, PluginCommandStatus.PermissionDenied, "Acesso à rede não autorizado.");
        var sourceCode = state.GetString("sourceLanguage") == "auto" ? "autodetect" : state.GetString("sourceLanguage");
        var clipped = ClipUtf8(text, 500);
        var uri = new Uri($"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(clipped)}&langpair={Uri.EscapeDataString(sourceCode!)}%7C{Uri.EscapeDataString(state.GetString("targetLanguage")!)}");
        try
        {
            var payload = await context.Network.GetStringAsync(uri, cancellationToken);
            using var json = JsonDocument.Parse(payload);
            var translated = json.RootElement.GetProperty("responseData").GetProperty("translatedText").GetString();
            if (string.IsNullOrWhiteSpace(translated)) throw new InvalidOperationException("Resposta vazia.");
            return new PluginCommandResult(state.With("output", WebUtility.HtmlDecode(translated).Trim()));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new PluginCommandResult(state, PluginCommandStatus.Unavailable, "Não foi possível traduzir agora.", new Dictionary<string, string> { ["detail"] = ex.Message }); }
    }
    public string? GetClipboardText(PluginState state) => NormalizeState(state).GetString("output");
    public static string Label(string code) => Languages.FirstOrDefault(item => item.Value.Equals(code, StringComparison.OrdinalIgnoreCase))?.Label ?? code;
    private static string ClipUtf8(string text, int maximumBytes)
    {
        var builder = new StringBuilder(); var bytes = 0;
        foreach (var rune in text.EnumerateRunes()) { var count = rune.Utf8SequenceLength; if (bytes + count > maximumBytes) break; builder.Append(rune); bytes += count; }
        return builder.ToString();
    }
}
