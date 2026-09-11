using System.Diagnostics;
using System.IO.Compression;

if (args.Length == 0 || !args.Any(argument => string.Equals(argument, "--update", StringComparison.OrdinalIgnoreCase))) return;

var arguments = ParseArguments(args);
if (!arguments.TryGetValue("--pid", out var pidValue)
    || !int.TryParse(pidValue, out var parentPid)
    || !arguments.TryGetValue("--zip", out var packagePath)
    || !arguments.TryGetValue("--target", out var targetDirectory)
    || !arguments.TryGetValue("--exe", out var executablePath)) return;

var diagnosticPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipDesk", "update-error.log");
var updateSucceeded = false;
try
{
    WaitForParentToExit(parentPid);
    var stagingDirectory = Path.Combine(Path.GetTempPath(), "ClipDesk", "Staging", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(stagingDirectory);
    try
    {
        ZipFile.ExtractToDirectory(packagePath, stagingDirectory, overwriteFiles: true);
        var sourceDirectory = FindPackageRoot(stagingDirectory);
        CopyDirectory(sourceDirectory, targetDirectory);
        var restarted = Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = targetDirectory,
            UseShellExecute = true
        });
        if (restarted is null) throw new InvalidOperationException("Não foi possível reiniciar o ClipDesk após a atualização.");
        updateSucceeded = true;
    }
    finally
    {
        TryDeleteDirectory(stagingDirectory);
    }
}
catch (Exception ex)
{
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(diagnosticPath)!);
        File.WriteAllText(diagnosticPath, $"{DateTime.Now:O}{Environment.NewLine}{ex}");
    }
    catch { }
    try
    {
        // A cópia anterior continua sendo a alternativa mais útil caso a substituição falhe.
        Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = targetDirectory,
            UseShellExecute = true
        });
    }
    catch { }
}
finally
{
    if (updateSucceeded) TryDeleteFile(packagePath);
}

static Dictionary<string, string> ParseArguments(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index + 1 < args.Length; index++)
    {
        if (args[index].StartsWith("--", StringComparison.Ordinal)
            && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            result[args[index]] = args[++index].Trim('"');
        }
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
