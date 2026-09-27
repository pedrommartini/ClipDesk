using System.Globalization;
using System.Text.Json;
using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.Clock;

public sealed class ClockModule : IClipDeskPluginModule
{
    public const string PluginId = "clipdesk.clock";
    public const int CurrentStateVersion = 1;
    public const int MaxWorldCities = 16;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private static readonly List<WorldCity> DefaultCities =
    [
        new("sp", "São Paulo", "America/Sao_Paulo"),
        new("nyc", "Nova York", "America/New_York"),
        new("lon", "Londres", "Europe/London"),
        new("tyo", "Tóquio", "Asia/Tokyo")
    ];

    public string Id => PluginId;
    public int StateVersion => CurrentStateVersion;

    public PluginState CreateDefaultState() => new(new Dictionary<string, string>
    {
        [PluginStateKeys.SchemaVersion] = "1",
        ["displayMode"] = "analog",
        ["is24Hour"] = "true",
        ["showSeconds"] = "true",
        ["primaryZone"] = "",
        ["worldClocks"] = JsonSerializer.Serialize(DefaultCities, JsonOptions)
    });

    public PluginState NormalizeState(PluginState state)
    {
        var values = state.ToDictionary();
        values[PluginStateKeys.SchemaVersion] = "1";

        var mode = state.GetString("displayMode");
        values["displayMode"] = string.Equals(mode, "digital", StringComparison.OrdinalIgnoreCase) ? "digital" : "analog";

        var is24H = state.GetString("is24Hour");
        values["is24Hour"] = string.Equals(is24H, "false", StringComparison.OrdinalIgnoreCase) ? "false" : "true";

        var showSec = state.GetString("showSeconds");
        values["showSeconds"] = string.Equals(showSec, "false", StringComparison.OrdinalIgnoreCase) ? "false" : "true";

        values["primaryZone"] = (state.GetString("primaryZone") ?? "").Trim();

        var citiesJson = state.GetString("worldClocks");
        List<WorldCity> cities;
        try
        {
            cities = string.IsNullOrWhiteSpace(citiesJson)
                ? new List<WorldCity>(DefaultCities)
                : JsonSerializer.Deserialize<List<WorldCity>>(citiesJson, JsonOptions) ?? new List<WorldCity>(DefaultCities);
        }
        catch
        {
            cities = new List<WorldCity>(DefaultCities);
        }

        // Deduplicate by ID and clamp to MaxWorldCities
        var sanitized = new List<WorldCity>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in cities)
        {
            if (string.IsNullOrWhiteSpace(c.City)) continue;
            var id = string.IsNullOrWhiteSpace(c.Id) ? GenerateId(c.City) : c.Id.Trim();
            if (seenIds.Add(id))
            {
                sanitized.Add(new WorldCity(id, c.City.Trim(), (c.Zone ?? "").Trim()));
                if (sanitized.Count >= MaxWorldCities) break;
            }
        }

        values["worldClocks"] = JsonSerializer.Serialize(sanitized, JsonOptions);
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
            "toggle-format" => ValueTask.FromResult(ToggleFormat(state)),
            "toggle-seconds" => ValueTask.FromResult(ToggleSeconds(state)),
            "set-display-mode" => ValueTask.FromResult(SetDisplayMode(state, command.Argument("mode"))),
            "add-world-clock" => ValueTask.FromResult(AddWorldClock(state, command.Argument("id"), command.Argument("city"), command.Argument("zone"))),
            "remove-world-clock" => ValueTask.FromResult(RemoveWorldClock(state, command.Argument("id"))),
            "set-primary-zone" => ValueTask.FromResult(SetPrimaryZone(state, command.Argument("zone"))),
            "set-state" => ValueTask.FromResult(SetState(state, command.Arguments)),
            _ => ValueTask.FromResult(PluginCommandResult.Invalid(state, $"Comando desconhecido: '{command.Name}'."))
        };
    }

    public string? GetClipboardText(PluginState state)
    {
        state = NormalizeState(state);
        var now = DateTimeOffset.UtcNow;
        var is24H = string.Equals(state.GetString("is24Hour"), "true", StringComparison.OrdinalIgnoreCase);
        var showSec = string.Equals(state.GetString("showSeconds"), "true", StringComparison.OrdinalIgnoreCase);
        var primaryZoneId = state.GetString("primaryZone");

        var primaryTime = TimeZoneResolver.GetLocalTime(now, primaryZoneId);
        var primaryOffset = TimeZoneResolver.FormatOffset(primaryTime.Offset);
        var primaryTimeStr = TimeZoneResolver.FormatTime(primaryTime, is24H, showSec);
        var primaryDateStr = primaryTime.ToString("D", new CultureInfo("pt-BR"));

        var cities = GetCities(state);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("🕒 Relógio Mundial ClipDesk");
        sb.AppendLine($"• Principal: {primaryTimeStr} ({primaryOffset}) - {primaryDateStr}");

        if (cities.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Cidades Mundiais:");
            foreach (var city in cities)
            {
                var cityTime = TimeZoneResolver.GetLocalTime(now, city.Zone);
                var cityTimeStr = TimeZoneResolver.FormatTime(cityTime, is24H, showSec);
                var offsetStr = TimeZoneResolver.FormatOffset(cityTime.Offset);
                var dayDiff = TimeZoneResolver.GetDayDifference(cityTime, primaryTime);
                var diffPart = dayDiff == "Hoje" ? "" : $", {dayDiff}";
                var sunMoon = TimeZoneResolver.IsDaytime(cityTime) ? "☀️" : "🌙";

                sb.AppendLine($"• {city.City}: {cityTimeStr} ({offsetStr}{diffPart}) {sunMoon}");
            }
        }

        return sb.ToString();
    }

    public static List<WorldCity> GetCities(PluginState state)
    {
        var json = state.GetString("worldClocks");
        if (string.IsNullOrWhiteSpace(json)) return new List<WorldCity>(DefaultCities);
        try
        {
            return JsonSerializer.Deserialize<List<WorldCity>>(json, JsonOptions) ?? new List<WorldCity>(DefaultCities);
        }
        catch
        {
            return new List<WorldCity>(DefaultCities);
        }
    }

    private static PluginCommandResult ToggleFormat(PluginState state)
    {
        var current = string.Equals(state.GetString("is24Hour"), "true", StringComparison.OrdinalIgnoreCase);
        var next = current ? "false" : "true";
        return new PluginCommandResult(state.With("is24Hour", next));
    }

    private static PluginCommandResult ToggleSeconds(PluginState state)
    {
        var current = string.Equals(state.GetString("showSeconds"), "true", StringComparison.OrdinalIgnoreCase);
        var next = current ? "false" : "true";
        return new PluginCommandResult(state.With("showSeconds", next));
    }

    private static PluginCommandResult SetDisplayMode(PluginState state, string? mode)
    {
        var cleanMode = string.Equals(mode, "digital", StringComparison.OrdinalIgnoreCase) ? "digital" : "analog";
        return new PluginCommandResult(state.With("displayMode", cleanMode));
    }

    private static PluginCommandResult AddWorldClock(PluginState state, string? id, string? city, string? zone)
    {
        if (string.IsNullOrWhiteSpace(city))
        {
            return PluginCommandResult.Invalid(state, "O nome da cidade não pode estar vazio.");
        }

        city = city.Trim();
        zone = (zone ?? "UTC").Trim();
        id = string.IsNullOrWhiteSpace(id) ? GenerateId(city) : id.Trim();

        var cities = GetCities(state);

        // Check if city exists by id or by city name
        var existingIndex = cities.FindIndex(c =>
            string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.City, city, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            cities[existingIndex] = new WorldCity(cities[existingIndex].Id, city, zone);
        }
        else
        {
            if (cities.Count >= MaxWorldCities)
            {
                return PluginCommandResult.Invalid(state, $"Limite máximo de {MaxWorldCities} cidades atingido.");
            }
            cities.Add(new WorldCity(id, city, zone));
        }

        var json = JsonSerializer.Serialize(cities, JsonOptions);
        return new PluginCommandResult(state.With("worldClocks", json));
    }

    private static PluginCommandResult RemoveWorldClock(PluginState state, string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return PluginCommandResult.Invalid(state, "ID ou nome da cidade inválido para remoção.");
        }

        var cities = GetCities(state);
        var target = id.Trim();
        var removed = cities.RemoveAll(c =>
            string.Equals(c.Id, target, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.City, target, StringComparison.OrdinalIgnoreCase));

        if (removed == 0)
        {
            return PluginCommandResult.Invalid(state, $"Cidade '{id}' não encontrada.");
        }

        var json = JsonSerializer.Serialize(cities, JsonOptions);
        return new PluginCommandResult(state.With("worldClocks", json));
    }

    private static PluginCommandResult SetPrimaryZone(PluginState state, string? zone)
    {
        var cleanZone = (zone ?? "").Trim();
        return new PluginCommandResult(state.With("primaryZone", cleanZone));
    }

    private static PluginCommandResult SetState(PluginState state, IReadOnlyDictionary<string, string>? args)
    {
        if (args == null || args.Count == 0) return new PluginCommandResult(state);
        var next = state.ToDictionary();
        foreach (var (k, v) in args)
        {
            next[k] = v;
        }
        return new PluginCommandResult(new PluginState(next));
    }

    private static string GenerateId(string text)
    {
        var safe = new string(text.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrEmpty(safe) ? Guid.NewGuid().ToString("N")[..6] : safe;
    }
}
