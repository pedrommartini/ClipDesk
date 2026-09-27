namespace ClipDesk.PluginSdk;

/// <summary>A host supplies a scheduler appropriate to its UI thread and isolation model.</summary>
public interface IPluginExecutionScheduler
{
    ValueTask<PluginCommandResult> RunAsync(
        Func<CancellationToken, ValueTask<PluginCommandResult>> operation,
        CancellationToken cancellationToken);
}

public enum PluginSessionStatus { Created, Active, Suspended, Disposed }

/// <summary>Portable host coordinator for state, capability negotiation and lifecycle.</summary>
public sealed class PluginModuleSession : IAsyncDisposable
{
    private readonly PluginManifest _manifest;
    private readonly IClipDeskPluginModuleV3 _module;
    private readonly IPluginCapabilityProvider _capabilities;
    private readonly IPluginExecutionScheduler _scheduler;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _commandTimeout;
    private CancellationTokenSource? _running;
    private bool _stopping;
    private bool _initialized;

    public PluginModuleSession(PluginManifest manifest, IClipDeskPluginModuleV3 module,
        IPluginCapabilityProvider capabilities, IPluginExecutionScheduler scheduler,
        PluginState? restoredState = null, TimeSpan? commandTimeout = null)
    {
        var issues = PluginManifestValidator.Validate(manifest);
        if (manifest.ManifestVersion != 3 || issues.Count > 0)
            throw new ArgumentException("Manifesto v3 inválido: " + string.Join("; ", issues.Select(x => x.Message)), nameof(manifest));
        if (!module.Id.Equals(manifest.Id, StringComparison.OrdinalIgnoreCase) || module.StateVersion != manifest.StateVersion)
            throw new ArgumentException("ID ou stateVersion do módulo não corresponde ao manifesto.", nameof(module));
        var manifestBound = new ManifestBoundCapabilityProvider(manifest, capabilities);
        if (!PluginCapabilityNegotiation.CanActivate(manifest, manifestBound))
            throw new InvalidOperationException("O host não oferece todas as capabilities obrigatórias do plugin.");
        _manifest = manifest;
        _module = module;
        _capabilities = manifestBound;
        _scheduler = scheduler;
        _commandTimeout = commandTimeout ?? TimeSpan.FromSeconds(30);
        if (_commandTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(commandTimeout));
        State = module.NormalizeState(restoredState ?? module.CreateDefaultState());
    }

    public PluginSessionStatus Status { get; private set; } = PluginSessionStatus.Created;
    public PluginState State { get; private set; }
    public PluginManifest Manifest => _manifest;

    public async ValueTask ActivateAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Status == PluginSessionStatus.Disposed) throw new ObjectDisposedException(nameof(PluginModuleSession));
            if (Status == PluginSessionStatus.Active) return;
            if (_module is IClipDeskPluginLifecycle lifecycle)
            {
                if (!_initialized)
                {
                    await lifecycle.InitializeAsync(_capabilities, cancellationToken);
                    _initialized = true;
                }
                if (Status == PluginSessionStatus.Suspended) await lifecycle.ResumeAsync(cancellationToken);
                else await lifecycle.ActivateAsync(cancellationToken);
            }
            Status = PluginSessionStatus.Active;
            Volatile.Write(ref _stopping, false);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask<PluginCommandResult> ExecuteAsync(PluginCommand command, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Status != PluginSessionStatus.Active)
                return new(State, PluginCommandStatus.Unavailable, "O plugin não está ativo.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Volatile.Write(ref _running, timeout);
            timeout.CancelAfter(_commandTimeout);
            try
            {
                var result = await _scheduler.RunAsync(token => _module.ExecuteAsync(State, command, _capabilities, token), timeout.Token)
                    .AsTask().WaitAsync(timeout.Token);
                if (result.Succeeded) State = _module.NormalizeState(result.State);
                return result;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Volatile.Read(ref _stopping)
                    ? new(State, PluginCommandStatus.Unavailable, "O plugin foi suspenso ou encerrado.")
                    : new(State, PluginCommandStatus.Failed, "O comando excedeu o limite de tempo do host.");
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                return new(State, PluginCommandStatus.Failed, "O plugin falhou ao executar o comando.");
            }
            finally { Volatile.Write(ref _running, null); }
        }
        finally { _gate.Release(); }
    }

    public async ValueTask SuspendAsync(CancellationToken cancellationToken = default)
    {
        Volatile.Write(ref _stopping, true);
        try { Volatile.Read(ref _running)?.Cancel(); } catch (ObjectDisposedException) { }
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Status != PluginSessionStatus.Active) return;
            if (_module is IClipDeskPluginLifecycle lifecycle)
            {
                await lifecycle.SuspendAsync(cancellationToken);
                State = _module.NormalizeState(await lifecycle.SaveStateAsync(cancellationToken));
            }
            Status = PluginSessionStatus.Suspended;
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        Volatile.Write(ref _stopping, true);
        try { Volatile.Read(ref _running)?.Cancel(); } catch (ObjectDisposedException) { }
        await _gate.WaitAsync();
        try
        {
            if (Status == PluginSessionStatus.Disposed) return;
            try
            {
                if (_module is IClipDeskPluginLifecycle lifecycle) await lifecycle.DisposeAsync();
            }
            finally { Status = PluginSessionStatus.Disposed; }
        }
        finally { _gate.Release(); }
    }
}
