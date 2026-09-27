using System.Text.Json.Serialization;

namespace ClipDesk.Plugin.Timezone;

/// <summary>
/// Represents a city and its associated timezone identifier.
/// </summary>
public sealed record TimezoneCity(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("city")] string City,
    [property: JsonPropertyName("zone")] string Zone);

/// <summary>
/// Working hour classification for a specific point in local time.
/// </summary>
public enum TimeSlotCategory
{
    /// <summary>09:00 to 17:59: Standard business hours (ideal for collaboration).</summary>
    Business,

    /// <summary>07:00 to 08:59 or 18:00 to 21:59: Shoulder hours (acceptable if necessary).</summary>
    Shoulder,

    /// <summary>22:00 to 06:59: Night hours / sleeping hours (inconvenient for meetings).</summary>
    Night
}

/// <summary>
/// Overall feasibility evaluation of a meeting across all compared zones.
/// </summary>
public enum MeetingFeasibility
{
    /// <summary>All participant cities are in standard business hours.</summary>
    Ideal,

    /// <summary>At least one city is in shoulder hours, but none are in night hours.</summary>
    Acceptable,

    /// <summary>At least one city is in night/sleeping hours.</summary>
    Inconvenient
}

/// <summary>
/// Converted time result for a specific city at the reference point in time.
/// </summary>
public sealed record ZoneConversionResult(
    TimezoneCity City,
    DateTimeOffset LocalTime,
    int DayOffset,
    TimeSlotCategory SlotCategory,
    string FormattedTime,
    string OffsetDisplay,
    string Abbreviation,
    bool IsReference);

/// <summary>
/// Evaluation of meeting feasibility across all evaluated cities.
/// </summary>
public sealed record MeetingEvaluation(
    MeetingFeasibility Feasibility,
    string Summary,
    IReadOnlyList<string> InconvenientCities,
    IReadOnlyList<ZoneConversionResult> Results);
