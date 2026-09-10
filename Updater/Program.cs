using System.Diagnostics;
using System.IO.Compression;

if (args.Length == 0 || !args.Any(argument => string.Equals(argument, "--update", StringComparison.OrdinalIgnoreCase))) return;

var arguments = ParseArguments(args);
if (!arguments.TryGetValue("--pid", out var pidValue)
    || !int.TryParse(pidValue, out var parentPid)
    || !arguments.TryGetValue("--zip", out var packagePath)
    || !arguments.TryGetValue("--target", out var targetDirectory)
    || !arguments.TryGetValue("--exe", out var executablePath)) return;

WaitForParentToExit(parentPid);
var stagingDirectory = Path.Combine(Path.GetTempPath(), "ClipDesk", "Staging", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(stagingDirectory);
try
{
    ZipFile.ExtractToDirectory(packagePath, stagingDirectory, overwriteFiles: true);
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

static Dictionary<string, string> ParseArguments(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index + 1 < args.Length; index++)
    {
        if (args[index].StartsWith("--", StringComparison.Ordinal)) result[args[index]] = args[++index].Trim('"');
    }
    return result;
}

static void WaitForParentToExit(int processId)
{
    try
    {
        using var process = Process.GetProcessById(processId);
        if (!process.HasExited) process.WaitForExit(TimeSpan.FromMinutes(2));
    }
    catch (ArgumentException) { }
}

static string FindPackageRoot(string stagingDirectory)
{
    if (File.Exists(Path.Combine(stagingDirectory, "ClipDesk.exe"))) return stagingDirectory;
    var executable = Directory.GetFiles(stagingDirectory, "ClipDesk.exe", SearchOption.AllDirectories).FirstOrDefault();
    if (executable is null) throw new FileNotFoundException("O pacote não contém o executável do ClipDesk.");
    return Path.GetDirectoryName(executable)!;
}

static void CopyDirectory(string sourceDirectory, string targetDirectory)
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

static void TryDeleteFile(string path)
{
    try { File.Delete(path); } catch { }
}

static void TryDeleteDirectory(string path)
{
    try { Directory.Delete(path, recursive: true); } catch { }
}
