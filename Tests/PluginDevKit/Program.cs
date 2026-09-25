using ClipDesk.PluginSdk;
using ClipDesk.Plugin.AudioNotes;
using ClipDesk.Plugin.ImageCompressor;
using ClipDesk.Plugin.Calculator;
using ClipDesk.Plugin.Checklist;
using ClipDesk.Plugin.Translator;
using ClipDesk.Plugin.CurrencyConverter;

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
var higherRequirement = new PluginManifest
{
    Permissions = [PluginPermissions.Microphone],
    Requirements = [new PluginCapabilityRequirement
    {
        Id = PluginHostCapabilityIds.AudioInput, MinimumVersion = "1.1.0", Permission = PluginPermissions.Microphone
    }]
};
Check(new ManifestBoundCapabilityProvider(higherRequirement, granted)
    .Inspect(PluginHostCapabilityIds.AudioInput, new(1, 0, 0)) == PluginCapabilityStatus.VersionUnsupported,
    "manifest capability minimum cannot be bypassed");
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
await using (var faulted = new PluginModuleSession(manifest, new ThrowingModule(), granted, new InlineScheduler()))
{
    await faulted.ActivateAsync();
    Check((await faulted.ExecuteAsync(new PluginCommand("record"))).Status == PluginCommandStatus.Failed,
        "module exception is contained");
}
var registry = new PluginHostRegistry<string>(new(3, 0, 0), new Version(1, 0));
registry.RegisterModule("clipdesk.audio-notes.module", () => new AudioNotesModule());
registry.RegisterRenderer("clipdesk.audio-notes.windows", () => new FakeRenderer());
var viewport = new PluginViewport(320, 260, 1, 1, PluginOrientation.Landscape, PluginInputMode.Mixed);
await using (var hosted = registry.Create(manifest, PluginPlatforms.Windows, granted, new InlineScheduler(), viewport))
{
    Check(await hosted.ActivateAsync() == "audio-view", "static renderer registration");
    Check((await hosted.ExecuteAsync(new PluginCommand("record"))).Succeeded, "renderer dispatch");
    hosted.UpdateViewport(viewport with { Width = 180, Orientation = PluginOrientation.Portrait, InputMode = PluginInputMode.Touch });
    Check(hosted.State.GetString("lastRecording") == "audio-note.pcm", "responsive state preservation");
}
try
{
    registry.Create(manifest, PluginPlatforms.Web, granted, new InlineScheduler(), viewport);
    throw new Exception("Missing web renderer accepted.");
}
catch (InvalidOperationException) { }
Check(PluginCompatibility.Supports(manifest, new Version(1, 0), new(3, 0, 0), PluginPlatforms.Windows, granted), "contract 3.0");
var newerManifest = System.Text.Json.JsonSerializer.Deserialize<PluginManifest>(
    manifestJson.Replace("\"contractVersion\": \"3.0.0\"", "\"contractVersion\": \"3.1.0\""),
    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
Check(!PluginCompatibility.Supports(newerManifest, new Version(1, 0), new(3, 0, 0), PluginPlatforms.Windows, granted), "minor version negotiation");
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
var emptyCapabilities = new PluginCapabilityRegistry([], []);
var calculator = (IClipDeskPluginModuleV3)new CalculatorModule();
var calculatorResult = await calculator.ExecuteAsync(calculator.CreateDefaultState(), new PluginCommand("press",
    new Dictionary<string, string> { ["key"] = "7" }), emptyCapabilities);
Check(calculatorResult.State.GetString("display") == "7", "calculator v3");
var checklist = (IClipDeskPluginModuleV3)new ChecklistModule();
var checklistResult = await checklist.ExecuteAsync(checklist.CreateDefaultState(), new PluginCommand("add",
    new Dictionary<string, string> { ["text"] = "Portátil" }), emptyCapabilities);
Check(ChecklistModule.ReadItems(checklistResult.State).Count == 4, "checklist v3");
var networkCapabilities = new PluginCapabilityRegistry([PluginPermissions.Network], [PluginPermissions.Network]);
networkCapabilities.Register(new PluginCapabilityDescriptor(PluginHostCapabilityIds.Http, new(1, 0, 0), PluginPermissions.Network), new FakeHttp());
var translator = (IClipDeskPluginModuleV3)new TranslatorModule();
var translatorState = translator.CreateDefaultState().With("input", "Hello");
var translated = await translator.ExecuteAsync(translatorState, new PluginCommand("translate"), networkCapabilities);
Check(translated.Succeeded && translated.State.GetString("output") == "Ol&á", "translator v3");
var currency = (IClipDeskPluginModuleV3)new CurrencyConverterModule();
var converted = await currency.ExecuteAsync(currency.CreateDefaultState().With("amount", "10"),
    new PluginCommand("convert"), networkCapabilities);
Check(converted.Succeeded && converted.State.GetString("result") == "2,00 USD", "currency v3");
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

sealed class FakeRenderer : IPluginRendererAdapter<string>
{
    public string CreateView(PluginViewport viewport, PluginState state,
        Func<PluginCommand, CancellationToken, ValueTask<PluginCommandResult>> dispatch) => "audio-view";
    public void Update(PluginViewport viewport, PluginState state) { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

sealed class ThrowingModule : IClipDeskPluginModuleV3
{
    public string Id => "clipdesk.audio-notes";
    public int StateVersion => 1;
    public PluginState CreateDefaultState() => new();
    public PluginState NormalizeState(PluginState state) => state;
    public ValueTask<PluginCommandResult> ExecuteAsync(PluginState state, PluginCommand command,
        IPluginCapabilityProvider capabilities, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("boom");
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

sealed class FakeHttp : IPluginHttp
{
    public ValueTask<PluginHttpResponse> SendAsync(PluginHttpRequest request, CancellationToken cancellationToken = default)
    {
        var body = request.Uri.Host == "api.frankfurter.dev"
            ? "[{\"date\":\"2026-01-01\",\"base\":\"BRL\",\"quote\":\"USD\",\"rate\":0.2}]"
            : "{\"responseData\":{\"translatedText\":\"Ol&amp;á\"}}";
        return ValueTask.FromResult(new PluginHttpResponse(200, "application/json", System.Text.Encoding.UTF8.GetBytes(body)));
    }
}

sealed class FakeFile(string name) : IPluginWritableFile
{
    public string DisplayName => name;
    public string? ContentType => "audio/pcm";
    public ValueTask<Stream> OpenWriteAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<Stream>(new MemoryStream());
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
