using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClipDesk.Plugin.Clock;

public sealed record WorldCity(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("city")] string City,
    [property: JsonPropertyName("zone")] string Zone
);

public static class TimeZoneResolver
{
    private static readonly Dictionary<string, string> IanaToWindowsMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["America/Sao_Paulo"] = "E. South America Standard Time",
        ["America/New_York"] = "Eastern Standard Time",
        ["America/Chicago"] = "Central Standard Time",
        ["America/Denver"] = "Mountain Standard Time",
        ["America/Los_Angeles"] = "Pacific Standard Time",
        ["Europe/London"] = "GMT Standard Time",
        ["Europe/Paris"] = "Romance Standard Time",
        ["Europe/Berlin"] = "W. Europe Standard Time",
        ["Europe/Rome"] = "W. Europe Standard Time",
        ["Europe/Madrid"] = "Romance Standard Time",
        ["Asia/Tokyo"] = "Tokyo Standard Time",
        ["Asia/Shanghai"] = "China Standard Time",
        ["Asia/Hong_Kong"] = "China Standard Time",
        ["Asia/Singapore"] = "Singapore Standard Time",
        ["Asia/Dubai"] = "Arabian Standard Time",
        ["Asia/Kolkata"] = "India Standard Time",
        ["Asia/Bangkok"] = "SE Asia Standard Time",
        ["Australia/Sydney"] = "AUS Eastern Standard Time",
        ["Australia/Melbourne"] = "AUS Eastern Standard Time",
        ["Pacific/Auckland"] = "New Zealand Standard Time",
        ["Pacific/Honolulu"] = "Hawaiian Standard Time",
        ["UTC"] = "UTC"
    };

    private static readonly Dictionary<string, string> WindowsToIanaMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["E. South America Standard Time"] = "America/Sao_Paulo",
        ["Eastern Standard Time"] = "America/New_York",
        ["Central Standard Time"] = "America/Chicago",
        ["Mountain Standard Time"] = "America/Denver",
        ["Pacific Standard Time"] = "America/Los_Angeles",
        ["GMT Standard Time"] = "Europe/London",
        ["Romance Standard Time"] = "Europe/Paris",
        ["W. Europe Standard Time"] = "Europe/Berlin",
        ["Tokyo Standard Time"] = "Asia/Tokyo",
        ["China Standard Time"] = "Asia/Shanghai",
        ["Singapore Standard Time"] = "Asia/Singapore",
        ["Arabian Standard Time"] = "Asia/Dubai",
        ["India Standard Time"] = "Asia/Kolkata",
        ["SE Asia Standard Time"] = "Asia/Bangkok",
        ["AUS Eastern Standard Time"] = "Australia/Sydney",
        ["New Zealand Standard Time"] = "Pacific/Auckland",
        ["Hawaiian Standard Time"] = "Pacific/Honolulu",
        ["UTC"] = "UTC"
    };

    public static TimeZoneInfo Resolve(string? zoneId)
    {
        if (string.IsNullOrWhiteSpace(zoneId) || string.Equals(zoneId, "Local", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.Local;
        }

        if (string.Equals(zoneId, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.Utc;
        }

        // 1. Direct system lookup
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        }
        catch
        {
            // Proceed to fallbacks
        }

        // 2. Try IANA to Windows conversion via BCL
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(zoneId, out var winId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(winId);
            }
            catch
            {
                // Proceed
            }
        }

        // 3. Try Windows to IANA conversion via BCL
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(zoneId, out var ianaId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
            }
            catch
            {
                // Proceed
            }
        }

        // 4. Well-known fallback dictionary lookup
        if (IanaToWindowsMap.TryGetValue(zoneId, out var mappedWinId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(mappedWinId);
            }
            catch
            {
                // Proceed
            }
        }

        if (WindowsToIanaMap.TryGetValue(zoneId, out var mappedIanaId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(mappedIanaId);
            }
            catch
            {
                // Proceed
            }
        }

        // 5. Try fixed UTC offset pattern (e.g., UTC+03:00, UTC-5, +02:00)
        if (TryExtractOffset(zoneId, out var offset))
        {
            try
            {
                var customId = $"UTC{(offset >= TimeSpan.Zero ? "+" : "-")}{offset:hh\\:mm}";
                return TimeZoneInfo.CreateCustomTimeZone(customId, offset, customId, customId);
            }
            catch
            {
                // Fallback to UTC
            }
        }

        return TimeZoneInfo.Utc;
    }

    private static bool TryExtractOffset(string text, out TimeSpan offset)
    {
        offset = TimeSpan.Zero;
        var clean = text.Trim();
        if (clean.StartsWith("UTC", StringComparison.OrdinalIgnoreCase) || clean.StartsWith("GMT", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean[3..].Trim();
        }

        if (string.IsNullOrEmpty(clean)) return false;

        if (TimeSpan.TryParse(clean, CultureInfo.InvariantCulture, out offset))
        {
            return true;
        }

        if (int.TryParse(clean, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours) && hours >= -14 && hours <= 14)
        {
            offset = TimeSpan.FromHours(hours);
            return true;
        }

        return false;
    }

    public static DateTimeOffset GetLocalTime(DateTimeOffset utcNow, string? zoneId)
    {
        var tz = Resolve(zoneId);
        return TimeZoneInfo.ConvertTime(utcNow, tz);
    }

    public static bool IsDaytime(DateTimeOffset time)
    {
        int hour = time.Hour;
        return hour >= 6 && hour < 18;
    }

    public static string FormatOffset(TimeSpan offset)
    {
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var abs = offset.Duration();
        return $"UTC{sign}{abs.Hours:D2}:{abs.Minutes:D2}";
    }

    public static string GetDayDifference(DateTimeOffset targetTime, DateTimeOffset referenceTime)
    {
        var targetDate = targetTime.Date;
        var refDate = referenceTime.Date;
        var diff = (targetDate - refDate).Days;

        return diff switch
        {
            0 => "Hoje",
            1 => "+1d",
            -1 => "-1d",
            > 1 => $"+{diff}d",
            < -1 => $"{diff}d"
        };
    }

    public static string FormatTime(DateTimeOffset time, bool is24Hour, bool showSeconds)
    {
        if (is24Hour)
        {
            return showSeconds
                ? time.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                : time.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        return showSeconds
            ? time.ToString("hh:mm:ss tt", CultureInfo.InvariantCulture)
            : time.ToString("hh:mm tt", CultureInfo.InvariantCulture);
    }
}
