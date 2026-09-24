using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ClipDesk.Core;
using ClipDesk.PluginSdk;
using ClipDesk.PluginSdk.Windows;
using ClipDesk.Services;
using ClipDesk.Views;

internal static class PluginDeliveryChecks
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static void Run(Application app)
    {
        var officialFeed = JsonSerializer.Deserialize<PluginFeed>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "production-feed.json")), Json)
            ?? throw new Exception("Production plugin feed could not be read.");
        var optional = PluginDeliveryService.AddRemoteEntries(officialFeed, [], Version.Parse(UpdateService.CurrentVersion));
        if (!new[] { "clipdesk.qrcode", "clipdesk.audiorecorder" }
                .All(id => optional.Any(entry => entry.Manifest.Id == id && entry.RemotePackage is not null)))
            throw new Exception("Production plugin feed hides an approved optional plugin.");
        var bundledFeed = PluginDeliveryService.LoadBundledFeed();
        if (bundledFeed is null || bundledFeed.Packages.Count != officialFeed.Packages.Count)
            throw new Exception("Production package does not contain the approved plugin feed.");

        var root = Path.Combine(Path.GetTempPath(), "ClipDesk-Plugin-Delivery-Checks", Guid.NewGuid().ToString("N"));
        var bundled = Path.Combine(root, "bundled", BuiltInPluginIds.Calculator);
        var installed = Path.Combine(root, "Documents", "Clipdesk");
        var privateUpdates = Path.Combine(root, "AppData", "Plugins");
        Environment.SetEnvironmentVariable("CLIPDESK_DEV_DATA_ROOT", root);
        Directory.CreateDirectory(bundled);
        try
        {
            var oldManifest = Manifest("1.0.0", "legacy-wpf", 0);
            File.WriteAllText(Path.Combine(bundled, "manifest.json"), JsonSerializer.Serialize(oldManifest, Json));
            var catalog = new PluginCatalogService(Path.GetDirectoryName(bundled), installed, new Version(0, 3, 3), privateUpdates);
            if (catalog.LoadCatalog().Single().InstalledVersion != "1.0.0") throw new Exception("Initial plugin install failed.");
            if (Directory.Exists(installed) || Directory.Exists(privateUpdates)
                || catalog.GetInstalledPackage(BuiltInPluginIds.Calculator)?.Directory != bundled)
                throw new Exception("A bundled plugin was copied out of the application directory.");
            var obsoleteCopy = Path.Combine(privateUpdates, BuiltInPluginIds.Calculator, "1.0.0");
            Directory.CreateDirectory(obsoleteCopy);
            File.Copy(Path.Combine(bundled, "manifest.json"), Path.Combine(obsoleteCopy, "manifest.json"));
            File.WriteAllText(Path.Combine(privateUpdates, BuiltInPluginIds.Calculator, "current-version.txt"), "1.0.0");
            if (catalog.GetInstalledPackage(BuiltInPluginIds.Calculator)?.Directory != bundled)
                throw new Exception("An old private copy displaced the bundled plugin.");
            if (new PluginCatalogService(Path.GetDirectoryName(bundled), hostVersion: new Version(0, 3, 3)).InstalledRoot
                != Path.Combine(root, "UserPlugins"))
                throw new Exception("The isolated DEV override did not redirect optional plugins.");
            var overrideRoot = Environment.GetEnvironmentVariable("CLIPDESK_DEV_DATA_ROOT");
            try
            {
                Environment.SetEnvironmentVariable("CLIPDESK_DEV_DATA_ROOT", null);
                var documentsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Clipdesk", "Dev");
                if (new PluginCatalogService(Path.GetDirectoryName(bundled), hostVersion: new Version(0, 3, 3)).InstalledRoot
                    != documentsRoot)
                    throw new Exception("The DEV optional-plugin path is not under Documents/Clipdesk.");
            }
            finally { Environment.SetEnvironmentVariable("CLIPDESK_DEV_DATA_ROOT", overrideRoot); }

            var source = Path.Combine(root, "source");
            Directory.CreateDirectory(source);
            var newManifest = Manifest("1.1.0", "wpf-v1", 1);
            File.WriteAllText(Path.Combine(source, "manifest.json"), JsonSerializer.Serialize(newManifest, Json));
            File.Copy(Path.Combine(AppContext.BaseDirectory, "ClipDesk.Plugin.Calculator.dll"),
                Path.Combine(source, "ClipDesk.Plugin.Calculator.dll"));
            var zip = Path.Combine(root, "calculator.zip");
            ZipFile.CreateFromDirectory(source, zip);
            var zipBytes = File.ReadAllBytes(zip);
            var hash = Convert.ToHexString(SHA256.HashData(zipBytes));
            var package = new PluginFeedPackage
            {
                Id = BuiltInPluginIds.Calculator, Name = "Calculadora", Description = "Plugin de teste.",
                Version = "1.1.0", Runtime = "wpf-v1", PluginApiVersion = 1,
                EntryAssembly = newManifest.EntryAssembly, EntryType = newManifest.EntryType,
                MinimumHostVersion = "0.3.3", Url = "https://example.test/calculator.zip", Sha256 = hash
            };
            var future = new PluginFeedPackage
            {
                Id = BuiltInPluginIds.Calculator, Version = "2.0.0", Runtime = "wpf-v1", PluginApiVersion = 2,
                EntryAssembly = newManifest.EntryAssembly, EntryType = newManifest.EntryType,
                MinimumHostVersion = "0.4.0", Url = "https://example.test/future.zip", Sha256 = hash
            };
            var feed = new PluginFeed { Packages = [package, future] };
            if (PluginDeliveryService.SelectUpdates(feed, catalog.LoadCatalog(), new Version(0, 3, 3)).Single().Version != "1.1.0")
                throw new Exception("Host compatibility did not select the correct plugin release.");

            var feedUri = new Uri("https://example.test/feed.json");
            var tampered = new PluginFeed { Packages = [new PluginFeedPackage
            {
                Id = package.Id, Version = package.Version, Runtime = package.Runtime,
                PluginApiVersion = package.PluginApiVersion, EntryAssembly = package.EntryAssembly,
                EntryType = package.EntryType, MinimumHostVersion = package.MinimumHostVersion,
                Url = package.Url, Sha256 = new string('0', 64)
            }] };
            using (var badHttp = new HttpClient(new PackageHandler(feedUri, JsonSerializer.SerializeToUtf8Bytes(tampered, Json), zipBytes)))
            {
                try
                {
                    new PluginDeliveryService(catalog, badHttp, feedUri).UpdateInstalledAsync().GetAwaiter().GetResult();
                    throw new Exception("A package with an invalid hash was installed.");
                }
                catch (InvalidDataException) { }
                if (catalog.GetInstalledPackage(BuiltInPluginIds.Calculator)?.Manifest.Version != "1.0.0")
                    throw new Exception("A rejected package changed the active version.");
            }
            var unsafeZip = Path.Combine(root, "unsafe.zip");
            using (var archive = ZipFile.Open(unsafeZip, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("../outside.txt").Open()))
                writer.Write("outside");
            var unsafeBytes = File.ReadAllBytes(unsafeZip);
            var unsafePackage = new PluginFeedPackage
            {
                Id = package.Id, Version = package.Version, Runtime = package.Runtime,
                PluginApiVersion = package.PluginApiVersion, EntryAssembly = package.EntryAssembly,
                EntryType = package.EntryType, MinimumHostVersion = package.MinimumHostVersion,
                Url = package.Url, Sha256 = Convert.ToHexString(SHA256.HashData(unsafeBytes))
            };
            using (var unsafeHttp = new HttpClient(new PackageHandler(feedUri, [], unsafeBytes)))
            {
                try
                {
                    new PluginDeliveryService(catalog, unsafeHttp, feedUri)
                        .InstallRemoteAsync(unsafePackage).GetAwaiter().GetResult();
                    throw new Exception("An archive containing a parent path was installed.");
                }
                catch (InvalidDataException ex) when (ex.Message.Contains("Caminho inválido")) { }
                if (catalog.GetInstalledPackage(BuiltInPluginIds.Calculator)?.Manifest.Version != "1.0.0")
                    throw new Exception("An unsafe archive changed the active version.");
            }
            using var http = new HttpClient(new PackageHandler(feedUri, JsonSerializer.SerializeToUtf8Bytes(feed, Json), zipBytes));
            var updated = new PluginDeliveryService(catalog, http, feedUri).UpdateInstalledAsync().GetAwaiter().GetResult();
            if (updated.Single().Manifest.Version != "1.1.0" || catalog.LoadCatalog().Single().Manifest.Version != "1.1.0")
                throw new Exception("Independent plugin update did not become active.");
            if (!updated.Single().Directory.StartsWith(privateUpdates + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || Directory.Exists(installed))
                throw new Exception("A bundled plugin update was installed in the user-selected plugins folder.");
            var storeEntry = PluginDeliveryService.AddRemoteEntries(feed, catalog.LoadCatalog(), new Version(0, 3, 3)).Single();
            if (!storeEntry.IsInstalled || storeEntry.RemotePackage?.Version != "1.1.0")
                throw new Exception($"The store did not retain the verified remote package needed to repair an installed plugin " +
                                    $"(installed={storeEntry.IsInstalled}, remote={storeEntry.RemotePackage?.Version ?? "none"}).");
            var repairedRemote = new PluginDeliveryService(catalog, http, feedUri)
                .RepairRemoteAsync(storeEntry.RemotePackage).GetAwaiter().GetResult();
            if (!repairedRemote.Directory.Contains("-repair-", StringComparison.Ordinal)
                || catalog.GetInstalledPackage(package.Id)?.Directory != repairedRemote.Directory)
                throw new Exception("Remote repair did not activate a clean, verified package slot.");

            var loader = new WindowsPluginLoader(catalog);
            var state = new Dictionary<string, string> { ["expression"] = "", ["display"] = "0" };
            var hostActions = 0;
            var commits = 0;
            var body = loader.CreateBody(BuiltInPluginIds.Calculator,
                new PluginViewContext(state, true, false, 300, 390, 1, _ => commits++, _ => hostActions++));
            if (body is not Grid grid || grid.Children.OfType<UniformGrid>().SingleOrDefault() is not { } keys)
                throw new Exception("The plugin module was not loaded from its installed package.");
            var seven = keys.Children.OfType<Button>().Single(button => (button.Content as TextBlock)?.Text == "7");
            seven.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (state["expression"] != "7" || state["display"] != "7" || hostActions != 1 || commits != 1)
                throw new Exception("Independent plugin code did not handle its own action.");

            var newPluginSource = Path.Combine(root, "new-plugin");
            Directory.CreateDirectory(newPluginSource);
            var newPluginManifest = new PluginManifest
            {
                Id = "clipdesk.example", Name = "Novo plugin", Description = "Instalado pelo feed.",
                Version = "1.0.0", Runtime = "wpf-v1", PluginApiVersion = 1,
                EntryAssembly = newManifest.EntryAssembly, EntryType = newManifest.EntryType,
                MinimumHostVersion = "0.3.3", DefaultContent = new() { ["expression"] = "", ["display"] = "0" }
            };
            File.WriteAllText(Path.Combine(newPluginSource, "manifest.json"), JsonSerializer.Serialize(newPluginManifest, Json));
            File.Copy(Path.Combine(AppContext.BaseDirectory, "ClipDesk.Plugin.Calculator.dll"),
                Path.Combine(newPluginSource, "ClipDesk.Plugin.Calculator.dll"));
            var newPluginZip = Path.Combine(root, "new-plugin.zip");
            ZipFile.CreateFromDirectory(newPluginSource, newPluginZip);
            var newPluginBytes = File.ReadAllBytes(newPluginZip);
            var newPluginRelease = new PluginFeedPackage
            {
                Id = newPluginManifest.Id, Name = newPluginManifest.Name, Description = newPluginManifest.Description,
                Version = newPluginManifest.Version, Runtime = newPluginManifest.Runtime, PluginApiVersion = 1,
                EntryAssembly = newPluginManifest.EntryAssembly, EntryType = newPluginManifest.EntryType,
                MinimumHostVersion = "0.3.3", Url = "https://example.test/new-plugin.zip",
                Sha256 = Convert.ToHexString(SHA256.HashData(newPluginBytes)),
                DefaultContent = newPluginManifest.DefaultContent
            };
            var newPluginFeed = new PluginFeed { Packages = [newPluginRelease] };
            var visible = PluginDeliveryService.AddRemoteEntries(newPluginFeed, catalog.LoadCatalog(), new Version(0, 3, 3));
            if (visible.Count != 2 || visible.Single(item => item.Manifest.Id == newPluginManifest.Id).IsInstalled)
                throw new Exception("A compatible new plugin did not appear as available in the store.");
            using var newPluginHttp = new HttpClient(new PackageHandler(feedUri,
                JsonSerializer.SerializeToUtf8Bytes(newPluginFeed, Json), newPluginBytes));
            new PluginDeliveryService(catalog, newPluginHttp, feedUri)
                .InstallRemoteAsync(newPluginRelease).GetAwaiter().GetResult();
            if (catalog.LoadCatalog().Count != 2 || loader.CreateBody(newPluginManifest.Id,
                    new PluginViewContext(newPluginManifest.DefaultContent, true, false, 300, 390, 1, _ => { }, _ => { })) is null)
                throw new Exception("A new plugin could not be installed and loaded without changing the app.");
            if (!catalog.GetInstalledPackage(newPluginManifest.Id)!.Directory.StartsWith(installed + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new Exception("A user-selected plugin was not installed under Documents.");
            VerifyLegacyOptionalMigration(root, newPluginSource, newPluginManifest);
            VerifyPluginLifecycle(root);
            var boardView = new BoardObjectView(new BoardObject
            {
                Kind = BoardObjectKind.Plugin, PluginId = newPluginManifest.Id,
                PluginName = newPluginManifest.Name, PluginVersion = newPluginManifest.Version, Width = 300, Height = 390,
                Content = new Dictionary<string, string>(newPluginManifest.DefaultContent)
            });
            var installRequests = 0;
            boardView.PluginInstallRequested += _ => installRequests++;
            var installButton = Descendants<Button>(boardView).SingleOrDefault(button =>
                (button.Content as TextBlock)?.Text == "Instalar");
            if (installButton is null
                || !Descendants<TextBlock>(boardView).Any(text => text.Text == newPluginManifest.Name)
                || Descendants<UniformGrid>(boardView).Any())
                throw new Exception("A shared-board plugin missing locally did not show its safe installation placeholder.");
            installButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (installRequests != 1 || boardView.Object.Content["display"] != "0")
                throw new Exception("The missing-plugin action changed synchronized state or did not request installation.");
            new PluginCatalogService().Install(newPluginManifest, newPluginSource);
            BoardObjectView.InvalidateExternalPlugin(newPluginManifest.Id);
            boardView.RefreshFromObject();
            if (!Descendants<UniformGrid>(boardView).Any() || boardView.Object.Content["display"] != "0")
                throw new Exception("An existing shared-board plugin did not activate without losing state after local installation.");
            VerifyMissingBuiltInDoesNotUseFallback();
            VerifyIndependentModules(root, catalog, loader);
            Console.WriteLine("PASS: compatible remote package installs separately and its WPF code runs from user data.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static void VerifyMissingBuiltInDoesNotUseFallback()
    {
        var catalog = new PluginCatalogService();
        if (!catalog.Uninstall(BuiltInPluginIds.Calculator))
            throw new Exception("The bundled calculator could not be disabled for the missing-plugin check.");
        try
        {
            BoardObjectView.InvalidateExternalPlugin(BuiltInPluginIds.Calculator);
            var view = new BoardObjectView(new BoardObject
            {
                Kind = BoardObjectKind.Calculator, PluginId = BuiltInPluginIds.Calculator,
                PluginName = "Calculadora", PluginVersion = "1.0.0", Width = 300, Height = 390,
                Content = new() { ["expression"] = "6*7", ["display"] = "42" }
            });
            if (Descendants<UniformGrid>(view).Any()
                || !Descendants<Button>(view).Any(button => (button.Content as TextBlock)?.Text == "Instalar"))
                throw new Exception("A missing standard plugin executed the legacy fallback instead of the safe placeholder.");
        }
        finally
        {
            catalog.Enable(BuiltInPluginIds.Calculator);
            BoardObjectView.InvalidateExternalPlugin(BuiltInPluginIds.Calculator);
        }
    }

    private static void VerifyLegacyOptionalMigration(string root, string source, PluginManifest manifest)
    {
        var legacyRoot = Path.Combine(root, "legacy-optional-cache");
        var documentsRoot = Path.Combine(root, "migrated-documents", "Clipdesk");
        var oldPackage = Path.Combine(legacyRoot, manifest.Id, manifest.Version);
        Directory.CreateDirectory(oldPackage);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(oldPackage, Path.GetFileName(file)));
        File.WriteAllText(Path.Combine(legacyRoot, manifest.Id, "current-version.txt"), manifest.Version);
        var migrated = new PluginCatalogService(Path.Combine(root, "empty-bundled"), documentsRoot,
            new Version(0, 3, 3), legacyRoot).LoadCatalog().Single();
        if (!migrated.IsInstalled || !migrated.PackageDirectory.StartsWith(documentsRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) || !Directory.Exists(oldPackage))
            throw new Exception("A legacy optional plugin was not copied safely to Documents.");
    }

    private static void VerifyPluginLifecycle(string root)
    {
        var lifecycleRoot = Path.Combine(root, "lifecycle");
        var bundledRoot = Path.Combine(lifecycleRoot, "bundled");
        var package = Path.Combine(bundledRoot, BuiltInPluginIds.Calculator);
        var optionalRoot = Path.Combine(lifecycleRoot, "optional");
        var updateRoot = Path.Combine(lifecycleRoot, "updates");
        Directory.CreateDirectory(package);
        var manifest = Manifest("1.0.0", "legacy-wpf", 0);
        File.WriteAllText(Path.Combine(package, "manifest.json"), JsonSerializer.Serialize(manifest, Json));
        var catalog = new PluginCatalogService(bundledRoot, optionalRoot, new Version(0, 3, 3), updateRoot);

        if (!catalog.Uninstall(manifest.Id) || catalog.GetInstalledPackage(manifest.Id) is not null
            || catalog.LoadCatalog().Single().IsInstalled)
            throw new Exception("Uninstall did not deactivate the plugin while retaining its store entry.");
        catalog.Enable(manifest.Id);
        if (catalog.GetInstalledPackage(manifest.Id) is null)
            throw new Exception("An uninstalled plugin could not be enabled again.");

        var repaired = catalog.Repair(manifest, package);
        if (!repaired.PackageDirectory.Contains("-repair-", StringComparison.Ordinal)
            || catalog.GetInstalledPackage(manifest.Id)?.Directory != repaired.PackageDirectory
            || !File.Exists(Path.Combine(updateRoot, manifest.Id, "current-package.txt")))
            throw new Exception("Repair did not activate a clean package slot.");
        catalog.Uninstall(manifest.Id);
        var disabled = catalog.LoadCatalog().Single();
        if (disabled.IsInstalled || disabled.PackageDirectory != repaired.PackageDirectory)
            throw new Exception("Repair source was not retained for a later activation.");
        catalog.Enable(manifest.Id);
        if (catalog.GetInstalledPackage(manifest.Id)?.Directory != repaired.PackageDirectory)
            throw new Exception("The repaired package could not be activated again after uninstalling.");
    }

    private static void VerifyIndependentModules(string root, PluginCatalogService catalog, WindowsPluginLoader loader)
    {
        foreach (var (id, module, type) in new[]
        {
            (BuiltInPluginIds.Checklist, "Checklist", "ClipDesk.Plugin.Checklist.ChecklistPlugin"),
            (BuiltInPluginIds.Translator, "Translator", "ClipDesk.Plugin.Translator.TranslatorPlugin"),
            (BuiltInPluginIds.CurrencyConverter, "CurrencyConverter", "ClipDesk.Plugin.CurrencyConverter.CurrencyConverterPlugin")
        })
        {
            var directory = Path.Combine(root, "module-" + module);
            Directory.CreateDirectory(directory);
            var assembly = $"ClipDesk.Plugin.{module}.dll";
            var manifest = new PluginManifest
            {
                Id = id, Name = module, Version = "1.1.0", Runtime = "wpf-v1", PluginApiVersion = 1,
                EntryAssembly = assembly, EntryType = type, MinimumHostVersion = "0.3.3"
            };
            File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(manifest, Json));
            File.Copy(Path.Combine(AppContext.BaseDirectory, assembly), Path.Combine(directory, assembly));
            catalog.Install(manifest, directory);
            loader.Invalidate(id);
            var state = id == BuiltInPluginIds.Checklist
                ? new Dictionary<string, string> { ["items"] = "Primeiro\nSegundo", ["checked"] = "" }
                : new Dictionary<string, string>();
            var commits = 0;
            var view = loader.CreateBody(id, new PluginViewContext(state, true, false, 350, 260, 1,
                _ => commits++, _ => { }));
            if (view is not Grid) throw new Exception($"Independent {module} module did not load.");
            if (id != BuiltInPluginIds.Checklist) continue;
            var toggle = Descendants<Button>(view).FirstOrDefault(button =>
                button.Content is TextBlock text && text.Text == "  ");
            if (toggle is null) throw new Exception("Independent checklist has no toggle.");
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (state.GetValueOrDefault("checked") != "0" || commits != 1)
                throw new Exception("Independent checklist could not update its state.");
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static PluginManifest Manifest(string version, string runtime, int apiVersion) => new()
    {
        Id = BuiltInPluginIds.Calculator, Name = "Calculadora", Version = version, Runtime = runtime,
        PluginApiVersion = apiVersion, EntryAssembly = runtime == "wpf-v1" ? "ClipDesk.Plugin.Calculator.dll" : null,
        EntryType = runtime == "wpf-v1" ? "ClipDesk.Plugin.Calculator.CalculatorPlugin" : null,
        MinimumHostVersion = "0.3.3", InstallByDefault = true
    };

    private sealed class PackageHandler(Uri feedUri, byte[] feedBytes, byte[] packageBytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var bytes = request.RequestUri == feedUri ? feedBytes : packageBytes;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
}
