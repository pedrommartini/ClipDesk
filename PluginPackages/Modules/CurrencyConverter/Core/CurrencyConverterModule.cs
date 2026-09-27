using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.CurrencyConverter;

public sealed class CurrencyConverterModule : IClipDeskPluginModule, IClipDeskPluginModuleV3
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
        => await ExecuteCore(state, command, context.HasPermission(PluginPermissions.Network),
            PluginCommandStatus.PermissionDenied, context.Network.GetStringAsync, context.UtcNow, cancellationToken);

    public async ValueTask<PluginCommandResult> ExecuteAsync(PluginState state, PluginCommand command,
        IPluginCapabilityProvider capabilities, CancellationToken cancellationToken = default)
    {
        var http = capabilities.Resolve<IPluginHttp>(PluginHostCapabilityIds.Http, new(1, 0, 0));
        async Task<string> GetString(Uri uri, CancellationToken token)
        {
            var response = await http.Value!.SendAsync(new PluginHttpRequest(uri), token);
            if (response.StatusCode is < 200 or >= 300) throw new InvalidOperationException($"HTTP {response.StatusCode}");
            return Encoding.UTF8.GetString(response.Body);
        }
        return await ExecuteCore(state, command, http.Available,
            http.Status is PluginCapabilityStatus.PermissionDenied or PluginCapabilityStatus.PermissionNotDeclared
                ? PluginCommandStatus.PermissionDenied : PluginCommandStatus.Unavailable,
            GetString, DateTimeOffset.UtcNow, cancellationToken);
    }

    private async ValueTask<PluginCommandResult> ExecuteCore(PluginState state, PluginCommand command,
        bool networkAvailable, PluginCommandStatus networkStatus,
        Func<Uri, CancellationToken, Task<string>> getString, DateTimeOffset now,
        CancellationToken cancellationToken)
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
            if (!networkAvailable) return new PluginCommandResult(state, networkStatus, "Acesso à rede não disponível ou não autorizado.");
            try
            {
                var payload = await getString(new Uri("https://api.frankfurter.dev/v2/currencies"), cancellationToken);
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
        if (!networkAvailable && source != target) return new PluginCommandResult(state, networkStatus, "Acesso à rede não disponível ou não autorizado.");
        try
        {
            var rate = source == target ? 1m : await GetRateAsync(source, target, getString, now, cancellationToken);
            var result = $"{(amount * rate).ToString("N2", CultureInfo.GetCultureInfo("pt-BR"))} {target}";
            return new PluginCommandResult(state.With("result", result), Data: new Dictionary<string, string> { ["rate"] = rate.ToString(CultureInfo.InvariantCulture) });
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new PluginCommandResult(state, PluginCommandStatus.Unavailable, "Cotação indisponível no momento.", new Dictionary<string, string> { ["detail"] = ex.Message }); }
    }
    public string? GetClipboardText(PluginState state) => NormalizeState(state).GetString("result");
    private static async Task<decimal> GetRateAsync(string source, string target,
        Func<Uri, CancellationToken, Task<string>> getString, DateTimeOffset now, CancellationToken token)
    {
        var pair = (source, target);
        if (Rates.TryGetValue(pair, out var cached) && now - cached.Fetched < TimeSpan.FromMinutes(5)) return cached.Rate;
        var payload = await getString(new Uri($"https://api.frankfurter.dev/v2/rates?base={Uri.EscapeDataString(source)}&quotes={Uri.EscapeDataString(target)}"), token);
        using var json = JsonDocument.Parse(payload);
        var entry = json.RootElement.EnumerateArray().FirstOrDefault(item => item.TryGetProperty("quote", out var quote) && quote.GetString()?.Equals(target, StringComparison.OrdinalIgnoreCase) == true);
        if (entry.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("Cotação indisponível.");
        var rate = entry.GetProperty("rate").GetDecimal(); Rates[pair] = (rate, now); return rate;
    }
    private static string NormalizeCode(string value, string fallback)
    { var code = value.Trim().ToUpperInvariant(); return code.Length is >= 3 and <= 5 && code.All(c => char.IsLetter(c)) ? code : fallback; }
}
