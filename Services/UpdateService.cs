using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;

namespace ClipDesk.Services;

public sealed record AvailableUpdate(Version Version, string TagName, string DownloadUrl);

public static class UpdateService
{
    public const string CurrentVersion = "0.4.0";
    private const string ReleasesUrl = "https://api.github.com/repos/pedrommartini/ClipDesk/releases";
    private const string PackageName = "ClipDesk-Windows-x64.zip";

    private static readonly HttpClient Http = CreateHttpClient();

    public static async Task<AvailableUpdate?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (AppEnvironment.IsTestClient) return null;
        AvailableUpdate? newest = null;
        for (var page = 1; page <= 10; page++)
        {
            using var response = await Http.GetAsync($"{ReleasesUrl}?per_page=100&page={page}", cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var releases = document.RootElement;
            foreach (var release in releases.EnumerateArray())
            {
                if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) continue;
                var tagName = release.GetProperty("tag_name").GetString() ?? string.Empty;
                if (!tagName.StartsWith('v') || !TryParseVersion(tagName, out var version)
                    || version <= new Version(CurrentVersion) || (newest is not null && version <= newest.Version)) continue;
                foreach (var asset in release.GetProperty("assets").EnumerateArray())
                {
                    if (!string.Equals(asset.GetProperty("name").GetString(), PackageName, StringComparison.OrdinalIgnoreCase)) continue;
                    var url = asset.GetProperty("browser_download_url").GetString();
                    if (Uri.TryCreate(url, UriKind.Absolute, out var downloadUri)
                        && downloadUri.Scheme == Uri.UriSchemeHttps
                        && string.Equals(downloadUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                        newest = new AvailableUpdate(version, tagName, downloadUri.AbsoluteUri);
                }
            }
            if (releases.GetArrayLength() < 100) break;
        }
        return newest;
    }

    public static async Task DownloadAndStartAsync(AvailableUpdate update, CancellationToken cancellationToken = default)
    {
        if (AppEnvironment.IsTestClient) throw new InvalidOperationException("As instâncias de teste são atualizadas junto com o pacote do ClipDesk.");
        var applicationDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var updaterSource = Path.Combine(applicationDirectory, "ClipDesk.Updater.exe");
        if (!File.Exists(updaterSource)) throw new FileNotFoundException("O componente de atualização não está instalado.", updaterSource);

        var updateDirectory = Path.Combine(Path.GetTempPath(), "ClipDesk", "Updates", $"{update.Version}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(updateDirectory);
        var packagePath = Path.Combine(updateDirectory, PackageName);
        var updaterCopy = Path.Combine(updateDirectory, "ClipDesk.Updater.exe");

        await using (var source = await Http.GetStreamAsync(update.DownloadUrl, cancellationToken))
        await using (var destination = File.Create(packagePath))
        {
            await source.CopyToAsync(destination, cancellationToken);
        }

        File.Copy(updaterSource, updaterCopy, overwrite: true);
        var executablePath = Environment.ProcessPath ?? Path.Combine(applicationDirectory, "ClipDesk.exe");
        var arguments = string.Join(" ",
            "--update",
            "--pid", Environment.ProcessId.ToString(),
            "--zip", QuoteArgument(packagePath),
            "--target", QuoteArgument(applicationDirectory),
            "--exe", QuoteArgument(executablePath));

        var programFiles = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var needsElevation = (Path.GetFullPath(applicationDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .StartsWith(programFiles, StringComparison.OrdinalIgnoreCase);
        var updaterProcess = Process.Start(new ProcessStartInfo
        {
            FileName = updaterCopy,
            Arguments = arguments,
            WorkingDirectory = applicationDirectory,
            UseShellExecute = needsElevation,
            Verb = needsElevation ? "runas" : string.Empty,
            CreateNoWindow = !needsElevation,
            WindowStyle = ProcessWindowStyle.Hidden
        });
        if (updaterProcess is null)
            throw new InvalidOperationException("Não foi possível iniciar o atualizador do ClipDesk.");
    }

    public static async Task RunUpdaterAsync(string[] args)
    {
        var options = ParseArguments(args);
        if (!options.TryGetValue("--pid", out var pidValue)
            || !int.TryParse(pidValue, out var parentPid)
            || !options.TryGetValue("--zip", out var packagePath)
            || !options.TryGetValue("--target", out var targetDirectory)
            || !options.TryGetValue("--exe", out var executablePath)) return;

        WaitForParentToExit(parentPid);
        var stagingDirectory = Path.Combine(Path.GetTempPath(), "ClipDesk", "Staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            System.IO.Compression.ZipFile.ExtractToDirectory(packagePath, stagingDirectory, overwriteFiles: true);
            var sourceDirectory = FindPackageRoot(stagingDirectory);
            CopyDirectory(sourceDirectory, targetDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = targetDirectory,
                UseShellExecute = true
            });
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
            TryDeleteFile(packagePath);
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ClipDesk-Updater/0.4.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static bool TryParseVersion(string tagName, out Version version)
    {
        var normalized = tagName.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V')) normalized = normalized[1..];
        return Version.TryParse(normalized, out version!);
    }

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (args[index].StartsWith("--", StringComparison.Ordinal)) result[args[index]] = args[++index].Trim('"');
        }
        return result;
    }

    private static void WaitForParentToExit(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited) process.WaitForExit(TimeSpan.FromMinutes(2));
        }
        catch (ArgumentException)
        {
            // The parent has already exited.
        }
    }

    private static string FindPackageRoot(string stagingDirectory)
    {
        if (File.Exists(Path.Combine(stagingDirectory, "ClipDesk.exe"))) return stagingDirectory;
        var executable = Directory.GetFiles(stagingDirectory, "ClipDesk.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (executable is null) throw new FileNotFoundException("O pacote não contém o executável do ClipDesk.");
        return Path.GetDirectoryName(executable)!;
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (var sourceFile in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
            var targetFile = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(sourceFile, targetFile, overwrite: true);
        }
    }

    private static string QuoteArgument(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }
}
