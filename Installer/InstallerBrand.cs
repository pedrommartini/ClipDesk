namespace ClipDesk.Installer;

internal static class InstallerBrand
{
#if CLIPDESK_DEV
    public const bool IsDevelopment = true;
    public const string AppName = "ClipDesk DEV";
    public const string DirectoryName = "ClipDesk DEV";
    public const string RegistryKeyName = "ClipDesk DEV";
    public const string ShortcutFileName = "ClipDesk DEV.lnk";
    public const string DataDirectoryName = "ClipDesk-Dev";
    public const string StartupValueName = "ClipDesk.Dev";
#else
    public const bool IsDevelopment = false;
    public const string AppName = "ClipDesk";
    public const string DirectoryName = "ClipDesk";
    public const string RegistryKeyName = "ClipDesk";
    public const string ShortcutFileName = "ClipDesk.lnk";
    public const string DataDirectoryName = "ClipDesk";
    public const string StartupValueName = "ClipDesk";
#endif
}
