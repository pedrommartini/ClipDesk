using System.Windows;
using ClipDesk.PluginSdk;

namespace ClipDesk.PluginSdk.Windows;

/// <summary>WPF renderer for a portable v2 plugin module.</summary>
public interface IWindowsPluginRenderer
{
    FrameworkElement CreateBody(WindowsPluginViewContext context);
}

public enum PluginHostActionKind
{
    CopyToClipboard,
    OpenUri,
    ShowMessage,
    AddFilesToBoard
}

public sealed record PluginHostAction(PluginHostActionKind Kind, string? Value = null)
{
    public PluginBoardFileRequest? BoardFiles { get; init; }

    public static PluginHostAction AddFiles(PluginBoardFileRequest request) =>
        new(PluginHostActionKind.AddFilesToBoard) { BoardFiles = request };
}

public sealed class WindowsPluginViewContext
{
    private readonly Action _beforeChange;
    private readonly Action<PluginState, bool> _changed;
    private readonly Action<PluginHostAction> _hostAction;

    public WindowsPluginViewContext(
        IClipDeskPluginModule module,
        PluginState state,
        IPluginExecutionContext execution,
        bool isDarkMode,
        bool isEditing,
        double width,
        double height,
        double scale,
        string accentColor,
        Action beforeChange,
        Action<PluginState, bool> changed,
        Action<PluginHostAction> hostAction)
    {
        Module = module;
        State = module.NormalizeState(state);
        Execution = execution;
        IsDarkMode = isDarkMode;
        IsEditing = isEditing;
        Width = width;
        Height = height;
        Scale = scale;
        AccentColor = accentColor;
        _beforeChange = beforeChange;
        _changed = changed;
        _hostAction = hostAction;
    }

    public IClipDeskPluginModule Module { get; }
    public PluginState State { get; private set; }
    public IPluginExecutionContext Execution { get; }
    public bool IsDarkMode { get; }
    public bool IsEditing { get; }
    public double Width { get; }
    public double Height { get; }
    public double Scale { get; }
    public string AccentColor { get; }

    public void NotifyBeforeChange() => _beforeChange();

    public void Commit(PluginState state, bool rebuild = true)
    {
        var normalized = Module.NormalizeState(state);
        State = normalized;
        _changed(normalized, rebuild);
    }

    public async ValueTask<PluginCommandResult> ExecuteAsync(
        PluginCommand command,
        bool rebuild = true,
        CancellationToken cancellationToken = default,
        bool recordUndo = true)
    {
        if (recordUndo) _beforeChange();
        var result = await Module.ExecuteAsync(State, command, Execution, cancellationToken);
        if (result.Succeeded && !result.State.ContentEquals(State)) Commit(result.State, rebuild);
        return result;
    }

    public void RequestHostAction(PluginHostAction action) => _hostAction(action);
}

/// <summary>Transition helper for packages that need to remain loadable by a v1 host.</summary>
public static class LegacyPluginAdapter
{
    public static WindowsPluginViewContext CreateContext(IClipDeskPluginModule module, PluginViewContext legacy) => new(
        module, new PluginState(legacy.State), new PluginExecutionContext(new OfflineNetworkClient()),
        legacy.IsDarkMode, legacy.IsEditing, legacy.Width, legacy.Height, legacy.Scale, "#9B7DFF",
        () => legacy.RequestHostAction("plugin:before-change"),
        (state, rebuild) =>
        {
            legacy.State.Clear();
            foreach (var item in state.Values) legacy.State[item.Key] = item.Value;
            legacy.Commit(rebuild);
        },
        action => legacy.RequestHostAction(action.Kind switch
        {
            PluginHostActionKind.CopyToClipboard => $"plugin:copy:{action.Value}",
            PluginHostActionKind.OpenUri => $"plugin:open:{action.Value}",
            PluginHostActionKind.AddFilesToBoard => "plugin:message:Adicionar arquivos à mesa requer um plugin v2.",
            _ => $"plugin:message:{action.Value}"
        }));

    private sealed class OfflineNetworkClient : IPluginNetworkClient
    {
        public Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken = default) =>
            Task.FromException<string>(new InvalidOperationException("Rede não disponível no adaptador legado."));
    }
}
