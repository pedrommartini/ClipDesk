using System.Windows;

namespace ClipDesk.PluginSdk.Windows;

/// <summary>Version 1 of the Windows view contract. The host owns the board frame and persistence.</summary>
public interface IWindowsPlugin
{
    FrameworkElement CreateBody(PluginViewContext context);
}

public sealed class PluginViewContext
{
    private readonly Action<bool> _changed;
    private readonly Action<string> _hostAction;

    public PluginViewContext(Dictionary<string, string> state, bool isDarkMode, bool isEditing,
        double width, double height, double scale, Action<bool> changed, Action<string> hostAction)
    {
        State = state;
        IsDarkMode = isDarkMode;
        IsEditing = isEditing;
        Width = width;
        Height = height;
        Scale = scale;
        _changed = changed;
        _hostAction = hostAction;
    }

    public Dictionary<string, string> State { get; }
    public bool IsDarkMode { get; }
    public bool IsEditing { get; }
    public double Width { get; }
    public double Height { get; }
    public double Scale { get; }
    public void Commit(bool rebuild = true) => _changed(rebuild);
    public void RequestHostAction(string action) => _hostAction(action);
}
