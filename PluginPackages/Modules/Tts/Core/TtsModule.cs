using System.Globalization;
using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.Tts;

/// <summary>
/// Core portable module for ClipDesk Text-to-Speech (TTS).
/// Manages state versioning, normalization, and typed command execution.
/// Platform-agnostic: zero references to WPF, Win32, or OS APIs.
/// </summary>
public sealed class TtsModule : IClipDeskPluginModule
{
    public const string ModuleId = "clipdesk.tts";
    public const int SchemaStateVersion = 1;

    public string Id => ModuleId;
    public int StateVersion => SchemaStateVersion;

    public PluginState CreateDefaultState() => TtsPluginState.CreateDefault();

    public PluginState NormalizeState(PluginState state) => TtsPluginState.Normalize(state);

    public ValueTask<PluginCommandResult> ExecuteAsync(
        PluginState state,
        PluginCommand command,
        IPluginExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        state = NormalizeState(state);

        return command.Name switch
        {
            "set-text" => ValueTask.FromResult(ExecuteSetText(state, command)),
            "set-voice" => ValueTask.FromResult(ExecuteSetVoice(state, command)),
            "set-volume" => ValueTask.FromResult(ExecuteSetVolume(state, command)),
            "set-rate" => ValueTask.FromResult(ExecuteSetRate(state, command)),
            "generate-speech" => ValueTask.FromResult(ExecuteGenerateSpeech(state, command)),
            "clear" => ValueTask.FromResult(ExecuteClear(state)),
            _ => ValueTask.FromResult(PluginCommandResult.Invalid(state, $"Comando desconhecido: '{command.Name}'."))
        };
    }

    public string? GetClipboardText(PluginState state)
    {
        var normalized = NormalizeState(state);
        var lastGenerated = normalized.GetLastGeneratedText();
        if (!string.IsNullOrWhiteSpace(lastGenerated))
            return lastGenerated;

        var text = normalized.GetText();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static PluginCommandResult ExecuteSetText(PluginState state, PluginCommand command)
    {
        var value = command.Argument("text") ?? command.Argument("value") ?? "";
        if (value.Length > TtsPluginState.MaximumTextLength)
        {
            return PluginCommandResult.Invalid(
                state,
                $"O texto deve ter no máximo {TtsPluginState.MaximumTextLength:N0} caracteres.");
        }

        var next = state.WithText(value);

        // Optional auto-detection of language if requested or if language is set to 'auto'
        var explicitLang = command.Argument("language");
        if (!string.IsNullOrWhiteSpace(explicitLang))
        {
            next = next.WithLanguage(explicitLang);
        }
        else if (command.Argument("detect") == "true" || next.GetLanguage().Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                var detected = LanguageDetector.DetectLanguage(value);
                next = next.WithLanguage(detected);
            }
        }

        return new PluginCommandResult(next);
    }

    private static PluginCommandResult ExecuteSetVoice(PluginState state, PluginCommand command)
    {
        var voiceId = command.Argument("voice_id")
            ?? command.Argument("voiceId")
            ?? command.Argument("value");

        if (string.IsNullOrWhiteSpace(voiceId))
        {
            return PluginCommandResult.Invalid(state, "Identificador de voz inválido.");
        }

        var next = state.WithVoiceId(voiceId.Trim());

        var language = command.Argument("language");
        if (!string.IsNullOrWhiteSpace(language))
        {
            next = next.WithLanguage(language.Trim());
        }

        return new PluginCommandResult(next);
    }

    private static PluginCommandResult ExecuteSetVolume(PluginState state, PluginCommand command)
    {
        var raw = command.Argument("volume") ?? command.Argument("value");
        if (string.IsNullOrWhiteSpace(raw) ||
            !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var volume) ||
            volume < TtsPluginState.MinVolume ||
            volume > TtsPluginState.MaxVolume)
        {
            return PluginCommandResult.Invalid(
                state,
                $"O volume deve ser um número inteiro entre {TtsPluginState.MinVolume} e {TtsPluginState.MaxVolume}.");
        }

        return new PluginCommandResult(state.WithVolume(volume));
    }

    private static PluginCommandResult ExecuteSetRate(PluginState state, PluginCommand command)
    {
        var raw = command.Argument("rate") ?? command.Argument("value");
        if (string.IsNullOrWhiteSpace(raw) ||
            !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rate) ||
            rate < TtsPluginState.MinRate ||
            rate > TtsPluginState.MaxRate)
        {
            return PluginCommandResult.Invalid(
                state,
                $"A velocidade de fala deve ser um número inteiro entre {TtsPluginState.MinRate} e {TtsPluginState.MaxRate}.");
        }

        return new PluginCommandResult(state.WithRate(rate));
    }

    private static PluginCommandResult ExecuteGenerateSpeech(PluginState state, PluginCommand command)
    {
        var text = command.Argument("text") ?? state.GetText();
        if (string.IsNullOrWhiteSpace(text))
        {
            return PluginCommandResult.Invalid(state, "Insira um texto para sintetizar a voz.");
        }

        if (text.Length > TtsPluginState.MaximumTextLength)
        {
            return PluginCommandResult.Invalid(
                state,
                $"O texto para síntese deve ter no máximo {TtsPluginState.MaximumTextLength:N0} caracteres.");
        }

        var next = state
            .WithText(text)
            .WithLastGeneratedText(text);

        var audioFileName = command.Argument("audio_file_name") ?? command.Argument("file");
        if (!string.IsNullOrWhiteSpace(audioFileName))
        {
            next = next.WithAudioFileName(audioFileName);
        }

        return new PluginCommandResult(next);
    }

    private static PluginCommandResult ExecuteClear(PluginState state)
    {
        var next = state
            .WithText("")
            .WithAudioFileName("")
            .WithLastGeneratedText("");

        return new PluginCommandResult(next);
    }
}
