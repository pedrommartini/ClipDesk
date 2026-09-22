using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.CurrencyConverter;

public sealed class CurrencyConverterModule : IClipDeskPluginModule
{
    private static readonly ConcurrentDictionary<(string Source, string Target), (decimal Rate, DateTimeOffset Fetched)> Rates = new();
    public static readonly IReadOnlyList<PluginOption> FallbackCurrencies =
    [
        new("AUD", "Dólar australiano"), new("BRL", "Real brasileiro"), new("CAD", "Dólar canadense"),
        new("CHF", "Franco suíço"), new("CNY", "Yuan chinês"), new("EUR", "Euro"),
        new("GBP", "Libra esterlina"), new("JPY", "Iene japonês"), new("USD", "Dólar americano")
    ];
    public string Id => BuiltInPluginIds.CurrencyConverter;
    public int StateVersion => 1;
    public PluginState CreateDefaultState() => new(new Dictionary<string, string>
    { [PluginStateKeys.SchemaVersion] = "1", ["sourceCurrency"] = "BRL", ["targetCurrency"] = "USD", ["amount"] = "1", ["result"] = "Escolha as moedas e converta" });
    public PluginState NormalizeState(PluginState state)
    {
        var values = state.ToDictionary(); values[PluginStateKeys.SchemaVersion] = "1";
        values.TryAdd("sourceCurrency", "BRL"); values.TryAdd("targetCurrency", "USD"); values.TryAdd("amount", "1"); values.TryAdd("result", "Escolha as moedas e converta");
        values["sourceCurrency"] = NormalizeCode(values["sourceCurrency"], "BRL"); values["targetCurrency"] = NormalizeCode(values["targetCurrency"], "USD");
        return new PluginState(values);
    }
    public async ValueTask<PluginCommandResult> ExecuteAsync(PluginState state, PluginCommand command,
        IPluginExecutionContext context, CancellationToken cancellationToken = default)
    {
        state = NormalizeState(state);
        if (command.Name == "set")
        {
            var key = command.Argument("key");
            if (key is not ("amount" or "sourceCurrency" or "targetCurrency")) return PluginCommandResult.Invalid(state, "Campo inválido.");
            return new PluginCommandResult(NormalizeState(state.With(key, command.Argument("value") ?? "")));
        }
        if (command.Name == "swap") return new PluginCommandResult(state.With("sourceCurrency", state.GetString("targetCurrency")).With("targetCurrency", state.GetString("sourceCurrency")));
        if (command.Name == "currencies")
        {
            if (!context.HasPermission(PluginPermissions.Network)) return new PluginCommandResult(state, PluginCommandStatus.PermissionDenied, "Acesso à rede não autorizado.");
            try
            {
                var payload = await context.Network.GetStringAsync(new Uri("https://api.frankfurter.dev/v2/currencies"), cancellationToken);
                using var json = JsonDocument.Parse(payload);
                var options = json.RootElement.EnumerateArray().Select(entry => new PluginOption(entry.GetProperty("iso_code").GetString() ?? "", entry.GetProperty("name").GetString() ?? ""))
                    .Where(x => x.Value.Length > 0 && x.Label.Length > 0).OrderBy(x => x.Value).ToArray();
                return new PluginCommandResult(state, Data: new Dictionary<string, string> { ["options"] = JsonSerializer.Serialize(options) });
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { return new PluginCommandResult(state, PluginCommandStatus.Unavailable, "Lista completa indisponível.", new Dictionary<string, string> { ["detail"] = ex.Message }); }
        }
        if (command.Name != "convert") return PluginCommandResult.Invalid(state, "Comando desconhecido.");
        var raw = (state.GetString("amount") ?? "").Replace(',', '.');
        if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)) return PluginCommandResult.Invalid(state, "Digite um valor válido.");
        var source = state.GetString("sourceCurrency")!; var target = state.GetString("targetCurrency")!;
        if (!context.HasPermission(PluginPermissions.Network) && source != target) return new PluginCommandResult(state, PluginCommandStatus.PermissionDenied, "Acesso à rede não autorizado.");
        try
        {
            var rate = source == target ? 1m : await GetRateAsync(source, target, context, cancellationToken);
            var result = $"{(amount * rate).ToString("N2", CultureInfo.GetCultureInfo("pt-BR"))} {target}";
            return new PluginCommandResult(state.With("result", result), Data: new Dictionary<string, string> { ["rate"] = rate.ToString(CultureInfo.InvariantCulture) });
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new PluginCommandResult(state, PluginCommandStatus.Unavailable, "Cotação indisponível no momento.", new Dictionary<string, string> { ["detail"] = ex.Message }); }
    }
    public string? GetClipboardText(PluginState state) => NormalizeState(state).GetString("result");
    private static async Task<decimal> GetRateAsync(string source, string target, IPluginExecutionContext context, CancellationToken token)
    {
        var pair = (source, target);
        if (Rates.TryGetValue(pair, out var cached) && context.UtcNow - cached.Fetched < TimeSpan.FromMinutes(5)) return cached.Rate;
        var payload = await context.Network.GetStringAsync(new Uri($"https://api.frankfurter.dev/v2/rates?base={Uri.EscapeDataString(source)}&quotes={Uri.EscapeDataString(target)}"), token);
        using var json = JsonDocument.Parse(payload);
        var entry = json.RootElement.EnumerateArray().FirstOrDefault(item => item.TryGetProperty("quote", out var quote) && quote.GetString()?.Equals(target, StringComparison.OrdinalIgnoreCase) == true);
        if (entry.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("Cotação indisponível.");
        var rate = entry.GetProperty("rate").GetDecimal(); Rates[pair] = (rate, context.UtcNow); return rate;
    }
    private static string NormalizeCode(string value, string fallback)
    { var code = value.Trim().ToUpperInvariant(); return code.Length is >= 3 and <= 5 && code.All(c => char.IsLetter(c)) ? code : fallback; }
}
