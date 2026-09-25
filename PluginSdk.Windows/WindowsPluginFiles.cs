using System.IO;
using ClipDesk.PluginSdk;

namespace ClipDesk.PluginSdk.Windows;

/// <summary>Compatibility helpers for Windows renderers. Portable modules use IPluginFileService.</summary>
public static class WindowsPluginFiles
{
    public static PluginBoardFile FromPath(string path, string? displayName = null) =>
        new(displayName ?? Path.GetFileName(path), Source: path);

    public static string DefaultDocumentDirectory(string pluginFolderName)
    {
        if (string.IsNullOrWhiteSpace(pluginFolderName) || pluginFolderName is "." or ".."
            || pluginFolderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || pluginFolderName.Contains('/') || pluginFolderName.Contains('\\'))
            throw new ArgumentException("Use somente o nome simples da pasta do plugin.", nameof(pluginFolderName));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ClipDesk", pluginFolderName);
    }
}
