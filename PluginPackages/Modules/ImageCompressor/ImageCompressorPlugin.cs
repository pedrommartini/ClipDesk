using System.Windows;
using ClipDesk.Plugin.ImageCompressor.UI;
using ClipDesk.PluginSdk.Windows;

namespace ClipDesk.Plugin.ImageCompressor;

/// <summary>
/// Renderizador oficial WPF v2 para o plugin ClipDesk Image Compressor.
/// </summary>
public sealed class ImageCompressorPlugin : IWindowsPluginRenderer
{
    public FrameworkElement CreateBody(WindowsPluginViewContext context)
    {
        return new ImageCompressorControl(context);
    }
}
