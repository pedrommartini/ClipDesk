using System.IO;
using System.IO.Compression;
using System.Text.Json;
using ClipDesk.PluginSdk;
using ClipDesk.Services;

internal static class CandidatePackageChecks
{
    private static readonly string[] Ids =
    [
        "clipdesk.audiorecorder", "clipdesk.fileconverter", "clipdesk.imagecompressor",
        "clipdesk.imageupscaler", "clipdesk.tts"
    ];

    public static void Run(string installedRoot)
    {
        var catalog = new PluginCatalogService(Path.Combine(installedRoot, "no-bundled"), installedRoot,
            new Version(0, 4, 4), Path.Combine(installedRoot, "no-updates"));
        var loader = new WindowsPluginLoader(catalog);
        foreach (var id in Ids)
        {
            var manifest = loader.Manifest(id);
            if (manifest?.Version != "1.1.0")
                throw new Exception($"The candidate package did not activate: {id} ({loader.LastFailure(id)})");
            var body = loader.CreateBody(id, new PluginState(manifest.DefaultContent), true, false,
                manifest.DefaultSize?.Width ?? 360, manifest.DefaultSize?.Height ?? 300, 1,
                manifest.AccentColor ?? "#8B5CF6", () => { }, (_, _) => { }, _ => { });
            if (body is null || loader.LastFailure(id) is not null)
                throw new Exception($"The packaged renderer failed: {id} ({loader.LastFailure(id)})");
            (body as IDisposable)?.Dispose();
        }
        Console.WriteLine("PASS: five packaged plugin renderers load from isolated Windows installations.");
    }

    public static void RunOptionalArchives(string packageDirectory)
    {
        var ids = new[] { "clipdesk.clock", "clipdesk.stopwatch", "clipdesk.timer",
            "clipdesk.timezone", "clipdesk.qrcode" };
        var root = Path.Combine(AppEnvironment.DataRoot, "optional-package-load");
        var installedRoot = Path.Combine(root, "installed");
        var catalog = new PluginCatalogService(Path.Combine(root, "no-bundled"), installedRoot,
            new Version(0, 4, 4), Path.Combine(root, "no-updates"));
        foreach (var id in ids)
        {
            var archives = Directory.GetFiles(packageDirectory, id + "-*.zip");
            if (archives.Length != 1) throw new Exception($"Expected one candidate archive for {id}.");
            var extracted = Path.Combine(root, "extracted", id);
            ZipFile.ExtractToDirectory(archives[0], extracted);
            var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(Path.Combine(extracted, "manifest.json")),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            if (manifest.Id != id) throw new Exception($"Archive identity mismatch for {id}.");
            catalog.Install(manifest, extracted);
        }
        var loader = new WindowsPluginLoader(catalog);
        foreach (var id in ids)
        {
            var manifest = loader.Manifest(id) ?? throw new Exception($"Package did not activate: {id} ({loader.LastFailure(id)})");
            var body = loader.CreateBody(id, new PluginState(manifest.DefaultContent), true, false,
                manifest.DefaultSize?.Width ?? 360, manifest.DefaultSize?.Height ?? 300, 1,
                manifest.AccentColor ?? "#8B5CF6", () => { }, (_, _) => { }, _ => { });
            if (body is null || loader.LastFailure(id) is not null)
                throw new Exception($"Packaged renderer failed: {id} ({loader.LastFailure(id)})");
            (body as IDisposable)?.Dispose();
        }
        Console.WriteLine("PASS: clock, stopwatch, timer, timezone and QR package renderers load after isolated install.");
    }
}
