using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Security.Principal;
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
                    defaultInstallPath = InstallerEngine.DefaultInstallPath,
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

        if (InstallerBrand.IsDevelopment && e.Args.FirstOrDefault() == "--update-existing-dev")
        {
            var existingPath = InstallerEngine.FindExistingInstallPath();
            if (existingPath is null) { Shutdown(1); return; }
            try
            {
                await InstallerEngine.InstallAsync(existingPath, testMode: false, progress: null, preserveData: true);
                Shutdown(0);
            }
            catch (Exception ex)
            {
                try { await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "ClipDesk-DEV-update-error.txt"), ex.ToString()); } catch { }
                Shutdown(1);
            }
            return;
        }

        if (!InstallerBrand.IsDevelopment && !new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
        {
            try
            {
                var elevated = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
                foreach (var argument in e.Args) elevated.ArgumentList.Add(argument);
                Process.Start(elevated);
                Shutdown(0);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                Shutdown(1);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Não foi possível abrir o instalador como administrador: {ex.Message}", InstallerBrand.AppName);
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
