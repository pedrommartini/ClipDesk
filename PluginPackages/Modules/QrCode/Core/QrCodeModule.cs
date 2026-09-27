using System.Globalization;
using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.QrCode;

public sealed class QrCodeModule : IClipDeskPluginModule
{
    public const int MaximumTextLength = 2_000;
    public const string DefaultInitialText = "https://github.com/pedrommartini/ClipDesk";
    public const string DefaultEcc = "M";

    public string Id => "clipdesk.qrcode";
    public int StateVersion => 1;

    public PluginState CreateDefaultState() => new(new Dictionary<string, string>
    {
        [PluginStateKeys.SchemaVersion] = "1",
        ["text"] = DefaultInitialText,
        ["ecc"] = DefaultEcc
    });

    public PluginState NormalizeState(PluginState state)
    {
        var values = state.ToDictionary();
        values[PluginStateKeys.SchemaVersion] = "1";

        string rawText = state.GetString("text") ?? string.Empty;
        if (rawText.Length > MaximumTextLength)
            rawText = rawText[..MaximumTextLength];
        values["text"] = rawText;

        string ecc = (state.GetString("ecc") ?? string.Empty).Trim().ToUpperInvariant();
        if (ecc is not ("L" or "M" or "Q" or "H"))
            ecc = DefaultEcc;
        values["ecc"] = ecc;

        return new PluginState(values);
    }

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
            "set-text" => ValueTask.FromResult(SetText(state, command.Argument("value"))),
            "set-ecc" => ValueTask.FromResult(SetEcc(state, command.Argument("value"))),
            "clear" => ValueTask.FromResult(new PluginCommandResult(state.With("text", string.Empty))),
            _ => ValueTask.FromResult(PluginCommandResult.Invalid(state, "Comando desconhecido."))
        };
    }

    public string? GetClipboardText(PluginState state) => NormalizeState(state).GetString("text");

    public static QrEccLevel ParseEccLevel(string? eccString) => (eccString?.Trim().ToUpperInvariant()) switch
    {
        "L" => QrEccLevel.Low,
        "Q" => QrEccLevel.Quartile,
        "H" => QrEccLevel.High,
        _ => QrEccLevel.Medium
    };

    private static PluginCommandResult SetText(PluginState state, string? value)
    {
        value ??= string.Empty;
        if (value.Length > MaximumTextLength)
            return PluginCommandResult.Invalid(state, string.Format(CultureInfo.InvariantCulture, "O texto pode ter no máximo {0} caracteres.", MaximumTextLength));

        // Testar se gera sem erro de capacidade
        try
        {
            string ecc = state.GetString("ecc") ?? DefaultEcc;
            _ = QrCodeEncoder.EncodeText(value, ParseEccLevel(ecc));
        }
        catch (Exception ex)
        {
            return PluginCommandResult.Invalid(state, ex.Message);
        }

        return new PluginCommandResult(state.With("text", value));
    }

    private static PluginCommandResult SetEcc(PluginState state, string? ecc)
    {
        ecc = ecc?.Trim().ToUpperInvariant() ?? DefaultEcc;
        if (ecc is not ("L" or "M" or "Q" or "H"))
            return PluginCommandResult.Invalid(state, "Nível de correção inválido. Escolha L, M, Q ou H.");

        string text = state.GetString("text") ?? string.Empty;
        try
        {
            _ = QrCodeEncoder.EncodeText(text, ParseEccLevel(ecc));
        }
        catch (Exception ex)
        {
            return PluginCommandResult.Invalid(state, ex.Message);
        }

        return new PluginCommandResult(state.With("ecc", ecc));
    }
}
