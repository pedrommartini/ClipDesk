namespace ClipDesk.PluginSdk;

/// <summary>Host-specific UI adapter with a platform-neutral command and viewport boundary.</summary>
public interface IPluginRendererAdapter<TView> : IAsyncDisposable
{
    TView CreateView(PluginViewport viewport, PluginState state,
        Func<PluginCommand, CancellationToken, ValueTask<PluginCommandResult>> dispatch);
    void Update(PluginViewport viewport, PluginState state);
}

/// <summary>Static registration works on hosts that cannot load code at runtime.</summary>
public sealed class PluginHostRegistry<TView>
{
    private readonly Dictionary<string, Func<IClipDeskPluginModuleV3>> _modules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<IPluginRendererAdapter<TView>>> _renderers = new(StringComparer.OrdinalIgnoreCase);
    private readonly PluginSemanticVersion _contractVersion;
    private readonly Version _hostVersion;

    public PluginHostRegistry(PluginSemanticVersion contractVersion, Version hostVersion)
    {
        if (contractVersion.Major != 3) throw new ArgumentOutOfRangeException(nameof(contractVersion), "O host v3 exige contrato 3.x.y.");
        _contractVersion = contractVersion;
        _hostVersion = hostVersion ?? throw new ArgumentNullException(nameof(hostVersion));
    }

    public void RegisterModule(string registration, Func<IClipDeskPluginModuleV3> factory) =>
        _modules.Add(Required(registration), factory ?? throw new ArgumentNullException(nameof(factory)));

    public void RegisterRenderer(string registration, Func<IPluginRendererAdapter<TView>> factory) =>
        _renderers.Add(Required(registration), factory ?? throw new ArgumentNullException(nameof(factory)));

    public PluginHostedInstance<TView> Create(PluginManifest manifest, string platform,
        IPluginCapabilityProvider capabilities, IPluginExecutionScheduler scheduler,
        PluginViewport viewport, PluginState? restoredState = null)
    {
        if (PluginManifestValidator.Validate(manifest).Count > 0 || manifest.ManifestVersion != 3)
            throw new ArgumentException("Manifesto v3 inválido.", nameof(manifest));
        if (!PluginCompatibility.Supports(manifest, _hostVersion, _contractVersion, platform, capabilities))
            throw new InvalidOperationException($"O plugin não é compatível com a plataforma '{platform}', o host {_hostVersion}, o contrato {_contractVersion} ou suas capabilities obrigatórias.");
        if (manifest.Module?.Registration is not { Length: > 0 } moduleId || !_modules.TryGetValue(moduleId, out var module))
            throw new InvalidOperationException($"Módulo '{manifest.Module?.Registration}' não está registrado neste host.");
        var rendererId = manifest.RendererFor(platform)?.Registration;
        if (rendererId is null || !_renderers.TryGetValue(rendererId, out var renderer))
            throw new InvalidOperationException($"Renderer '{rendererId}' não está registrado neste host.");
        var session = new PluginModuleSession(manifest, module(), capabilities, scheduler, restoredState);
        return new PluginHostedInstance<TView>(session, renderer(), viewport);
    }

    private static string Required(string value) => !string.IsNullOrWhiteSpace(value)
        ? value : throw new ArgumentException("Registration ID é obrigatório.", nameof(value));
}

public sealed class PluginHostedInstance<TView> : IAsyncDisposable
{
    private readonly PluginModuleSession _session;
    private readonly IPluginRendererAdapter<TView> _renderer;
    private PluginViewport _viewport;
    private TView? _view;
    private bool _viewCreated;

    internal PluginHostedInstance(PluginModuleSession session, IPluginRendererAdapter<TView> renderer, PluginViewport viewport)
    {
        _session = session;
        _renderer = renderer;
        _viewport = viewport;
    }

    public PluginState State => _session.State;
    public PluginSessionStatus Status => _session.Status;

    public async ValueTask<TView> ActivateAsync(CancellationToken cancellationToken = default)
    {
        await _session.ActivateAsync(cancellationToken);
        if (!_viewCreated)
        {
            _view = _renderer.CreateView(_viewport, _session.State, ExecuteAsync);
            _viewCreated = true;
        }
        else _renderer.Update(_viewport, _session.State);
        return _view!;
    }

    public async ValueTask<PluginCommandResult> ExecuteAsync(PluginCommand command, CancellationToken cancellationToken = default)
    {
        var result = await _session.ExecuteAsync(command, cancellationToken);
        if (result.Succeeded) _renderer.Update(_viewport, _session.State);
        return result;
    }

    public void UpdateViewport(PluginViewport viewport)
    {
        _viewport = viewport;
        if (Status == PluginSessionStatus.Active) _renderer.Update(viewport, _session.State);
    }

    public ValueTask SuspendAsync(CancellationToken cancellationToken = default) => _session.SuspendAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        try { await _session.DisposeAsync(); }
        finally { await _renderer.DisposeAsync(); }
    }
}
