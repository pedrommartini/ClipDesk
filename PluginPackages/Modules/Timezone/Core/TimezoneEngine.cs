using System.Globalization;
using System.Text.RegularExpressions;

namespace ClipDesk.Plugin.Timezone;

/// <summary>
/// Core computational engine for timezone resolution, date shifts, and meeting evaluations.
/// </summary>
public static class TimezoneEngine
{
    private static readonly Dictionary<string, string> KnownTimezoneFallbacks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["America/Sao_Paulo"] = "E. South America Standard Time",
        ["E. South America Standard Time"] = "America/Sao_Paulo",
        ["America/New_York"] = "Eastern Standard Time",
        ["Eastern Standard Time"] = "America/New_York",
        ["Europe/London"] = "GMT Standard Time",
        ["GMT Standard Time"] = "Europe/London",
        ["Asia/Tokyo"] = "Tokyo Standard Time",
        ["Tokyo Standard Time"] = "Asia/Tokyo",
        ["Europe/Paris"] = "Romance Standard Time",
        ["Romance Standard Time"] = "Europe/Paris",
        ["Europe/Berlin"] = "W. Europe Standard Time",
        ["W. Europe Standard Time"] = "Europe/Berlin",
        ["Australia/Sydney"] = "AUS Eastern Standard Time",
        ["AUS Eastern Standard Time"] = "Australia/Sydney",
        ["America/Los_Angeles"] = "Pacific Standard Time",
        ["Pacific Standard Time"] = "America/Los_Angeles",
        ["America/Chicago"] = "Central Standard Time",
        ["Central Standard Time"] = "America/Chicago",
        ["Asia/Dubai"] = "Arabian Standard Time",
        ["Arabian Standard Time"] = "Asia/Dubai",
        ["Asia/Kolkata"] = "India Standard Time",
        ["India Standard Time"] = "Asia/Kolkata",
        ["Asia/Singapore"] = "Singapore Standard Time",
        ["Singapore Standard Time"] = "Asia/Singapore",
        ["Asia/Shanghai"] = "China Standard Time",
        ["China Standard Time"] = "Asia/Shanghai",
        ["Asia/Hong_Kong"] = "China Standard Time",
        ["UTC"] = "UTC"
    };

    /// <summary>
    /// Resiliently resolves a timezone from IANA or Windows identifier, or fixed offset.
    /// </summary>
    public static TimeZoneInfo ResolveTimeZone(string? zoneId)
    {
        if (string.IsNullOrWhiteSpace(zoneId))
            return TimeZoneInfo.Utc;

        zoneId = zoneId.Trim();

        // 1. Direct system lookup (works for Windows ID or IANA when ICU is present)
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }

        // 2. Try converting IANA to Windows ID
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(zoneId, out var winId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(winId);
            }
            catch (Exception) { }
        }

        // 3. Try converting Windows ID to IANA
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(zoneId, out var ianaId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
            }
            catch (Exception) { }
        }

        // 4. Known fallback dictionary
        if (KnownTimezoneFallbacks.TryGetValue(zoneId, out var fallbackId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(fallbackId);
            }
            catch (Exception) { }
        }

        // 5. Fixed offset parsing (e.g., UTC+3, UTC-04:00, +05:30)
        if (TryCreateCustomOffsetZone(zoneId, out var customZone))
        {
            return customZone;
        }

        return TimeZoneInfo.Utc;
    }

    private static bool TryCreateCustomOffsetZone(string text, out TimeZoneInfo customZone)
    {
        customZone = TimeZoneInfo.Utc;
        var match = Regex.Match(text, @"^(?:UTC|GMT)?\s*([+-])\s*(\d{1,2})(?::?(\d{2}))?$", RegexOptions.IgnoreCase);
        if (!match.Success) return false;

        var isNegative = match.Groups[1].Value == "-";
        if (!int.TryParse(match.Groups[2].Value, out var hours) || hours > 14) return false;
        var minutes = 0;
        if (match.Groups[3].Success && (!int.TryParse(match.Groups[3].Value, out minutes) || minutes >= 60)) return false;

        var offset = TimeSpan.FromMinutes((hours * 60) + minutes);
        if (isNegative) offset = -offset;

        var name = $"UTC{(isNegative ? "-" : "+")}{hours:D2}:{minutes:D2}";
        try
        {
            customZone = TimeZoneInfo.CreateCustomTimeZone(name, offset, name, name);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Calculates the universal timestamp representing a minute-of-day in the reference timezone.
    /// </summary>
    public static DateTimeOffset CalculateUtcFromReference(DateTimeOffset nowUtc, TimeZoneInfo refTz, int minuteOfDay)
    {
        minuteOfDay = Math.Clamp(minuteOfDay, 0, 1439);

        // Determine current date in the reference timezone
        var refLocalNow = TimeZoneInfo.ConvertTime(nowUtc, refTz).DateTime;
        var localTarget = new DateTime(refLocalNow.Year, refLocalNow.Month, refLocalNow.Day, 0, 0, 0, DateTimeKind.Unspecified)
            .AddMinutes(minuteOfDay);

        // Handle DST spring-forward gap
        if (refTz.IsInvalidTime(localTarget))
        {
            localTarget = localTarget.AddHours(1);
        }

        TimeSpan offset;
        if (refTz.IsAmbiguousTime(localTarget))
        {
            var offsets = refTz.GetAmbiguousTimeOffsets(localTarget);
            offset = offsets.Length > 0 ? offsets[0] : refTz.BaseUtcOffset;
        }
        else
        {
            offset = refTz.GetUtcOffset(localTarget);
        }

        var refDto = new DateTimeOffset(localTarget, offset);
        return refDto.ToUniversalTime();
    }

    /// <summary>
    /// Formats an offset as a clean string (e.g. UTC-3, UTC+5:30).
    /// </summary>
    public static string FormatOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset.Duration();
        if (abs.Minutes == 0)
            return $"UTC{sign}{abs.Hours}";
        return $"UTC{sign}{abs.Hours}:{abs.Minutes:D2}";
    }

    /// <summary>
    /// Obtains standard recognized timezone abbreviation or falls back to offset string.
    /// </summary>
    public static string GetAbbreviation(TimeZoneInfo tz, DateTimeOffset time)
    {
        var id = tz.Id;
        bool dst = tz.IsDaylightSavingTime(time);

        if (id.Contains("Sao_Paulo", StringComparison.OrdinalIgnoreCase) || id.Contains("South America", StringComparison.OrdinalIgnoreCase))
            return dst ? "BRST" : "BRT";
        if (id.Contains("New_York", StringComparison.OrdinalIgnoreCase) || id.Contains("Eastern", StringComparison.OrdinalIgnoreCase))
            return dst ? "EDT" : "EST";
        if (id.Contains("London", StringComparison.OrdinalIgnoreCase) || id.Contains("GMT", StringComparison.OrdinalIgnoreCase))
            return dst ? "BST" : "GMT";
        if (id.Contains("Tokyo", StringComparison.OrdinalIgnoreCase))
            return "JST";
        if (id.Contains("Paris", StringComparison.OrdinalIgnoreCase) || id.Contains("Berlin", StringComparison.OrdinalIgnoreCase) || id.Contains("Romance", StringComparison.OrdinalIgnoreCase))
            return dst ? "CEST" : "CET";
        if (id.Contains("Sydney", StringComparison.OrdinalIgnoreCase) || id.Contains("AUS Eastern", StringComparison.OrdinalIgnoreCase))
            return dst ? "AEDT" : "AEST";
        if (id.Contains("Los_Angeles", StringComparison.OrdinalIgnoreCase) || id.Contains("Pacific", StringComparison.OrdinalIgnoreCase))
            return dst ? "PDT" : "PST";
        if (id.Contains("Chicago", StringComparison.OrdinalIgnoreCase) || id.Contains("Central", StringComparison.OrdinalIgnoreCase))
            return dst ? "CDT" : "CST";
        if (id.Contains("Kolkata", StringComparison.OrdinalIgnoreCase) || id.Contains("India", StringComparison.OrdinalIgnoreCase))
            return "IST";
        if (id.Contains("Dubai", StringComparison.OrdinalIgnoreCase) || id.Contains("Arabian", StringComparison.OrdinalIgnoreCase))
            return "GST";
        if (id.Contains("Singapore", StringComparison.OrdinalIgnoreCase))
            return "SGT";
        if (id.Contains("China", StringComparison.OrdinalIgnoreCase) || id.Contains("Shanghai", StringComparison.OrdinalIgnoreCase))
            return "CST";
        if (id.Equals("UTC", StringComparison.OrdinalIgnoreCase))
            return "UTC";

        return FormatOffset(tz.GetUtcOffset(time));
    }

    /// <summary>
    /// Classifies an hour into Business, Shoulder, or Night hours.
    /// </summary>
    public static TimeSlotCategory CategorizeTime(int hour, int minute)
    {
        if (hour >= 9 && hour < 18)
            return TimeSlotCategory.Business;
        if ((hour >= 7 && hour < 9) || (hour >= 18 && hour < 22))
            return TimeSlotCategory.Shoulder;
        return TimeSlotCategory.Night;
    }

    /// <summary>
    /// Converts time across all configured cities relative to the reference timezone.
    /// </summary>
    public static MeetingEvaluation Evaluate(
        DateTimeOffset nowUtc,
        string referenceZoneId,
        int minuteOfDay,
        IReadOnlyList<TimezoneCity> cities)
    {
        var refTz = ResolveTimeZone(referenceZoneId);
        var baseUtc = CalculateUtcFromReference(nowUtc, refTz, minuteOfDay);
        var refLocal = TimeZoneInfo.ConvertTime(baseUtc, refTz);
        var refDate = refLocal.Date;

        var results = new List<ZoneConversionResult>();
        foreach (var city in cities)
        {
            var tz = ResolveTimeZone(city.Zone);
            var local = TimeZoneInfo.ConvertTime(baseUtc, tz);
            var dayOffset = (local.Date - refDate).Days;
            var category = CategorizeTime(local.Hour, local.Minute);
            var offsetStr = FormatOffset(tz.GetUtcOffset(local));
            var abbr = GetAbbreviation(tz, local);
            var isRef = string.Equals(city.Zone, referenceZoneId, StringComparison.OrdinalIgnoreCase);

            results.Add(new ZoneConversionResult(
                City: city,
                LocalTime: local,
                DayOffset: dayOffset,
                SlotCategory: category,
                FormattedTime: local.ToString("HH:mm", CultureInfo.InvariantCulture),
                OffsetDisplay: offsetStr,
                Abbreviation: abbr,
                IsReference: isRef));
        }

        // Evaluate overall feasibility
        var inconvenient = new List<string>();
        bool hasShoulder = false;

        foreach (var r in results)
        {
            if (r.SlotCategory == TimeSlotCategory.Night)
                inconvenient.Add(r.City.City);
            else if (r.SlotCategory == TimeSlotCategory.Shoulder)
                hasShoulder = true;
        }

        MeetingFeasibility feasibility;
        string summary;

        if (inconvenient.Count > 0)
        {
            feasibility = MeetingFeasibility.Inconvenient;
            var names = string.Join(", ", inconvenient);
            summary = $"Horário Inconveniente (Madrugada em {names})";
        }
        else if (hasShoulder)
        {
            feasibility = MeetingFeasibility.Acceptable;
            summary = "Horário Aceitável para Reunião";
        }
        else
        {
            feasibility = MeetingFeasibility.Ideal;
            summary = "Horário Ideal para Reunião (Comercial)";
        }

        return new MeetingEvaluation(feasibility, summary, inconvenient, results);
    }

    /// <summary>
    /// Formats a complete, professional meeting proposal text for email/chat export.
    /// </summary>
    public static string FormatProposalText(MeetingEvaluation evaluation, string referenceCity)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("📅 Proposta de Reunião Internacional");

        var refResult = evaluation.Results.FirstOrDefault(r => r.IsReference)
            ?? evaluation.Results.FirstOrDefault();

        if (refResult != null)
        {
            sb.AppendLine($"Horário Base: {refResult.City.City} - {refResult.FormattedTime} ({refResult.Abbreviation}, {refResult.OffsetDisplay})");
        }
        sb.AppendLine();
        sb.AppendLine("Horários Locais:");

        foreach (var r in evaluation.Results)
        {
            var dayTag = r.DayOffset switch
            {
                > 0 => $" +{r.DayOffset}d",
                < 0 => $" {r.DayOffset}d",
                _ => ""
            };

            var catLabel = r.SlotCategory switch
            {
                TimeSlotCategory.Business => "Comercial",
                TimeSlotCategory.Shoulder => "Estendido",
                TimeSlotCategory.Night => "Madrugada",
                _ => "Normal"
            };

            sb.AppendLine($"• {r.City.City}: {r.FormattedTime} ({r.Abbreviation}, {r.OffsetDisplay}) [{catLabel}{dayTag}]");
        }

        sb.AppendLine();
        sb.AppendLine($"Status: {evaluation.Summary}");

        return sb.ToString().TrimEnd();
    }
}
