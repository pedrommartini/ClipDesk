namespace ClipDesk.PluginSdk;

/// <summary>Conventional IDs; hosts may register additional namespaced capabilities.</summary>
public static class PluginHostCapabilityIds
{
    public const string Http = "host.http";
    public const string FileRead = "host.file-read";
    public const string FileWrite = "host.file-write";
    public const string Storage = "host.storage";
    public const string SharedAssets = "host.shared-assets";
    public const string AudioInput = "host.audio-input";
    public const string AudioOutput = "host.audio-output";
    public const string ImageProcessing = "host.image-processing";
    public const string SystemAudio = "host.system-audio";
    public const string ClipboardRead = "host.clipboard-read";
    public const string ClipboardWrite = "host.clipboard-write";
    public const string Notifications = "host.notifications";
    public const string BackgroundTasks = "host.background-tasks";

    public static string? RequiredPermission(string? id) => id?.ToLowerInvariant() switch
    {
        Http => PluginPermissions.Network,
        FileRead => PluginPermissions.FileRead,
        FileWrite => PluginPermissions.FileWrite,
        SharedAssets => PluginPermissions.SharedAssets,
        AudioInput => PluginPermissions.Microphone,
        SystemAudio => PluginPermissions.SystemAudio,
        ClipboardRead => PluginPermissions.ClipboardRead,
        ClipboardWrite => PluginPermissions.ClipboardWrite,
        Notifications => PluginPermissions.Notifications,
        _ => null
    };

    public static Type? ServiceType(string? id) => id?.ToLowerInvariant() switch
    {
        Http => typeof(IPluginHttp),
        FileRead => typeof(IPluginFileRead),
        FileWrite => typeof(IPluginFileWrite),
        Storage => typeof(IPluginStorage),
        SharedAssets => typeof(IPluginSharedAssets),
        AudioInput => typeof(IPluginAudioInput),
        AudioOutput => typeof(IPluginAudioOutput),
        SystemAudio => typeof(IPluginAudioInput),
        ImageProcessing => typeof(IPluginImageProcessing),
        ClipboardRead => typeof(IPluginClipboardRead),
        ClipboardWrite => typeof(IPluginClipboardWrite),
        Notifications => typeof(IPluginNotifications),
        BackgroundTasks => typeof(IPluginBackgroundTasks),
        _ => null
    };
}

/// <summary>Opaque file handle. A web or mobile host need not expose filesystem paths.</summary>
public interface IPluginReadableFile : IAsyncDisposable
{
    string DisplayName { get; }
    string? ContentType { get; }
    ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default);
}

public interface IPluginWritableFile : IAsyncDisposable
{
    string DisplayName { get; }
    string? ContentType { get; }
    ValueTask<Stream> OpenWriteAsync(CancellationToken cancellationToken = default);
}

public interface IPluginFileRead
{
    ValueTask<IReadOnlyList<IPluginReadableFile>> PickOpenAsync(bool multiple, CancellationToken cancellationToken = default);
}

public interface IPluginFileWrite
{
    ValueTask<IPluginWritableFile?> PickSaveAsync(string suggestedName, CancellationToken cancellationToken = default);
    ValueTask<IPluginWritableFile> CreateTemporaryAsync(string suggestedName, CancellationToken cancellationToken = default);
}

public interface IPluginStorage
{
    ValueTask<string?> ReadAsync(string key, CancellationToken cancellationToken = default);
    ValueTask WriteAsync(string key, string? value, CancellationToken cancellationToken = default);
}

/// <summary>A durable file reference scoped to one shared plugin instance; never contains a device path.</summary>
public sealed record PluginSharedAsset(string Id, string Name, long Size, string Sha256);

/// <summary>Imports selected/generated files into the instance and opens an authorized local copy.
/// Receiving an asset does not rerun the command which produced it.</summary>
public interface IPluginSharedAssets
{
    ValueTask<PluginSharedAsset> ImportAsync(IPluginReadableFile source, CancellationToken cancellationToken = default);
    ValueTask<IPluginReadableFile?> OpenAsync(string assetId, CancellationToken cancellationToken = default);
}

public sealed record PluginHttpRequest(Uri Uri, string Method = "GET", string? ContentType = null, byte[]? Body = null);
public sealed record PluginHttpResponse(int StatusCode, string? ContentType, byte[] Body);
public interface IPluginHttp
{
    ValueTask<PluginHttpResponse> SendAsync(PluginHttpRequest request, CancellationToken cancellationToken = default);
}

public sealed record PluginAudioFormat(int SampleRate, int Channels, string Encoding);
public sealed record PluginAudioChunk(byte[] Data, TimeSpan Timestamp);
public interface IPluginAudioInput
{
    IAsyncEnumerable<PluginAudioChunk> CaptureAsync(PluginAudioFormat format, CancellationToken cancellationToken = default);
}

public interface IPluginAudioOutput
{
    ValueTask PlayAsync(IAsyncEnumerable<PluginAudioChunk> audio, PluginAudioFormat format,
        CancellationToken cancellationToken = default);
}

public interface IPluginImageProcessing
{
    ValueTask TransformAsync(Stream source, Stream destination, string contentType,
        IReadOnlyDictionary<string, string> options, CancellationToken cancellationToken = default);
}

public interface IPluginClipboardRead
{
    ValueTask<string?> ReadTextAsync(CancellationToken cancellationToken = default);
}

public interface IPluginClipboardWrite
{
    ValueTask WriteTextAsync(string text, CancellationToken cancellationToken = default);
}

public interface IPluginNotifications
{
    ValueTask ShowAsync(string title, string message, CancellationToken cancellationToken = default);
}

public interface IPluginBackgroundTasks
{
    ValueTask RunAsync(Func<CancellationToken, ValueTask> operation, CancellationToken cancellationToken = default);
}
