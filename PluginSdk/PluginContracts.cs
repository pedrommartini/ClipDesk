using System.Collections.ObjectModel;
using System.Globalization;

namespace ClipDesk.PluginSdk;

public static class PluginStateKeys
{
    public const string SchemaVersion = "$schema";
}

public static class PluginStyleKeys
{
    public const string AccentColor = "accent";
}

/// <summary>
/// Immutable state boundary shared by every ClipDesk host and renderer.
/// Values deliberately remain strings so state is durable, inspectable and
/// forward compatible across desktop and mobile installations.
/// </summary>
public sealed class PluginState
{
    private readonly IReadOnlyDictionary<string, string> _values;

    public PluginState(IReadOnlyDictionary<string, string>? values = null)
    {
        var copy = values?.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);
        _values = new ReadOnlyDictionary<string, string>(copy);
    }

    public IReadOnlyDictionary<string, string> Values => _values;
    public string? GetString(string key) => _values.TryGetValue(key, out var value) ? value : null;

    public int GetInt32(string key, int fallback = 0) =>
        int.TryParse(GetString(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    public decimal GetDecimal(string key, decimal fallback = 0) =>
        decimal.TryParse(GetString(key), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    public PluginState With(string key, string? value)
    {
        var next = ToDictionary();
        if (value is null) next.Remove(key);
        else next[key] = value;
        return new PluginState(next);
    }

    public Dictionary<string, string> ToDictionary() =>
        _values.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

    public bool ContentEquals(PluginState other) =>
        _values.Count == other._values.Count
        && _values.All(item => other._values.TryGetValue(item.Key, out var value) && value == item.Value);
}

public sealed record PluginCommand(
    string Name,
    IReadOnlyDictionary<string, string>? Arguments = null)
{
    public string? Argument(string key) =>
        Arguments is not null && Arguments.TryGetValue(key, out var value) ? value : null;
}

public enum PluginCommandStatus
{
    Success,
    ValidationError,
    PermissionDenied,
    Unavailable,
    Failed
}

public sealed record PluginCommandResult(
    PluginState State,
    PluginCommandStatus Status = PluginCommandStatus.Success,
    string? Message = null,
    IReadOnlyDictionary<string, string>? Data = null)
{
    public bool Succeeded => Status == PluginCommandStatus.Success;

    public static PluginCommandResult Invalid(PluginState state, string message) =>
        new(state, PluginCommandStatus.ValidationError, message);
}

public sealed record PluginOption(string Value, string Label);

public interface IPluginNetworkClient
{
    Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken = default);
}

public interface IPluginExecutionContext
{
    DateTimeOffset UtcNow { get; }
    IPluginNetworkClient Network { get; }
    bool HasPermission(string permission);
}

public sealed class PluginExecutionContext(
    IPluginNetworkClient network,
    IEnumerable<string>? permissions = null,
    Func<DateTimeOffset>? clock = null) : IPluginExecutionContext
{
    private readonly HashSet<string> _permissions = new(
        permissions ?? [], StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset UtcNow => (clock ?? (() => DateTimeOffset.UtcNow))();
    public IPluginNetworkClient Network { get; } = network;
    public bool HasPermission(string permission) => _permissions.Contains(permission);
}

/// <summary>
/// Portable plugin behavior. This assembly must not reference WPF, MAUI or
/// platform APIs. Renderers translate user interaction into named commands.
/// </summary>
public interface IClipDeskPluginModule
{
    string Id { get; }
    int StateVersion { get; }
    PluginState CreateDefaultState();
    PluginState NormalizeState(PluginState state);
    ValueTask<PluginCommandResult> ExecuteAsync(
        PluginState state,
        PluginCommand command,
        IPluginExecutionContext context,
        CancellationToken cancellationToken = default);
    string? GetClipboardText(PluginState state);
}
