namespace ClipDesk.PluginSdk;

/// <summary>
/// Stable, platform-neutral metadata shared by ClipDesk and independently
/// distributed plugin packages. UI entry points remain platform specific.
/// </summary>
public sealed class PluginManifest
{
    public int ManifestVersion { get; init; } = 1;
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Version { get; init; } = "1.0.0";
    public string Publisher { get; init; } = "ClipDesk";
    public string IconGlyph { get; init; } = "\uE87B";
    public string AccentColor { get; init; } = "#9B7DFF";
    public string Runtime { get; init; } = "legacy-wpf";
    public string? EntryAssembly { get; init; }
    public string? EntryType { get; init; }
    public int PluginApiVersion { get; init; }
    public string MinimumHostVersion { get; init; } = "0.3.3";
    public string? MaximumHostVersion { get; init; }
    public bool InstallByDefault { get; init; }
    public int SortOrder { get; init; }
    public PluginSize DefaultSize { get; init; } = new();
    /// <summary>Smallest usable compact layout, in board coordinates.</summary>
    public PluginSize MinimumSize { get; init; } = new() { Width = 160, Height = 160 };
    public Dictionary<string, string> DefaultContent { get; init; } = [];
    public IReadOnlyList<string> Platforms { get; init; } = ["windows"];
    public IReadOnlyList<string> Permissions { get; init; } = [];
    public PluginEntryPoint? Module { get; init; }
    public IReadOnlyList<PluginRendererEntryPoint> Renderers { get; init; } = [];
    public int StateVersion { get; init; } = 1;
    public IReadOnlyList<string> Capabilities { get; init; } = [PluginCapabilities.BoardWidget];
    public IReadOnlyList<string> NetworkHosts { get; init; } = [];
    /// <summary>SemVer of the portable contract. Required for manifest v3.</summary>
    public string? ContractVersion { get; init; }
    public IReadOnlyList<PluginCapabilityRequirement> Requirements { get; init; } = [];
    public PluginLayoutSpec? Layout { get; init; }
    public IReadOnlyList<PluginPlatformExtension> PlatformExtensions { get; init; } = [];

    public PluginRendererEntryPoint? RendererFor(string platform) =>
        (Renderers ?? []).FirstOrDefault(renderer => renderer is not null
            && string.Equals(renderer.Platform, platform, StringComparison.OrdinalIgnoreCase));
}

public class PluginEntryPoint
{
    public string Assembly { get; init; } = "";
    public string Type { get; init; } = "";
    /// <summary>Static host registration ID for hosts that cannot load assemblies dynamically.</summary>
    public string? Registration { get; init; }
}

public sealed class PluginRendererEntryPoint : PluginEntryPoint
{
    public string Platform { get; init; } = "";
    public string Runtime { get; init; } = "";
}

public static class PluginPlatforms
{
    public const string Windows = "windows";
    public const string Android = "android";
    public const string Web = "web";
    public const string Ios = "ios";
}

public static class PluginRuntimes
{
    public const string LegacyWpf = "legacy-wpf";
    public const string WpfV1 = "wpf-v1";
    public const string PortableV2 = "portable-v2";
    public const string WpfV2 = "wpf-v2";
    public const string MauiV1 = "maui-v1";
    public const string PortableV3 = "portable-v3";
}

public static class PluginPermissions
{
    public const string Network = "network";
    public const string ClipboardRead = "clipboard.read";
    public const string ClipboardWrite = "clipboard.write";
    public const string FileRead = "file.read";
    public const string FileWrite = "file.write";
    public const string Microphone = "microphone";
    public const string Camera = "camera";
    public const string SystemAudio = "system-audio";
    public const string Notifications = "notifications";
}

public static class PluginCapabilities
{
    public const string BoardWidget = "board-widget";
    public const string AddFilesToBoard = "board-files";
    public const string ReceiveFileDrops = "file-drop";
    public const string Command = "command";
    public const string BackgroundRefresh = "background-refresh";
}

public static class PluginCompatibility
{
    public const int LegacyWindowsApiVersion = 1;
    public const int PortableApiVersion = 2;
    public const int WindowsApiVersion = 2;
    public const int CurrentPortableApiVersion = 3;

    public static bool Supports(PluginManifest manifest, Version hostVersion,
        PluginSemanticVersion hostContractVersion, string platform, IPluginCapabilityProvider capabilities)
    {
        if (manifest.ManifestVersion != 3
            || !PluginSemanticVersion.TryParse(manifest.ContractVersion, out var required)
            || required.Major != hostContractVersion.Major
            || required.CompareTo(hostContractVersion) > 0
            || !Supports(manifest, hostVersion, CurrentPortableApiVersion, platform)) return false;
        return PluginCapabilityNegotiation.CanActivate(manifest,
            new ManifestBoundCapabilityProvider(manifest, capabilities));
    }

    public static bool Supports(PluginManifest manifest, Version hostVersion, int apiVersion, string platform)
    {
        if (manifest.ManifestVersion is < 1 or > 3 || manifest.PluginApiVersion > apiVersion
            || manifest.PluginApiVersion < 0 || manifest.Platforms is null
            || !manifest.Platforms.Contains(platform, StringComparer.OrdinalIgnoreCase)
            || !Version.TryParse(manifest.Version, out _)
            || !Version.TryParse(manifest.MinimumHostVersion, out var minimum)
            || hostVersion < minimum) return false;

        if (manifest.MaximumHostVersion is { Length: > 0 } maximumText
            && (!Version.TryParse(maximumText, out var maximum) || hostVersion > maximum)) return false;

        if (manifest.ManifestVersion == 1)
        {
            return manifest.Runtime switch
            {
                PluginRuntimes.LegacyWpf => platform.Equals(PluginPlatforms.Windows, StringComparison.OrdinalIgnoreCase),
                PluginRuntimes.WpfV1 => platform.Equals(PluginPlatforms.Windows, StringComparison.OrdinalIgnoreCase)
                && manifest.PluginApiVersion == LegacyWindowsApiVersion
                && !string.IsNullOrWhiteSpace(manifest.EntryAssembly)
                && !string.IsNullOrWhiteSpace(manifest.EntryType),
                _ => false
            };
        }

        if (manifest.ManifestVersion == 3)
        {
            if (manifest.PluginApiVersion != CurrentPortableApiVersion
                || manifest.Runtime != PluginRuntimes.PortableV3
                || !PluginSemanticVersion.TryParse(manifest.ContractVersion, out var contract)
                || contract.Major != 3 || manifest.StateVersion < 1
                || !IsValidPortableEntry(manifest.Module)
                || PluginManifestValidator.Validate(manifest).Count != 0) return false;
            var entry = manifest.RendererFor(platform);
            return entry is not null && IsValidPortableEntry(entry);
        }

        if (manifest.PluginApiVersion != PortableApiVersion
            || manifest.Runtime != PluginRuntimes.PortableV2
            || manifest.StateVersion < 1
            || manifest.DefaultSize is null
            || manifest.MinimumSize is null
            || manifest.MinimumSize.Width < 120
            || Math.Abs(manifest.MinimumSize.Width - manifest.MinimumSize.Height) > .01
            || manifest.MinimumSize.Width > manifest.DefaultSize.Width
            || manifest.MinimumSize.Height > manifest.DefaultSize.Height
            || !IsValidEntryPoint(manifest.Module)) return false;

        var renderer = manifest.RendererFor(platform);
        if (renderer is null || !IsValidEntryPoint(renderer)) return false;

        return platform.ToLowerInvariant() switch
        {
            PluginPlatforms.Windows => renderer.Runtime == PluginRuntimes.WpfV2,
            PluginPlatforms.Android => renderer.Runtime == PluginRuntimes.MauiV1,
            _ => false
        };
    }

    private static bool IsValidEntryPoint(PluginEntryPoint? entryPoint) =>
        entryPoint is not null
        && !string.IsNullOrWhiteSpace(entryPoint.Assembly)
        && Path.GetFileName(entryPoint.Assembly).Equals(entryPoint.Assembly, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(entryPoint.Type);

    private static bool IsValidPortableEntry(PluginEntryPoint? entryPoint) =>
        entryPoint is not null &&
        (!string.IsNullOrWhiteSpace(entryPoint.Registration)
         || IsValidEntryPoint(entryPoint));
}

public sealed class PluginSize
{
    public double Width { get; init; } = 320;
    public double Height { get; init; } = 240;
}

public static class BuiltInPluginIds
{
    public const string Checklist = "clipdesk.checklist";
    public const string Calculator = "clipdesk.calculator";
    public const string Translator = "clipdesk.translator";
    public const string CurrencyConverter = "clipdesk.currency-converter";
}
