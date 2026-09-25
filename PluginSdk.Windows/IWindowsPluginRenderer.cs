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
    public double Width { get; private set; }
    public double Height { get; private set; }
    public double Scale { get; private set; }
    public double ViewportZoom { get; private set; } = 1;
    public string AccentColor { get; }
    public event Action? ViewportZoomChanged;
    /// <summary>Subscribe in CreateBody to receive files explicitly dropped on this plugin.
    /// The host validates the file.read permission and supplies existing local files only.
    /// Keep file contents out of PluginState; read them only for the user's requested action.</summary>
    public event Func<IReadOnlyList<PluginDroppedFile>, Task>? FilesDropped;
    /// <summary>Raised when this instance is resized. Use it for layout switches;
    /// Grid, wrapping and scrolling should handle ordinary size changes.</summary>
    public event Action? LayoutChanged;

    public void UpdateLayout(double width, double height, double scale)
    {
        if (Width == width && Height == height && Scale == scale) return;
        Width = width;
        Height = height;
        Scale = scale;
        LayoutChanged?.Invoke();
    }

    public void UpdateViewportZoom(double zoom)
    {
        var next = Math.Clamp(zoom, .35, 1);
        if (Math.Abs(ViewportZoom - next) < .0001) return;
        ViewportZoom = next;
        ViewportZoomChanged?.Invoke();
    }

    public bool AcceptsFileDrops => FilesDropped is not null;
    public async Task DeliverFilesAsync(IReadOnlyList<PluginDroppedFile> files)
    {
        if (FilesDropped is null) return;
        foreach (Func<IReadOnlyList<PluginDroppedFile>, Task> handler in FilesDropped.GetInvocationList())
            await handler(files);
    }

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

public sealed record PluginDroppedFile(string Path, string FileName, long Length);

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
