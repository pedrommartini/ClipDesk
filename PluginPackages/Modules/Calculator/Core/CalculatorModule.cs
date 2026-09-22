using System.Globalization;
using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.Calculator;

public sealed class CalculatorModule : IClipDeskPluginModule
{
    public string Id => BuiltInPluginIds.Calculator;
    public int StateVersion => 1;
    public PluginState CreateDefaultState() => new(new Dictionary<string, string>
    {
        [PluginStateKeys.SchemaVersion] = "1", ["expression"] = "", ["display"] = "0"
    });
    public PluginState NormalizeState(PluginState state)
    {
        var values = state.ToDictionary();
        values[PluginStateKeys.SchemaVersion] = "1";
        values.TryAdd("expression", ""); values.TryAdd("display", "0");
        return new PluginState(values);
    }
    public ValueTask<PluginCommandResult> ExecuteAsync(PluginState state, PluginCommand command,
        IPluginExecutionContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); state = NormalizeState(state);
        if (command.Name != "press") return ValueTask.FromResult(PluginCommandResult.Invalid(state, "Comando desconhecido."));
        var key = command.Argument("key");
        if (string.IsNullOrWhiteSpace(key)) return ValueTask.FromResult(PluginCommandResult.Invalid(state, "A tecla é obrigatória."));
        var (expression, display) = CalculatorMath.Press(state.GetString("expression"), key);
        return ValueTask.FromResult(new PluginCommandResult(state.With("expression", expression).With("display", display)));
    }
    public string? GetClipboardText(PluginState state) => NormalizeState(state).GetString("display");
}

public static class CalculatorMath
{
    public static (string Expression, string Display) Press(string? expression, string key)
    {
        expression = Normalize(expression);
        if (key == "C") return ("", "0");
        if (key == "back") { expression = expression.Length == 0 ? "" : expression[..^1]; return (expression, expression.Length == 0 ? "0" : Display(expression)); }
        if (key is "=" or "%" or "±")
        {
            if (!TryEvaluate(expression, out var value)) return (expression, "Erro");
            if (key == "%") value /= 100d; if (key == "±") value = -value;
            var result = value.ToString("0.############", CultureInfo.InvariantCulture); return (result, result);
        }
        var token = key switch { "×" => "*", "÷" => "/", "," => ".", _ => key };
        if (token.Length != 1 || !"0123456789.+-*/".Contains(token, StringComparison.Ordinal)) return (expression, expression.Length == 0 ? "0" : Display(expression));
        if ("+-*/".Contains(token, StringComparison.Ordinal) && (expression.Length == 0 || "+-*/".Contains(expression[^1])))
        { if (token == "-" && expression.Length == 0) expression = "-"; else if (expression.Length > 0) expression = expression[..^1] + token; }
        else if (token == "." && CurrentNumber(expression).Contains('.')) { }
        else expression += token;
        return (expression, expression.Length == 0 ? "0" : Display(expression));
    }
    public static bool TryEvaluate(string? expression, out double value)
    {
        value = 0;
        try { var parser = new Parser(Normalize(expression)); value = parser.ParseExpression(); return parser.AtEnd && double.IsFinite(value); }
        catch (FormatException) { return false; } catch (DivideByZeroException) { return false; }
    }
    private static string Display(string expression) => expression.Replace("*", "×").Replace("/", "÷");
    private static string Normalize(string? value) => (value ?? "").Trim().Replace(',', '.').Replace('×', '*').Replace('÷', '/');
    private static string CurrentNumber(string expression) { var index = expression.LastIndexOfAny(['+', '-', '*', '/']); return index < 0 ? expression : expression[(index + 1)..]; }
    private sealed class Parser(string source)
    {
        private int _index;
        public bool AtEnd { get { SkipWhitespace(); return _index == source.Length; } }
        public double ParseExpression() { var value = ParseTerm(); while (true) { SkipWhitespace(); if (Take('+')) value += ParseTerm(); else if (Take('-')) value -= ParseTerm(); else return value; } }
        private double ParseTerm() { var value = ParseFactor(); while (true) { SkipWhitespace(); if (Take('*')) value *= ParseFactor(); else if (Take('/')) { var divisor = ParseFactor(); if (Math.Abs(divisor) < double.Epsilon) throw new DivideByZeroException(); value /= divisor; } else return value; } }
        private double ParseFactor() { SkipWhitespace(); var sign = Take('-') ? -1d : Take('+') ? 1d : 1d; SkipWhitespace(); var start = _index; while (_index < source.Length && (char.IsDigit(source[_index]) || source[_index] == '.')) _index++; if (start == _index || !double.TryParse(source[start.._index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) throw new FormatException(); return sign * value; }
        private bool Take(char value) { if (_index >= source.Length || source[_index] != value) return false; _index++; return true; }
        private void SkipWhitespace() { while (_index < source.Length && char.IsWhiteSpace(source[_index])) _index++; }
    }
}
