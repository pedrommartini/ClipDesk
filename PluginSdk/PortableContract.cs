using System.Globalization;
using System.Text.RegularExpressions;

namespace ClipDesk.PluginSdk;

/// <summary>Contract and capability versions use stable MAJOR.MINOR.PATCH numbers.</summary>
public readonly record struct PluginSemanticVersion(int Major, int Minor, int Patch) : IComparable<PluginSemanticVersion>
{
    private static readonly Regex Pattern = new(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", RegexOptions.Compiled);

    public static bool TryParse(string? text, out PluginSemanticVersion version)
    {
        version = default;
        var match = Pattern.Match(text ?? "");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch)) return false;
        version = new PluginSemanticVersion(major, minor, patch);
        return true;
    }

    public int CompareTo(PluginSemanticVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0) return major;
        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

public sealed class PluginCapabilityRequirement
{
    public string Id { get; init; } = "";
    public string MinimumVersion { get; init; } = "1.0.0";
    public string? Permission { get; init; }
    public bool Optional { get; init; }
}

public sealed class PluginPlatformExtension
{
    public string Platform { get; init; } = "";
    public string Capability { get; init; } = "";
    public string? Permission { get; init; }
    public string? FallbackCapability { get; init; }
}

/// <summary>Logical layout hints. The renderer chooses composition and appearance.</summary>
public sealed class PluginLayoutSpec
{
    public PluginSize? Minimum { get; init; }
    public PluginSize? Preferred { get; init; }
    public PluginSize? Maximum { get; init; }
    public double? CompactBelowWidth { get; init; }
    public double? CompactBelowHeight { get; init; }
    public bool AllowScroll { get; init; } = true;
}

public enum PluginInputMode { Pointer, Touch, Keyboard, Mixed }
public enum PluginOrientation { Unknown, Portrait, Landscape }

public sealed record PluginViewport(double Width, double Height, double Density, double TextScale,
    PluginOrientation Orientation, PluginInputMode InputMode);

/// <summary>Portable behavior entry. A host can instantiate it through static registration.</summary>
public interface IClipDeskPluginModuleV3
{
    string Id { get; }
    int StateVersion { get; }
    PluginState CreateDefaultState();
    PluginState NormalizeState(PluginState state);
    ValueTask<PluginCommandResult> ExecuteAsync(PluginState state, PluginCommand command,
        IPluginCapabilityProvider capabilities, CancellationToken cancellationToken = default);
}

public interface IClipDeskPluginLifecycle
{
    ValueTask InitializeAsync(IPluginCapabilityProvider capabilities, CancellationToken cancellationToken = default);
    ValueTask ActivateAsync(CancellationToken cancellationToken = default);
    ValueTask SuspendAsync(CancellationToken cancellationToken = default);
    ValueTask ResumeAsync(CancellationToken cancellationToken = default);
    ValueTask<PluginState> SaveStateAsync(CancellationToken cancellationToken = default);
    ValueTask DisposeAsync(CancellationToken cancellationToken = default);
}

public sealed record PluginCapabilityDescriptor(string Id, PluginSemanticVersion Version, string? Permission = null);
public enum PluginCapabilityStatus { Available, Unavailable, VersionUnsupported, PermissionNotDeclared, PermissionDenied, TypeMismatch }
public sealed record PluginCapabilityResult<T>(PluginCapabilityStatus Status, T? Value) where T : class
{
    public bool Available => Status == PluginCapabilityStatus.Available && Value is not null;
}

public interface IPluginCapabilityProvider
{
    IReadOnlyList<PluginCapabilityDescriptor> Available { get; }
    PluginCapabilityStatus Inspect(string id, PluginSemanticVersion minimumVersion);
    PluginCapabilityResult<T> Resolve<T>(string id, PluginSemanticVersion minimumVersion) where T : class;
}

/// <summary>Host-owned registry. The plugin sees only IPluginCapabilityProvider.</summary>
public sealed class PluginCapabilityRegistry : IPluginCapabilityProvider
{
    private readonly Dictionary<string, (PluginCapabilityDescriptor Descriptor, object Service)> _services = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _declared;
    private readonly HashSet<string> _granted;

    public PluginCapabilityRegistry(IEnumerable<string> declaredPermissions, IEnumerable<string> grantedPermissions)
    {
        _declared = new HashSet<string>(declaredPermissions, StringComparer.OrdinalIgnoreCase);
        _granted = new HashSet<string>(grantedPermissions, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<PluginCapabilityDescriptor> Available => _services.Values.Select(x => x.Descriptor).ToArray();

    public void Register<T>(PluginCapabilityDescriptor descriptor, T service) where T : class
    {
        if (string.IsNullOrWhiteSpace(descriptor.Id)) throw new ArgumentException("Capability ID is required.", nameof(descriptor));
        _services.Add(descriptor.Id, (descriptor, service));
    }

    public PluginCapabilityResult<T> Resolve<T>(string id, PluginSemanticVersion minimumVersion) where T : class
    {
        var status = Inspect(id, minimumVersion);
        if (status != PluginCapabilityStatus.Available) return new(status, null);
        var entry = _services[id];
        return entry.Service is T service
            ? new(PluginCapabilityStatus.Available, service)
            : new(PluginCapabilityStatus.TypeMismatch, null);
    }

    public PluginCapabilityStatus Inspect(string id, PluginSemanticVersion minimumVersion)
    {
        if (!_services.TryGetValue(id, out var entry)) return PluginCapabilityStatus.Unavailable;
        if (entry.Descriptor.Version.Major != minimumVersion.Major
            || entry.Descriptor.Version.CompareTo(minimumVersion) < 0)
            return PluginCapabilityStatus.VersionUnsupported;
        if (entry.Descriptor.Permission is { } permission)
        {
            if (!_declared.Contains(permission)) return PluginCapabilityStatus.PermissionNotDeclared;
            if (!_granted.Contains(permission)) return PluginCapabilityStatus.PermissionDenied;
        }
        return PluginCapabilityStatus.Available;
    }
}

public sealed record PluginCapabilityAvailability(string Id, bool Optional, PluginCapabilityStatus Status);

public static class PluginCapabilityNegotiation
{
    public static IReadOnlyList<PluginCapabilityAvailability> Evaluate(PluginManifest manifest, IPluginCapabilityProvider provider) =>
        (manifest.Requirements ?? []).Select(requirement => new PluginCapabilityAvailability(
            requirement.Id,
            requirement.Optional,
            PluginSemanticVersion.TryParse(requirement.MinimumVersion, out var version)
                ? provider.Inspect(requirement.Id, version)
                : PluginCapabilityStatus.VersionUnsupported)).ToArray();

    public static bool CanActivate(PluginManifest manifest, IPluginCapabilityProvider provider) =>
        Evaluate(manifest, provider).All(item => item.Optional || item.Status == PluginCapabilityStatus.Available);
}

/// <summary>Ensures a module can only resolve capabilities declared by its manifest.</summary>
public sealed class ManifestBoundCapabilityProvider : IPluginCapabilityProvider
{
    private readonly PluginManifest _manifest;
    private readonly IPluginCapabilityProvider _host;

    public ManifestBoundCapabilityProvider(PluginManifest manifest, IPluginCapabilityProvider host)
    {
        _manifest = manifest;
        _host = host;
    }

    public IReadOnlyList<PluginCapabilityDescriptor> Available => _host.Available
        .Where(x => (_manifest.Requirements ?? []).Any(y => y.Id.Equals(x.Id, StringComparison.OrdinalIgnoreCase)))
        .ToArray();

    public PluginCapabilityStatus Inspect(string id, PluginSemanticVersion minimumVersion)
    {
        var requirement = (_manifest.Requirements ?? []).FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (requirement is null) return PluginCapabilityStatus.PermissionNotDeclared;
        var descriptor = _host.Available.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (descriptor?.Permission is { } permission
            && (!( _manifest.Permissions ?? []).Contains(permission, StringComparer.OrdinalIgnoreCase)
                || !string.Equals(requirement.Permission, permission, StringComparison.OrdinalIgnoreCase)))
            return PluginCapabilityStatus.PermissionNotDeclared;
        return _host.Inspect(id, minimumVersion);
    }

    public PluginCapabilityResult<T> Resolve<T>(string id, PluginSemanticVersion minimumVersion) where T : class
    {
        var status = Inspect(id, minimumVersion);
        return status == PluginCapabilityStatus.Available
            ? _host.Resolve<T>(id, minimumVersion)
            : new(status, null);
    }
}
