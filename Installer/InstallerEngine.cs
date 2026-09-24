using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace ClipDesk.Installer;

public static class InstallerEngine
{
    public static string DefaultInstallPath { get; } = Path.Combine(
        Environment.GetFolderPath(InstallerBrand.IsDevelopment
            ? Environment.SpecialFolder.LocalApplicationData
            : Environment.SpecialFolder.ProgramFiles),
        InstallerBrand.IsDevelopment ? "Programs" : string.Empty,
        InstallerBrand.DirectoryName);

    public static string LegacyInstallPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", InstallerBrand.DirectoryName);

    public static string? FindExistingInstallPath()
    {
        var candidates = new[] { ShortcutService.GetRegisteredInstallPath(), DefaultInstallPath, LegacyInstallPath };
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try
            {
                var fullPath = Path.GetFullPath(candidate);
                ValidateInstallPath(fullPath, testMode: false);
                if (File.Exists(Path.Combine(fullPath, "ClipDesk.exe"))) return fullPath;
            }
            catch
            {
                // Ignore stale or unsafe registry entries.
            }
        }
        return null;
    }

    public static string CreateInstallPath(string parentDirectory)
    {
        var parent = Path.GetFullPath(Environment.ExpandEnvironmentVariables(parentDirectory.Trim().Trim('"')));
        return string.Equals(Path.GetFileName(parent.TrimEnd(Path.DirectorySeparatorChar)), InstallerBrand.DirectoryName, StringComparison.OrdinalIgnoreCase)
            ? parent
            : Path.Combine(parent, InstallerBrand.DirectoryName);
    }

    public static async Task<InstallResult> InstallAsync(string installPath, bool testMode, IProgress<InstallProgress>? progress, bool preserveData = true)
    {
        installPath = Path.GetFullPath(installPath);
        ValidateInstallPath(installPath, testMode);
        if (testMode && !preserveData) throw new InvalidOperationException("O teste do pacote não pode apagar dados pessoais.");
        using var data = !preserveData ? new InstallationData(InstallationData.Root) : null;
        using var dataGuard = data is not null ? AcquireEditionDataGuard() : null;
        var parent = Directory.GetParent(installPath)?.FullName ?? throw new InvalidOperationException("Destino de instalação inválido.");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".ClipDesk-installing-{Guid.NewGuid():N}");
        var backup = Path.Combine(parent, $".ClipDesk-backup-{Guid.NewGuid():N}");
        var movedPrevious = false;
        var activatedNewInstall = false;
        var installationCommitted = false;
        var previousRegisteredInstall = testMode ? null : FindExistingInstallPath();

        try
        {
            progress?.Report(new InstallProgress(.08, "Preparando os arquivos…"));
            Directory.CreateDirectory(staging);
            await ExtractPayloadAsync(staging, progress);
            var executable = Path.Combine(staging, "ClipDesk.exe");
            if (!File.Exists(executable)) throw new InvalidDataException("O pacote não contém o executável do ClipDesk.");

            progress?.Report(new InstallProgress(.72, "Instalando o ClipDesk…"));
            if (previousRegisteredInstall is not null
                && !string.Equals(previousRegisteredInstall, installPath, StringComparison.OrdinalIgnoreCase))
                EnsureInstalledAppIsClosed(previousRegisteredInstall);
            if (Directory.Exists(installPath))
            {
                EnsureInstalledAppIsClosed(installPath);
                Directory.Move(installPath, backup);
                movedPrevious = true;
            }
            Directory.Move(staging, installPath);
            activatedNewInstall = true;
            data?.Stage();

            var installedExecutable = Path.Combine(installPath, "ClipDesk.exe");
            if (!testMode)
            {
                progress?.Report(new InstallProgress(.86, "Criando atalhos…"));
                var uninstaller = Path.Combine(installPath, "Uninstall ClipDesk.exe");
                File.Copy(Environment.ProcessPath!, uninstaller, overwrite: true);
                ShortcutService.CreateShortcuts(installedExecutable);
                var bytes = Directory.EnumerateFiles(installPath, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length);
                ShortcutService.RegisterUninstaller(installPath, uninstaller, bytes);
            }

            installationCommitted = true;
            data?.Commit();
            if (previousRegisteredInstall is not null
                && !string.Equals(previousRegisteredInstall, installPath, StringComparison.OrdinalIgnoreCase))
            {
                try { Directory.Delete(previousRegisteredInstall, recursive: true); }
                catch { /* The new installation is already complete; leave the stale copy recoverable. */ }
            }

            if (movedPrevious && Directory.Exists(backup))
            {
                try { Directory.Delete(backup, recursive: true); }
                catch { /* Keep the old binaries recoverable if another process has them open. */ }
            }
            var count = Directory.EnumerateFiles(installPath, "*", SearchOption.AllDirectories).Count();
            progress?.Report(new InstallProgress(1, "Instalação concluída"));
            return new InstallResult(installPath, installedExecutable, count);
        }
        catch
        {
            if (installationCommitted) throw;
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            if (activatedNewInstall && Directory.Exists(installPath)) Directory.Delete(installPath, recursive: true);
            if (movedPrevious && Directory.Exists(backup)) Directory.Move(backup, installPath);
            throw;
        }
    }

    public static async Task UninstallAsync(string installPath, IProgress<InstallProgress>? progress, bool deleteData = false)
    {
        installPath = Path.GetFullPath(installPath);
        ValidateInstallPath(installPath, testMode: false);
        progress?.Report(new InstallProgress(.15, "Fechando o ClipDesk…"));
        EnsureInstalledAppIsClosed(installPath);
        using var dataGuard = deleteData ? AcquireEditionDataGuard() : null;
        await Task.Delay(180);
        progress?.Report(new InstallProgress(.48, "Removendo atalhos…"));
        ShortcutService.RemoveShortcuts();
        ShortcutService.Unregister();
        progress?.Report(new InstallProgress(.72, "Removendo o aplicativo…"));
        if (Directory.Exists(installPath)) Directory.Delete(installPath, recursive: true);
        if (deleteData)
        {
            progress?.Report(new InstallProgress(.9, "Apagando mesas, histórico e caches locais…"));
            using var data = new InstallationData(InstallationData.Root);
            data.Stage(); data.Commit();
        }
        progress?.Report(new InstallProgress(1, "ClipDesk removido"));
    }

    private static async Task ExtractPayloadAsync(string destination, IProgress<InstallProgress>? progress)
    {
        await using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("ClipDeskPayload.zip")
            ?? throw new InvalidDataException("O pacote de instalação está incompleto.");
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        for (var index = 0; index < archive.Entries.Count; index++)
        {
            var entry = archive.Entries[index];
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Entrada insegura no pacote.");
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await input.CopyToAsync(output);
            progress?.Report(new InstallProgress(.12 + .55 * (index + 1d) / archive.Entries.Count, $"Copiando {entry.Name}…"));
        }
    }

    private static void EnsureInstalledAppIsClosed(string installPath)
    {
        var executable = Path.Combine(installPath, "ClipDesk.exe");
        foreach (var process in Process.GetProcessesByName("ClipDesk"))
        {
            try
            {
                if (!string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase)) continue;
                if (process.CloseMainWindow() && process.WaitForExit(5000)) continue;
                throw new IOException("Feche o ClipDesk antes de continuar a instalação.");
            }
            finally { process.Dispose(); }
        }
    }

    private static Mutex AcquireEditionDataGuard()
    {
        // Keep a handle for the entire operation so a newly launched app cannot reopen its database.
        // Ownership is unnecessary: the app tests creation, and async continuations may change threads.
        var instance = new Mutex(false, $"Local\\{InstallerBrand.StartupValueName}.SingleInstance", out var created);
        if (created) return instance;
        instance.Dispose();
        throw new IOException($"Feche o {InstallerBrand.AppName}, inclusive na bandeja, antes de apagar os dados.");
    }

    private static void ValidateInstallPath(string installPath, bool testMode)
    {
        var candidate = Path.GetFullPath(installPath).TrimEnd(Path.DirectorySeparatorChar);
        if (testMode)
        {
            var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!(candidate + Path.DirectorySeparatorChar).StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("O destino do teste está fora da área temporária permitida.");
            return;
        }

        var dataRoot = Path.GetFullPath(InstallationData.Root).TrimEnd(Path.DirectorySeparatorChar);
        if ((candidate+Path.DirectorySeparatorChar).StartsWith(dataRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)
            || (dataRoot+Path.DirectorySeparatorChar).StartsWith(candidate+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A pasta do aplicativo deve ser separada da pasta de mesas e dados pessoais.");

        if (!string.Equals(Path.GetFileName(candidate), InstallerBrand.DirectoryName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"A instalação precisa usar uma pasta final chamada {InstallerBrand.DirectoryName}.");

        var root = Path.GetPathRoot(candidate)?.TrimEnd(Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(root) || string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Escolha uma pasta segura para instalar o ClipDesk.");

        var windowsDirectory = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if ((candidate + Path.DirectorySeparatorChar).StartsWith(windowsDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O ClipDesk não pode ser instalado dentro da pasta do Windows.");
    }
}
