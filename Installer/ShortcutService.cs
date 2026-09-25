using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace ClipDesk.Installer;

internal static class ShortcutService
{
    private static string AppName => InstallerBrand.AppName;
    private static string UninstallKeyPath => @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + InstallerBrand.RegistryKeyName;
    public static string DesktopShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), InstallerBrand.ShortcutFileName);
    public static string StartMenuDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName);
    public static string StartMenuShortcut => Path.Combine(StartMenuDirectory, InstallerBrand.ShortcutFileName);

    public static void CreateShortcuts(string executable)
    {
        Directory.CreateDirectory(StartMenuDirectory);
        CreateShortcut(DesktopShortcut, executable);
        CreateShortcut(StartMenuShortcut, executable);
    }

    public static void RemoveShortcuts()
    {
        if (File.Exists(DesktopShortcut)) File.Delete(DesktopShortcut);
        if (File.Exists(StartMenuShortcut)) File.Delete(StartMenuShortcut);
        if (Directory.Exists(StartMenuDirectory) && !Directory.EnumerateFileSystemEntries(StartMenuDirectory).Any())
            Directory.Delete(StartMenuDirectory);
    }

    public static void RegisterUninstaller(string installPath, string uninstallerPath, long estimatedBytes)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath, writable: true);
        key.SetValue("DisplayName", InstallerBrand.AppName);
        key.SetValue("DisplayVersion", "0.4.3");
        key.SetValue("Publisher", "ClipDesk");
        key.SetValue("InstallLocation", installPath);
        key.SetValue("DisplayIcon", Path.Combine(installPath, "ClipDesk.exe"));
        key.SetValue("UninstallString", $"\"{uninstallerPath}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)Math.Max(1, estimatedBytes / 1024), RegistryValueKind.DWord);
    }

    public static string? GetRegisteredInstallPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath, writable: false);
        return key?.GetValue("InstallLocation") as string;
    }

    public static void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);
        using var startup = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        startup?.DeleteValue(InstallerBrand.StartupValueName, throwOnMissingValue: false);
    }

    private static void CreateShortcut(string shortcutPath, string executable)
    {
        var shellLinkType = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))
            ?? throw new InvalidOperationException("O componente de atalhos do Windows não está disponível.");
        var linkObject = Activator.CreateInstance(shellLinkType)
            ?? throw new InvalidOperationException($"Não foi possível criar o atalho do {InstallerBrand.AppName}.");
        var link = (IShellLinkW)linkObject;
        try
        {
            link.SetPath(executable);
            link.SetWorkingDirectory(Path.GetDirectoryName(executable)!);
            link.SetDescription($"Abra suas mesas visuais do {InstallerBrand.AppName}");
            link.SetIconLocation(executable, 0);
            ((IPersistFile)link).Save(shortcutPath, false);
        }
        finally { Marshal.FinalReleaseComObject(linkObject); }
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
