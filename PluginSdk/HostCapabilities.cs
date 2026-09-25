namespace ClipDesk.PluginSdk;

/// <summary>Conventional IDs; hosts may register additional namespaced capabilities.</summary>
public static class PluginHostCapabilityIds
{
    public const string Http = "host.http";
    public const string FileRead = "host.file-read";
    public const string FileWrite = "host.file-write";
    public const string Storage = "host.storage";
    public const string AudioInput = "host.audio-input";
    public const string AudioOutput = "host.audio-output";
    public const string ImageProcessing = "host.image-processing";
    public const string SystemAudio = "host.system-audio";
    public const string Clipboard = "host.clipboard";
    public const string Notifications = "host.notifications";
    public const string BackgroundTasks = "host.background-tasks";
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

public interface IPluginClipboard
{
    ValueTask<string?> ReadTextAsync(CancellationToken cancellationToken = default);
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
