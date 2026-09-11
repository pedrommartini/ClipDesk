using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace ClipDesk.Installer;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.FirstOrDefault() == "--test-install" && e.Args.ElementAtOrDefault(1) is { } testPath)
        {
            try
            {
                var result = await InstallerEngine.InstallAsync(testPath, testMode: true, progress: null);
                var report = JsonSerializer.Serialize(new
                {
                    success = true,
                    result.InstallPath,
                    result.FileCount,
                    executable = result.ExecutablePath,
                    executableExists = File.Exists(result.ExecutablePath)
                }, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(Path.Combine(testPath, "install-test-result.json"), report);
                Shutdown(0);
            }
            catch (Exception ex)
            {
                try { await File.WriteAllTextAsync(testPath + ".error.txt", ex.ToString()); } catch { }
                Shutdown(1);
            }
            return;
        }

        if (e.Args.FirstOrDefault() == "--uninstall")
        {
            var currentExecutable = Path.GetFullPath(Environment.ProcessPath!);
            var installedUninstaller = Path.Combine(AppContext.BaseDirectory, "Uninstall ClipDesk.exe");
            if (string.Equals(currentExecutable, Path.GetFullPath(installedUninstaller), StringComparison.OrdinalIgnoreCase))
            {
                var temporary = Path.Combine(Path.GetTempPath(), $"ClipDesk-Uninstall-{Guid.NewGuid():N}.exe");
                File.Copy(currentExecutable, temporary, overwrite: true);
                Process.Start(new ProcessStartInfo(temporary, $"--uninstall-worker \"{AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)}\"") { UseShellExecute = true });
                Shutdown(0);
                return;
            }
        }

        var uninstallPath = e.Args.FirstOrDefault() == "--uninstall-worker"
            ? e.Args.ElementAtOrDefault(1) ?? InstallerEngine.DefaultInstallPath
            : null;
        var window = new MainWindow(uninstallPath);
        MainWindow = window;
        window.Show();
    }
}
