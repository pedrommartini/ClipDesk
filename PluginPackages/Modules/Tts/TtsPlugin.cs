using System.Windows;
using ClipDesk.PluginSdk.Windows;
using ClipDesk.Plugin.Tts.Views;

namespace ClipDesk.Plugin.Tts;

/// <summary>
/// Windows WPF plugin renderer for the ClipDesk Text-to-Speech (TTS) plugin.
/// </summary>
public sealed class TtsPlugin : IWindowsPluginRenderer
{
    public FrameworkElement CreateBody(WindowsPluginViewContext context)
    {
        return new TtsView(context);
    }
}
