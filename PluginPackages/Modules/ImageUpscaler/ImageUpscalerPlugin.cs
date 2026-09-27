using System.Windows;
using ClipDesk.Plugin.ImageUpscaler.UI;
using ClipDesk.PluginSdk.Windows;

namespace ClipDesk.Plugin.ImageUpscaler;

/// <summary>
/// Renderizador oficial WPF v2 para o plugin ClipDesk Image Upscaler.
/// </summary>
public sealed class ImageUpscalerPlugin : IWindowsPluginRenderer
{
    public FrameworkElement CreateBody(WindowsPluginViewContext context)
    {
        return new ImageUpscalerControl(context);
    }
}
