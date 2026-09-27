using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClipDesk.PluginSdk;

namespace ClipDesk.Plugin.Timezone;

/// <summary>
/// Portable module implementing multi-timezone conversion, 24h timeline calculation,
/// and meeting feasibility assessment without UI or platform references.
/// </summary>
public sealed class TimezoneModule : IClipDeskPluginModule
{
    public const string PluginId = "clipdesk.timezone";
    public const int CurrentStateVersion = 1;

    public const string KeyReferenceZone = "referenceZone";
    public const string KeyReferenceCity = "referenceCity";
    public const string KeySelectedMinuteOfDay = "selectedMinuteOfDay";
    public const string KeyIsLive = "isLive";
    public const string KeyZones = "zones";

    public static readonly IReadOnlyList<TimezoneCity> DefaultCities = new List<TimezoneCity>
    {
        new("sp", "São Paulo", "America/Sao_Paulo"),
        new("nyc", "Nova York", "America/New_York"),
        new("lon", "Londres", "Europe/London"),
        new("tyo", "Tóquio", "Asia/Tokyo")
    };

    public string Id => PluginId;
    public int StateVersion => CurrentStateVersion;

    public PluginState CreateDefaultState()
    {
        return new PluginState(new Dictionary<string, string>
        {
            [PluginStateKeys.SchemaVersion] = "1",
            [KeyReferenceZone] = "America/Sao_Paulo",
            [KeyReferenceCity] = "São Paulo",
            [KeySelectedMinuteOfDay] = "840", // 14:00
            [KeyIsLive] = "false",
            [KeyZones] = JsonSerializer.Serialize(DefaultCities)
        });
    }

    public PluginState NormalizeState(PluginState state)
    {
        var dict = state.ToDictionary();
        dict[PluginStateKeys.SchemaVersion] = "1";

        var refZone = (state.GetString(KeyReferenceZone) ?? "").Trim();
        if (string.IsNullOrWhiteSpace(refZone)) refZone = "America/Sao_Paulo";
        dict[KeyReferenceZone] = refZone;

        var refCity = (state.GetString(KeyReferenceCity) ?? "").Trim();
        if (string.IsNullOrWhiteSpace(refCity)) refCity = "São Paulo";
        dict[KeyReferenceCity] = refCity;

        var minuteVal = state.GetInt32(KeySelectedMinuteOfDay, 840);
        minuteVal = Math.Clamp(minuteVal, 0, 1439);
        dict[KeySelectedMinuteOfDay] = minuteVal.ToString(CultureInfo.InvariantCulture);

        var isLive = string.Equals(state.GetString(KeyIsLive), "true", StringComparison.OrdinalIgnoreCase);
        dict[KeyIsLive] = isLive ? "true" : "false";

        // Parse zones list
        var zonesJson = state.GetString(KeyZones);
        List<TimezoneCity>? parsedZones = null;
        if (!string.IsNullOrWhiteSpace(zonesJson))
        {
            try
            {
                parsedZones = JsonSerializer.Deserialize<List<TimezoneCity>>(zonesJson);
            }
            catch
            {
                parsedZones = null;
            }
        }

        if (parsedZones == null || parsedZones.Count == 0)
        {
            parsedZones = new List<TimezoneCity>(DefaultCities);
        }
        else
        {
            // Sanitize list
            var sanitized = new List<TimezoneCity>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var z in parsedZones)
            {
                if (string.IsNullOrWhiteSpace(z.City) || string.IsNullOrWhiteSpace(z.Zone))
                    continue;

                var id = string.IsNullOrWhiteSpace(z.Id) ? Slugify(z.City) : z.Id.Trim();
                if (seenIds.Add(id))
                {
                    sanitized.Add(new TimezoneCity(id, z.City.Trim(), z.Zone.Trim()));
                }
            }

            if (sanitized.Count == 0)
                sanitized.AddRange(DefaultCities);

            parsedZones = sanitized;
        }

        // Ensure reference city is in the zones list
        if (!parsedZones.Any(z => string.Equals(z.Zone, refZone, StringComparison.OrdinalIgnoreCase)))
        {
            var refId = Slugify(refCity);
            if (parsedZones.Any(z => z.Id.Equals(refId, StringComparison.OrdinalIgnoreCase)))
                refId = $"{refId}-ref";
            parsedZones.Insert(0, new TimezoneCity(refId, refCity, refZone));
        }

        dict[KeyZones] = JsonSerializer.Serialize(parsedZones);
        return new PluginState(dict);
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
            "set-reference-time" => ValueTask.FromResult(HandleSetReferenceTime(state, command)),
            "set-live" => ValueTask.FromResult(HandleSetLive(state, command, context)),
            "set-current-time" or "now" => ValueTask.FromResult(HandleSetCurrentTime(state, context)),
            "add-zone" => ValueTask.FromResult(HandleAddZone(state, command)),
            "remove-zone" => ValueTask.FromResult(HandleRemoveZone(state, command)),
            "swap-reference" => ValueTask.FromResult(HandleSwapReference(state, command, context)),
            _ => ValueTask.FromResult(PluginCommandResult.Invalid(state, "Comando desconhecido."))
        };
    }

    public string? GetClipboardText(PluginState state)
    {
        state = NormalizeState(state);
        var refZone = state.GetString(KeyReferenceZone)!;
        var refCity = state.GetString(KeyReferenceCity)!;
        var minute = state.GetInt32(KeySelectedMinuteOfDay, 840);
        var zones = ParseZones(state);

        var evaluation = TimezoneEngine.Evaluate(DateTimeOffset.UtcNow, refZone, minute, zones);
        return TimezoneEngine.FormatProposalText(evaluation, refCity);
    }

    public static List<TimezoneCity> ParseZones(PluginState state)
    {
        var json = state.GetString(KeyZones);
        if (string.IsNullOrWhiteSpace(json)) return new List<TimezoneCity>(DefaultCities);
        try
        {
            return JsonSerializer.Deserialize<List<TimezoneCity>>(json) ?? new List<TimezoneCity>(DefaultCities);
        }
        catch
        {
            return new List<TimezoneCity>(DefaultCities);
        }
    }

    private static PluginCommandResult HandleSetReferenceTime(PluginState state, PluginCommand command)
    {
        var raw = command.Argument("minuteOfDay") ?? command.Argument("minute") ?? command.Argument("value");
        if (string.IsNullOrWhiteSpace(raw) || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minute))
        {
            return PluginCommandResult.Invalid(state, "Minuto do dia inválido.");
        }

        minute = Math.Clamp(minute, 0, 1439);
        var next = state
            .With(KeySelectedMinuteOfDay, minute.ToString(CultureInfo.InvariantCulture))
            .With(KeyIsLive, "false");

        return new PluginCommandResult(next);
    }

    private static PluginCommandResult HandleSetLive(PluginState state, PluginCommand command, IPluginExecutionContext context)
    {
        var raw = command.Argument("live") ?? "true";
        var isLive = string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);

        if (isLive)
        {
            var refZoneId = state.GetString(KeyReferenceZone)!;
            var tz = TimezoneEngine.ResolveTimeZone(refZoneId);
            var local = TimeZoneInfo.ConvertTime(context.UtcNow, tz);
            var minute = (local.Hour * 60) + local.Minute;

            var next = state
                .With(KeyIsLive, "true")
                .With(KeySelectedMinuteOfDay, minute.ToString(CultureInfo.InvariantCulture));
            return new PluginCommandResult(next);
        }
        else
        {
            var next = state.With(KeyIsLive, "false");
            return new PluginCommandResult(next);
        }
    }

    private static PluginCommandResult HandleSetCurrentTime(PluginState state, IPluginExecutionContext context)
    {
        var refZoneId = state.GetString(KeyReferenceZone)!;
        var tz = TimezoneEngine.ResolveTimeZone(refZoneId);
        var local = TimeZoneInfo.ConvertTime(context.UtcNow, tz);
        var minute = (local.Hour * 60) + local.Minute;

        var next = state.With(KeySelectedMinuteOfDay, minute.ToString(CultureInfo.InvariantCulture));
        return new PluginCommandResult(next);
    }

    private static PluginCommandResult HandleAddZone(PluginState state, PluginCommand command)
    {
        var city = command.Argument("city")?.Trim();
        var zone = command.Argument("zone")?.Trim();
        var id = command.Argument("id")?.Trim();

        if (string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(zone))
            return PluginCommandResult.Invalid(state, "Cidade e fuso horário são obrigatórios.");

        if (string.IsNullOrWhiteSpace(id))
            id = Slugify(city);

        var zones = ParseZones(state);
        if (zones.Count >= 16)
            return PluginCommandResult.Invalid(state, "Limite máximo de 16 cidades atingido.");

        // Check if city/zone already exists
        var existingIndex = zones.FindIndex(z =>
            string.Equals(z.Id, id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(z.City, city, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
        {
            zones[existingIndex] = new TimezoneCity(zones[existingIndex].Id, city, zone);
        }
        else
        {
            zones.Add(new TimezoneCity(id, city, zone));
        }

        var next = state.With(KeyZones, JsonSerializer.Serialize(zones));
        return new PluginCommandResult(next);
    }

    private static PluginCommandResult HandleRemoveZone(PluginState state, PluginCommand command)
    {
        var id = command.Argument("id")?.Trim();
        if (string.IsNullOrWhiteSpace(id))
            return PluginCommandResult.Invalid(state, "Identificador da cidade não informado.");

        var zones = ParseZones(state);
        var removed = zones.RemoveAll(z => string.Equals(z.Id, id, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
            return PluginCommandResult.Invalid(state, "Cidade não encontrada.");

        if (zones.Count == 0)
            zones.AddRange(DefaultCities);

        var refZone = state.GetString(KeyReferenceZone)!;
        var refCity = state.GetString(KeyReferenceCity)!;

        // If removed city was the reference, select the first remaining city
        if (!zones.Any(z => string.Equals(z.Zone, refZone, StringComparison.OrdinalIgnoreCase)))
        {
            refZone = zones[0].Zone;
            refCity = zones[0].City;
        }

        var next = state
            .With(KeyZones, JsonSerializer.Serialize(zones))
            .With(KeyReferenceZone, refZone)
            .With(KeyReferenceCity, refCity);

        return new PluginCommandResult(next);
    }

    private static PluginCommandResult HandleSwapReference(PluginState state, PluginCommand command, IPluginExecutionContext context)
    {
        var id = command.Argument("id")?.Trim();
        var zone = command.Argument("zone")?.Trim();
        var zones = ParseZones(state);
        var targetCity = zones.FirstOrDefault(z =>
            (!string.IsNullOrWhiteSpace(id) && string.Equals(z.Id, id, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(zone) && string.Equals(z.Zone, zone, StringComparison.OrdinalIgnoreCase)));

        if (targetCity == null)
            return PluginCommandResult.Invalid(state, "Cidade alvo não encontrada.");

        var oldRefZoneId = state.GetString(KeyReferenceZone)!;
        var oldMinute = state.GetInt32(KeySelectedMinuteOfDay, 840);

        // Convert the current instant into UTC based on old reference
        var oldRefTz = TimezoneEngine.ResolveTimeZone(oldRefZoneId);
        var currentUtc = TimezoneEngine.CalculateUtcFromReference(context.UtcNow, oldRefTz, oldMinute);

        // Now calculate new local minute in target city's timezone
        var targetTz = TimezoneEngine.ResolveTimeZone(targetCity.Zone);
        var targetLocal = TimeZoneInfo.ConvertTime(currentUtc, targetTz);
        var newMinute = (targetLocal.Hour * 60) + targetLocal.Minute;

        var next = state
            .With(KeyReferenceZone, targetCity.Zone)
            .With(KeyReferenceCity, targetCity.City)
            .With(KeySelectedMinuteOfDay, newMinute.ToString(CultureInfo.InvariantCulture));

        return new PluginCommandResult(next);
    }

    private static string Slugify(string text)
    {
        var clean = Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(clean) ? Guid.NewGuid().ToString("N")[..6] : clean;
    }
}
