using ClipDesk.PluginSdk;
using ClipDesk.Plugin.AudioNotes;
using ClipDesk.Plugin.ImageCompressor;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var manifestJson = File.ReadAllText(Path.Combine(root, "PluginPackages", "Examples", "AudioNotes", "manifest.json"));
Check(PluginManifestValidator.ValidateJson(manifestJson).Count == 0, "example manifest");
var manifest = System.Text.Json.JsonSerializer.Deserialize<PluginManifest>(manifestJson,
    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
foreach (var platform in new[] { PluginPlatforms.Windows, PluginPlatforms.Web, PluginPlatforms.Android, PluginPlatforms.Ios })
    Check(PluginCompatibility.Supports(manifest, new Version(1, 0), 3, platform), $"v3 {platform}");
Check(!PluginCompatibility.Supports(manifest, new Version(1, 0), 2, PluginPlatforms.Windows), "legacy host rejects v3");
Check(PluginManifestValidator.ValidateJson(manifestJson.Replace("\"minimumVersion\"", "\"minVersion\"")).Any(x => x.Code == "CDK001"), "unknown field");
Check(PluginManifestValidator.ValidateJson(manifestJson.Replace("\"microphone\", \"file.write\"", "\"file.write\"")).Any(x => x.Code == "CDK015"), "permission declaration");

var denied = new PluginCapabilityRegistry([], []);
denied.Register(new PluginCapabilityDescriptor(PluginHostCapabilityIds.AudioInput, new(1, 0, 0), PluginPermissions.Microphone), new FakeAudio());
Check(denied.Resolve<IPluginAudioInput>(PluginHostCapabilityIds.AudioInput, new(1, 0, 0)).Status == PluginCapabilityStatus.PermissionNotDeclared, "undeclared permission");
var declared = new PluginCapabilityRegistry([PluginPermissions.Microphone], []);
declared.Register(new PluginCapabilityDescriptor(PluginHostCapabilityIds.AudioInput, new(1, 0, 0), PluginPermissions.Microphone), new FakeAudio());
Check(declared.Resolve<IPluginAudioInput>(PluginHostCapabilityIds.AudioInput, new(1, 0, 0)).Status == PluginCapabilityStatus.PermissionDenied, "denied permission");
var granted = new PluginCapabilityRegistry([PluginPermissions.Microphone, PluginPermissions.FileWrite], [PluginPermissions.Microphone, PluginPermissions.FileWrite]);
granted.Register(new PluginCapabilityDescriptor(PluginHostCapabilityIds.AudioInput, new(1, 0, 0), PluginPermissions.Microphone), new FakeAudio());
granted.Register(new PluginCapabilityDescriptor(PluginHostCapabilityIds.FileWrite, new(1, 0, 0), PluginPermissions.FileWrite), new FakeFiles());
Check(granted.Resolve<IPluginAudioInput>(PluginHostCapabilityIds.AudioInput, new(2, 0, 0)).Status == PluginCapabilityStatus.VersionUnsupported, "major version");
Check(PluginCapabilityNegotiation.CanActivate(manifest, denied), "optional capability fallback");
Check(new ManifestBoundCapabilityProvider(manifest, granted).Inspect(PluginHostCapabilityIds.Http, new(1, 0, 0))
    == PluginCapabilityStatus.PermissionNotDeclared, "undeclared capability");
var result = await new AudioNotesModule().ExecuteAsync(new PluginState(), new PluginCommand("record"), granted);
Check(result.Succeeded && result.State.GetString("lastRecording") == "audio-note.pcm" && result.Data?["bytes"] == "3", "advanced command");
await using (var session = new PluginModuleSession(manifest, new AudioNotesModule(), granted, new InlineScheduler()))
{
    Check((await session.ExecuteAsync(new PluginCommand("record"))).Status == PluginCommandStatus.Unavailable, "inactive lifecycle");
    await session.ActivateAsync();
    Check((await session.ExecuteAsync(new PluginCommand("record"))).Succeeded, "session command");
    await session.SuspendAsync();
    Check(session.Status == PluginSessionStatus.Suspended && session.State.GetString("lastRecording") == "audio-note.pcm", "suspend and state");
    await session.ActivateAsync();
    Check(session.Status == PluginSessionStatus.Active, "resume");
}
var unavailable = await new AudioNotesModule().ExecuteAsync(new PluginState(), new PluginCommand("record"), denied);
Check(unavailable.Status == PluginCommandStatus.Unavailable, "graceful degradation");
var imageManifestJson = File.ReadAllText(Path.Combine(root, "PluginPackages", "Examples", "ImageCompressor", "manifest.json"));
Check(PluginManifestValidator.ValidateJson(imageManifestJson).Count == 0, "image manifest");
var imageCapabilities = new PluginCapabilityRegistry([PluginPermissions.FileRead, PluginPermissions.FileWrite],
    [PluginPermissions.FileRead, PluginPermissions.FileWrite]);
imageCapabilities.Register(new PluginCapabilityDescriptor(PluginHostCapabilityIds.FileRead, new(1, 0, 0), PluginPermissions.FileRead), new FakeFileRead());
imageCapabilities.Register(new PluginCapabilityDescriptor(PluginHostCapabilityIds.FileWrite, new(1, 0, 0), PluginPermissions.FileWrite), new FakeFiles());
imageCapabilities.Register(new PluginCapabilityDescriptor(PluginHostCapabilityIds.ImageProcessing, new(1, 0, 0)), new FakeImageProcessing());
var compressed = await new ImageCompressorModule().ExecuteAsync(new PluginState(), new PluginCommand("compress"), imageCapabilities);
Check(compressed.Succeeded && compressed.State.GetString("lastOutput") == "imagem-comprimida.webp", "image workflow");
Console.WriteLine("PASS: portable v3 manifest, capabilities, permissions, versions and audio/image/file flows.");

static void Check(bool condition, string label) { if (!condition) throw new Exception(label); }

sealed class FakeAudio : IPluginAudioInput
{
    public async IAsyncEnumerable<PluginAudioChunk> CaptureAsync(PluginAudioFormat format,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new PluginAudioChunk([1, 2, 3], TimeSpan.Zero);
        await Task.CompletedTask;
    }
}

sealed class InlineScheduler : IPluginExecutionScheduler
{
    public ValueTask<PluginCommandResult> RunAsync(Func<CancellationToken, ValueTask<PluginCommandResult>> operation,
        CancellationToken cancellationToken) => operation(cancellationToken);
}

sealed class FakeFiles : IPluginFileWrite
{
    public ValueTask<IPluginWritableFile?> PickSaveAsync(string suggestedName, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IPluginWritableFile?>(new FakeFile(suggestedName));
    public ValueTask<IPluginWritableFile> CreateTemporaryAsync(string suggestedName, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IPluginWritableFile>(new FakeFile(suggestedName));
}

sealed class FakeFileRead : IPluginFileRead
{
    public ValueTask<IReadOnlyList<IPluginReadableFile>> PickOpenAsync(bool multiple, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<IPluginReadableFile>>([new FakeReadableFile()]);
}

sealed class FakeReadableFile : IPluginReadableFile
{
    public string DisplayName => "image.png";
    public string? ContentType => "image/png";
    public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<Stream>(new MemoryStream([1, 2, 3]));
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

sealed class FakeImageProcessing : IPluginImageProcessing
{
    public ValueTask TransformAsync(Stream source, Stream destination, string contentType,
        IReadOnlyDictionary<string, string> options, CancellationToken cancellationToken = default) =>
        new(source.CopyToAsync(destination, cancellationToken));
}

sealed class FakeFile(string name) : IPluginWritableFile
{
    public string DisplayName => name;
    public string? ContentType => "audio/pcm";
    public ValueTask<Stream> OpenWriteAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<Stream>(new MemoryStream());
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
