using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace ClipDesk.Services;

internal sealed class BoardUtilityService
{
    private static readonly HttpClient Http = new(new RequestCooldownHandler()) { Timeout = TimeSpan.FromSeconds(12) };
    private IReadOnlyDictionary<string, string>? _currencyCache;
    private readonly Dictionary<(string Source, string Target), (decimal Rate, string Date, DateTimeOffset FetchedAt)> _rateCache = [];

    public async Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var clipped = text.Trim();
        while (System.Text.Encoding.UTF8.GetByteCount(clipped) > 500) clipped = clipped[..^1];
        var resolvedSource = sourceLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase) ? "autodetect" : sourceLanguage;
        var url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(clipped)}&langpair={Uri.EscapeDataString(resolvedSource)}%7C{Uri.EscapeDataString(targetLanguage)}";
        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var translated = json.RootElement.GetProperty("responseData").GetProperty("translatedText").GetString();
        if (string.IsNullOrWhiteSpace(translated)) throw new InvalidOperationException("O serviço não retornou uma tradução.");
        return System.Net.WebUtility.HtmlDecode(translated).Trim();
    }

    public async Task<IReadOnlyDictionary<string, string>> GetCurrenciesAsync(CancellationToken cancellationToken = default)
    {
        if (_currencyCache is not null) return _currencyCache;
        using var response = await Http.GetAsync("https://api.frankfurter.dev/v2/currencies", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var currencies = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in json.RootElement.EnumerateArray())
        {
            var code = entry.TryGetProperty("iso_code", out var codeValue) ? codeValue.GetString() : null;
            var name = entry.TryGetProperty("name", out var nameValue) ? nameValue.GetString() : null;
            if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(name)) currencies[code] = name;
        }
        if (currencies.Count == 0) throw new InvalidOperationException("O serviço não retornou moedas disponíveis.");
        return _currencyCache = currencies;
    }

    public async Task<(decimal Converted, decimal Rate, string Date)> ConvertCurrencyAsync(decimal amount, string sourceCurrency, string targetCurrency, CancellationToken cancellationToken = default)
    {
        if (sourceCurrency.Equals(targetCurrency, StringComparison.OrdinalIgnoreCase))
            return (amount, 1m, DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var pair = (sourceCurrency.ToUpperInvariant(), targetCurrency.ToUpperInvariant());
        if (_rateCache.TryGetValue(pair, out var cached) && DateTimeOffset.UtcNow - cached.FetchedAt < TimeSpan.FromMinutes(5))
            return (amount * cached.Rate, cached.Rate, cached.Date);
        var url = $"https://api.frankfurter.dev/v2/rates?base={Uri.EscapeDataString(sourceCurrency)}&quotes={Uri.EscapeDataString(targetCurrency)}";
        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var result = json.RootElement.EnumerateArray().FirstOrDefault(entry =>
            entry.TryGetProperty("quote", out var quote) && quote.GetString()?.Equals(targetCurrency, StringComparison.OrdinalIgnoreCase) == true);
        if (result.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("A moeda selecionada não possui cotação disponível.");
        var rate = result.GetProperty("rate").GetDecimal();
        var date = result.GetProperty("date").GetString() ?? string.Empty;
        _rateCache[pair] = (rate, date, DateTimeOffset.UtcNow);
        return (amount * rate, rate, date);
    }
}
