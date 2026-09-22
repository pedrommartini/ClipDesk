using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ClipDesk.Core;
using ClipDesk.Models;
using Supabase.Realtime.Interfaces;
using Supabase.Realtime.Models;
using Supabase.Realtime.PostgresChanges;
using Supabase.Realtime.Channel;

namespace ClipDesk.Services;

internal interface ICloudSyncBackend : IAsyncDisposable
{
    bool RealtimeConnected { get; }
    CloudUser? User { get; }
    bool Connected { get; }
    bool SyncHistory { get; set; }
    string Status { get; }
    event Action? Changed;
    event Action? RemoteChanged;
    event Action? InvitationsChanged;
    event Action<CloudPresence>? PresenceReceived;
    event Action<CloudEntity>? RealtimeEntityReceived;
    void LoadPaths();
    void ObservePresentation(List<WorkspaceBoard> boards, IEnumerable<ClipboardHistoryEntry> history);
    Task SignInAsync(CancellationToken cancellation);
    Task SetUsernameAsync(string username);
    Task ConnectLiveAsync();
    Task PreparePresenceAsync(string workspaceId);
    Task PresenceAsync(PresenceMessage presence);
    Task SignOutAsync();
    Task SynchronizeAsync(List<WorkspaceBoard> boards, IEnumerable<ClipboardHistoryEntry> history);
    void Stage(List<WorkspaceBoard> boards, List<ClipboardHistoryEntry> history);
    void StageBoardObject(WorkspaceBoard board, BoardObject obj);
    Task SynchronizeBoardObjectsAsync(WorkspaceBoard board, IEnumerable<BoardObject> objects);
    (List<WorkspaceBoard> Boards, List<ClipboardHistoryEntry> History) Materialize();
    Task UploadDriveAsync(CloudAttachment attachment, WorkspaceBoard? board, IProgress<double>? progress = null);
    Task DownloadAsync(CloudAttachment attachment, CancellationToken cancellation = default, bool saveForUser = false);
    Task<List<CloudMember>> MembersAsync(WorkspaceBoard board);
    Task InviteAsync(WorkspaceBoard board, string username);
    Task RemoveMemberAsync(WorkspaceBoard board, CloudMember member);
    Task DeleteWorkspaceAsync(WorkspaceBoard board);
    Task<List<CloudInvitation>> InvitationsAsync();
    Task AcceptInviteAsync(string id);
    Task DeclineInviteAsync(string id);
    Task MakeLocalAsync(WorkspaceBoard board);
    Task MakePersonalAsync(WorkspaceBoard board);
}

/// <summary>
/// Stable application-facing cloud client. Parameterized construction remains
/// reserved for the self-hosted test server; regular app sessions use Supabase.
/// </summary>
public sealed class CloudSyncService : IAsyncDisposable
{
    private readonly ICloudSyncBackend _backend;
    public CloudSyncService(StorageService storage, CloudConfiguration? configuration = null, SavedCloudAccount? account = null, HttpClient? http = null, bool enableRealtime = true)
        => _backend = configuration is not null || account is not null || http is not null
            ? new LegacyCloudSyncService(storage, configuration, account, http, enableRealtime)
            : new SupabaseCloudSyncService(storage);
    public bool RealtimeConnected => _backend.RealtimeConnected;
    public CloudUser? User => _backend.User;
    public bool Connected => _backend.Connected;
    public bool SyncHistory { get => _backend.SyncHistory; set => _backend.SyncHistory = value; }
    public string Status => _backend.Status;
    public event Action? Changed { add => _backend.Changed += value; remove => _backend.Changed -= value; }
    public event Action? RemoteChanged { add => _backend.RemoteChanged += value; remove => _backend.RemoteChanged -= value; }
    public event Action? InvitationsChanged { add => _backend.InvitationsChanged += value; remove => _backend.InvitationsChanged -= value; }
    public event Action<CloudPresence>? PresenceReceived { add => _backend.PresenceReceived += value; remove => _backend.PresenceReceived -= value; }
    public event Action<CloudEntity>? RealtimeEntityReceived { add => _backend.RealtimeEntityReceived += value; remove => _backend.RealtimeEntityReceived -= value; }
    public void LoadPaths() => _backend.LoadPaths();
    public void ObservePresentation(List<WorkspaceBoard> boards, IEnumerable<ClipboardHistoryEntry> history) => _backend.ObservePresentation(boards, history);
    public Task SignInAsync(CancellationToken cancellation) => _backend.SignInAsync(cancellation);
    public Task SetUsernameAsync(string username) => _backend.SetUsernameAsync(username);
    public Task ConnectLiveAsync() => _backend.ConnectLiveAsync();
    public Task PreparePresenceAsync(string workspaceId) => _backend.PreparePresenceAsync(workspaceId);
    public Task PresenceAsync(PresenceMessage presence) => _backend.PresenceAsync(presence);
    public Task SignOutAsync() => _backend.SignOutAsync();
    public Task SynchronizeAsync(List<WorkspaceBoard> boards, IEnumerable<ClipboardHistoryEntry> history) => _backend.SynchronizeAsync(boards, history);
    public void Stage(List<WorkspaceBoard> boards, List<ClipboardHistoryEntry> history) => _backend.Stage(boards, history);
    public void StageBoardObject(WorkspaceBoard board, BoardObject obj) => _backend.StageBoardObject(board, obj);
    public Task SynchronizeBoardObjectsAsync(WorkspaceBoard board, IEnumerable<BoardObject> objects) => _backend.SynchronizeBoardObjectsAsync(board, objects);
    public (List<WorkspaceBoard> Boards, List<ClipboardHistoryEntry> History) Materialize() => _backend.Materialize();
    public Task UploadDriveAsync(CloudAttachment attachment, WorkspaceBoard? board, IProgress<double>? progress = null) => _backend.UploadDriveAsync(attachment, board, progress);
    public Task DownloadAsync(CloudAttachment attachment, CancellationToken cancellation = default, bool saveForUser = false) => _backend.DownloadAsync(attachment, cancellation, saveForUser);
    public Task<List<CloudMember>> MembersAsync(WorkspaceBoard board) => _backend.MembersAsync(board);
    public Task InviteAsync(WorkspaceBoard board, string username) => _backend.InviteAsync(board, username);
    public Task RemoveMemberAsync(WorkspaceBoard board, CloudMember member) => _backend.RemoveMemberAsync(board, member);
    public Task DeleteWorkspaceAsync(WorkspaceBoard board) => _backend.DeleteWorkspaceAsync(board);
    public Task<List<CloudInvitation>> InvitationsAsync() => _backend.InvitationsAsync();
    public Task AcceptInviteAsync(string id) => _backend.AcceptInviteAsync(id);
    public Task DeclineInviteAsync(string id) => _backend.DeclineInviteAsync(id);
    public Task MakeLocalAsync(WorkspaceBoard board) => _backend.MakeLocalAsync(board);
    public Task MakePersonalAsync(WorkspaceBoard board) => _backend.MakePersonalAsync(board);
    public ValueTask DisposeAsync() => _backend.DisposeAsync();
}

internal sealed class SupabaseCloudSyncService : ICloudSyncBackend
{
    private readonly StorageService _storage;
    private readonly SupabaseConfiguration _configuration;
    private readonly SupabaseConnection _connection;
    private readonly SupabaseRestClient _rest;
    private readonly GoogleConnection _google;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<string> _inaccessible = [];
    private Dictionary<string, CloudEntity> _presented = [];
    private SupabaseCloudSession? _session;
    private SavedDriveAccount? _driveAccount;
    private CloudUser? _user;
    private Task? _restore;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private readonly SemaphoreSlim _liveGate = new(1, 1);
    private IRealtimeChannel? _liveChannel;
    private IRealtimeBroadcast? _liveBroadcast;
    private readonly SemaphoreSlim _presenceChannelGate = new(1, 1);
    private readonly Dictionary<string, IRealtimeChannel> _presenceChannels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IRealtimeBroadcast> _presenceBroadcasts = new(StringComparer.Ordinal);
    private bool _liveConnected;
    private bool _disposed;
    private volatile bool _remoteDirty = true;
    private DateTimeOffset _lastPull;
    private int _presenceRefreshQueued;
    private int _presencePersistQueued;
    private DateTimeOffset _lastPresencePersisted;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _livePresenceSeen = new();
    private const string AssetBucket = "clipdesk-assets";

    public SupabaseCloudSyncService(StorageService storage, SupabaseConfiguration? configuration = null)
    {
        _storage = storage;
        _configuration = configuration ?? SupabaseConfiguration.Load();
        _connection = new SupabaseConnection(_configuration);
        _rest = new SupabaseRestClient(_configuration);
        _google = new GoogleConnection(new CloudConfiguration());
        _driveAccount = SecureDriveAccountFile.Load();
        LoadPresentation();
        var saved = SecureSupabaseSessionFile.Load();
        if (saved is not null) _restore = RestoreAsync(saved);
    }

    public bool RealtimeConnected => _liveConnected;
    public CloudUser? User => _user;
    public bool Connected => _session is not null && _user is not null;
    // Clipboard history has never been safe to replicate into a shared account
    // by default; the final cloud scopes synchronization to explicitly shared
    // boards. This setting remains for UI compatibility.
    public bool SyncHistory { get; set; }
    public string Status { get; private set; } = "Salvo neste computador";
    public event Action? Changed;
    public event Action? RemoteChanged;
    public event Action? InvitationsChanged;
    public event Action<CloudPresence>? PresenceReceived;
    public event Action<CloudEntity>? RealtimeEntityReceived;

    private void SetStatus(string status) { if (Status == status) return; Status = status; Changed?.Invoke(); }
    private async Task RestoreAsync(SupabaseCloudSession saved)
    {
        try
        {
            _session = await _connection.RestoreAsync(saved);
            _rest.SetSession(_session.AccessToken);
            _user = await ReadCurrentUserAsync(_lifetime.Token);
            SetStatus("Sincronizado");
        }
        catch (Supabase.Gotrue.Exceptions.GotrueException)
        {
            SecureSupabaseSessionFile.Clear(); _session = null; _user = null; _rest.ClearSession();
            SetStatus("Reconecte sua conta Google");
        }
        catch (Exception e) when (e is HttpRequestException or InvalidOperationException)
        {
            // A temporary network failure must not erase a usable saved login.
            _session = null; _user = null; _rest.ClearSession();
            SetStatus("Salvo localmente · aguardando conexão");
            _ = RetryRestoreAsync(saved);
        }
    }
    private async Task RetryRestoreAsync(SupabaseCloudSession saved)
    {
        var delays = new[]
        {
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(16)
        };
        foreach (var delay in delays)
        {
            try { await Task.Delay(delay, _lifetime.Token); }
            catch (OperationCanceledException) { return; }
            if (_disposed || Connected) return;
            try
            {
                // RestoreAsync persists every rotated refresh token. Reload it
                // before retrying so a successful auth followed by a temporary
                // profile request failure never reuses the older token.
                var current = SecureSupabaseSessionFile.Load() ?? saved;
                _session = await _connection.RestoreAsync(current);
                _rest.SetSession(_session.AccessToken);
                _user = await ReadCurrentUserAsync(_lifetime.Token);
                SetStatus("Sincronizado");
                Changed?.Invoke();
                return;
            }
            catch (Supabase.Gotrue.Exceptions.GotrueException)
            {
                SecureSupabaseSessionFile.Clear(); _session = null; _user = null; _rest.ClearSession();
                SetStatus("Reconecte sua conta Google");
                return;
            }
            catch (Exception e) when (e is HttpRequestException or InvalidOperationException)
            {
                _session = null; _user = null; _rest.ClearSession();
                SetStatus("Salvo localmente · aguardando conexão");
            }
        }
    }
    private async Task EnsureReadyAsync(CancellationToken cancellation)
    {
        if (_restore is not null) await _restore.WaitAsync(cancellation);
        if (!Connected) throw new InvalidOperationException("Entre com Google para usar a nuvem.");
        if (_session!.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2)) return;
        await _sessionGate.WaitAsync(cancellation);
        try
        {
            if (_session!.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(2))
            {
                try
                {
                    _session = await _connection.RefreshSessionAsync(_session);
                    _rest.SetSession(_session.AccessToken);
                    if (_liveConnected)
                        (await _connection.ClientAsync()).Realtime.SetAuth(_session.AccessToken);
                }
                catch (Supabase.Gotrue.Exceptions.GotrueException e)
                {
                    SecureSupabaseSessionFile.Clear(); _session = null; _user = null; _rest.ClearSession();
                    SetStatus("Reconecte sua conta Google");
                    throw new InvalidOperationException("Sua sessão expirou. Entre com Google novamente.", e);
                }
            }
        }
        finally { _sessionGate.Release(); }
    }
    public void LoadPaths() => LoadPresentation();
    private void LoadPresentation()
    {
        var saved = _storage.Database.Read("supabase-sync-presentation");
        _presented = saved is null ? [] : CloudRules.Deserialize<List<CloudEntity>>(saved).ToDictionary(x => x.Id);
    }
    public void ObservePresentation(List<WorkspaceBoard> boards, IEnumerable<ClipboardHistoryEntry> history)
    {
        if (User is null) return;
        _presented = CloudProjection.Project(boards, [], User.Id, false).ToDictionary(x => x.Id);
        _storage.Database.Write("supabase-sync-presentation", CloudRules.Serialize(_presented.Values.ToList()));
    }

    public async Task SignInAsync(CancellationToken cancellation)
    {
        SetStatus("Conectando ao Google…");
        try
        {
            _session = await _connection.SignInWithGoogleAsync(cancellation);
            _rest.SetSession(_session.AccessToken);
            _user = await ReadCurrentUserAsync(cancellation);
            SetStatus("Google conectado · nuvem pronta");
        }
        catch { SetStatus("Conexão não concluída"); throw; }
    }
    private async Task<CloudUser> ReadCurrentUserAsync(CancellationToken cancellation)
    {
        if (_session is null) throw new InvalidOperationException("Sessão inválida.");
        var id = _session.UserId;
        var profiles = await _rest.GetAsync<ProfileRow>($"profiles?select=id,username,display_name,picture_url&id=eq.{id}", cancellation);
        var profile = profiles.FirstOrDefault();
        return new CloudUser(id, profile?.Username, profile?.DisplayName ?? _session.DisplayName, profile?.PictureUrl ?? _session.PictureUrl);
    }
    public async Task SetUsernameAsync(string username)
    {
        await EnsureReadyAsync(_lifetime.Token);
        username = username.Trim().TrimStart('@').ToLowerInvariant();
        if (!CloudRules.IsUsernameValid(username)) throw new InvalidOperationException("Use 3–24 letras minúsculas, números ou _.");
        var updated = await _rest.PatchAsync<ProfileRow>($"profiles?id=eq.{_session!.UserId}", new { username }, _lifetime.Token);
        if (updated.Count != 1) throw new InvalidOperationException("Não foi possível salvar o username.");
        _user = _user! with { Username = username };
        Changed?.Invoke();
    }
    public async Task ConnectLiveAsync()
    {
        await EnsureReadyAsync(_lifetime.Token);
        if (_liveConnected || !await _liveGate.WaitAsync(0, _lifetime.Token)) return;
        try
        {
            if (_liveConnected) return;
            var client = await _connection.ClientAsync();
            client.Realtime.SetAuth(_session!.AccessToken);
            await client.Realtime.ConnectAsync();
            var channel = client.Realtime.Channel("clipdesk-cloud");
            var broadcast = channel.Register<BaseBroadcast>(broadcastSelf: false, broadcastAck: false);
            broadcast.AddBroadcastEventHandler((source, message) =>
            {
                if (message?.Payload is null) return;
                if (string.Equals(message.Event, "entity", StringComparison.Ordinal)
                    && PayloadText(message.Payload, "id") is { } value && Guid.TryParse(value, out var entityId))
                {
                    _ = ReceiveRealtimeEntityAsync(entityId.ToString("D"));
                    return;
                }
            });
            IRealtimeChannel.PostgresChangesHandler changed = (_, _) =>
            {
                _remoteDirty = true;
                RemoteChanged?.Invoke();
            };
            IRealtimeChannel.PostgresChangesHandler entityChanged = (source, response) =>
            {
                try
                {
                    var raw = response.Payload?.Data?.Record;
                    if (raw is null) throw new JsonException("Evento sem registro.");
                    var record = JsonSerializer.SerializeToElement(raw);
                    if (record.ValueKind == JsonValueKind.Object && record.TryGetProperty("id", out var id) && Guid.TryParse(id.GetString(), out var parsed))
                    {
                        // Postgres Changes already carries the committed row. Apply it
                        // directly so collaborators do not wait for a second REST
                        // round trip. Incomplete records still use the guarded fetch.
                        var row = JsonSerializer.Deserialize<BoardEntityRow>(record.GetRawText(), CloudRules.Json);
                        if (row is not null && !string.IsNullOrWhiteSpace(row.WorkspaceId)
                            && !string.IsNullOrWhiteSpace(row.Kind) && row.Data is not null)
                            _ = ApplyRealtimeEntityAsync(Entity(row));
                        else
                            _ = ReceiveRealtimeEntityAsync(parsed.ToString("D"));
                        return;
                    }
                }
                catch (Exception) { /* A full pull handles an event without a usable row id. */ }
                _remoteDirty = true;
                RemoteChanged?.Invoke();
            };
            IRealtimeChannel.PostgresChangesHandler presenceChanged = (_, _) => _ = ReceivePresenceAsync();
            IRealtimeChannel.PostgresChangesHandler invitationChanged = (_, _) => InvitationsChanged?.Invoke();
            channel.OnPostgresChange(changed, PostgresChangesOptions.ListenType.All, new PostgresChangesFilter { Schema = "public", Table = "workspaces" });
            channel.OnPostgresChange(entityChanged, PostgresChangesOptions.ListenType.All, new PostgresChangesFilter { Schema = "public", Table = "board_entities" });
            channel.OnPostgresChange(presenceChanged, PostgresChangesOptions.ListenType.All, new PostgresChangesFilter { Schema = "public", Table = "workspace_presence" });
            channel.OnPostgresChange(invitationChanged, PostgresChangesOptions.ListenType.All, new PostgresChangesFilter { Schema = "public", Table = "workspace_invitations" });
            await channel.Subscribe(8_000);
            _liveChannel = channel; _liveBroadcast = broadcast; _liveConnected = true; Changed?.Invoke();
        }
        catch
        {
            _liveConnected = false;
            // Normal synchronization remains available if a network blocks WebSocket.
        }
        finally { _liveGate.Release(); }
    }
    private async Task BroadcastEntityAsync(string id)
    {
        try
        {
            if (_liveConnected && _liveBroadcast is not null)
                await _liveBroadcast.Send("entity", new BaseBroadcast
                {
                    Event = "entity",
                    Payload = new Dictionary<string, object> { ["id"] = id }
                }, 2_000);
        }
        catch { /* Postgres Changes and the polling fallback still deliver it. */ }
    }
    private async Task ReceiveRealtimeEntityAsync(string id)
    {
        try
        {
            await EnsureReadyAsync(_lifetime.Token);
            // Fetch the latest authorized row rather than the entire board. This
            // also handles several edits arriving while an earlier event is in flight.
            var rows = await _rest.GetAsync<BoardEntityRow>($"board_entities?select=*&id=eq.{id}", _lifetime.Token);
            if (rows.Count != 1) { _remoteDirty = true; RemoteChanged?.Invoke(); return; }
            await ApplyRealtimeEntityAsync(Entity(rows[0]));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception) { _remoteDirty = true; RemoteChanged?.Invoke(); }
    }

    private async Task ApplyRealtimeEntityAsync(CloudEntity entity)
    {
        await _gate.WaitAsync(_lifetime.Token);
        try
        {
            if (!_storage.Database.Accept(entity)) return;
            if (entity.Kind == "boardObject") RealtimeEntityReceived?.Invoke(_storage.Database.Entities(entity.Id)[0]);
            else RemoteChanged?.Invoke();
        }
        finally { _gate.Release(); }
    }
    private async Task ReceivePresenceAsync()
    {
        if (_disposed || !Connected || Interlocked.Exchange(ref _presenceRefreshQueued, 1) != 0) return;
        try
        {
            await Task.Delay(40, _lifetime.Token);
            await EnsureReadyAsync(_lifetime.Token);
            var presence = await _rest.GetAsync<PresenceRow>("workspace_presence?select=workspace_id,user_id,x,y,item_id,view_center_x,view_center_y,zoom,profiles(username,picture_url)", _lifetime.Token);
            foreach (var row in presence.Where(x => x.UserId != _session!.UserId))
            {
                if (_livePresenceSeen.TryGetValue(row.UserId, out var liveSeen)
                    && DateTimeOffset.UtcNow - liveSeen < TimeSpan.FromSeconds(3)) continue;
                PresenceReceived?.Invoke(new CloudPresence(row.UserId, row.Profile?.Username ?? "usuário", row.Profile?.PictureUrl, CloudRules.CanonicalGuidId(row.WorkspaceId), row.X, row.Y, row.ItemId is null ? null : CloudRules.CanonicalGuidId(row.ItemId), row.ViewCenterX, row.ViewCenterY, row.Zoom));
            }
        }
        catch (Exception e) when (e is HttpRequestException or InvalidOperationException or OperationCanceledException) { }
        finally { Interlocked.Exchange(ref _presenceRefreshQueued, 0); }
    }

    public async Task PresenceAsync(PresenceMessage presence)
    {
        await EnsureReadyAsync(_lifetime.Token);
        var livePresence = new CloudPresence(_session!.UserId, User?.Username ?? "usuário", User?.Picture,
            presence.WorkspaceId, presence.X, presence.Y, presence.ItemId,
            presence.ViewCenterX, presence.ViewCenterY, presence.Zoom, presence.Drags);
        var broadcastSent = false;
        try
        {
            if (_liveConnected)
            {
                var privateBroadcast = await PresenceBroadcastAsync(presence.WorkspaceId);
                await privateBroadcast.Send("presence", new BaseBroadcast
                {
                    Event = "presence",
                    Payload = PresencePayload(livePresence)
                }, 500);
                broadcastSent = true;
                MotionDiagnostics.Record(MotionDiagnostics.Stage.Sent);
            }
        }
        catch { MotionDiagnostics.Record(MotionDiagnostics.Stage.SendFailed); /* The durable presence row remains the fallback. */ }

        var persistDue = DateTimeOffset.UtcNow - _lastPresencePersisted >= TimeSpan.FromSeconds(2);
        if (!broadcastSent) await PersistPresenceAsync(presence);
        else if (persistDue) _ = PersistPresenceAsync(presence);
    }

    public async Task PreparePresenceAsync(string workspaceId)
    {
        await EnsureReadyAsync(_lifetime.Token);
        if (_liveConnected) _ = await PresenceBroadcastAsync(workspaceId);
    }

    private async Task<IRealtimeBroadcast> PresenceBroadcastAsync(string workspaceId)
    {
        workspaceId = CloudRules.CanonicalGuidId(workspaceId);
        if (_presenceBroadcasts.TryGetValue(workspaceId, out var existing)) return existing;
        await _presenceChannelGate.WaitAsync(_lifetime.Token);
        try
        {
            if (_presenceBroadcasts.TryGetValue(workspaceId, out existing)) return existing;
            var client = await _connection.ClientAsync();
            var options = ChannelOptions.Private(client.Realtime.Options,
                () => _session?.AccessToken ?? "", client.Realtime.SerializerSettings);
            var channel = client.Realtime.Channel("workspace:" + workspaceId, options);
            var broadcast = channel.Register<BaseBroadcast>(broadcastSelf: false, broadcastAck: false);
            broadcast.AddBroadcastEventHandler((_, message) =>
            {
                if (message?.Payload is null || !string.Equals(message.Event, "presence", StringComparison.Ordinal)) return;
                if (!TryPresence(message.Payload, out var incoming))
                {
                    MotionDiagnostics.Record(MotionDiagnostics.Stage.Rejected);
                    return;
                }
                if (incoming.UserId == _session?.UserId) return;
                MotionDiagnostics.Record(MotionDiagnostics.Stage.Received);
                _livePresenceSeen[incoming.UserId] = DateTimeOffset.UtcNow;
                PresenceReceived?.Invoke(incoming);
            });
            await channel.Subscribe(8_000);
            _presenceChannels[workspaceId] = channel;
            _presenceBroadcasts[workspaceId] = broadcast;
            return broadcast;
        }
        finally { _presenceChannelGate.Release(); }
    }

    private async Task PersistPresenceAsync(PresenceMessage presence)
    {
        if (Interlocked.Exchange(ref _presencePersistQueued, 1) != 0) return;
        try
        {
            await EnsureReadyAsync(_lifetime.Token);
            await _rest.UpsertAsync("workspace_presence?on_conflict=workspace_id,user_id", new
            {
                workspace_id = presence.WorkspaceId, user_id = _session!.UserId, x = presence.X, y = presence.Y,
                item_id = string.IsNullOrWhiteSpace(presence.ItemId) ? null : presence.ItemId,
                view_center_x = presence.ViewCenterX, view_center_y = presence.ViewCenterY, zoom = presence.Zoom
            }, _lifetime.Token);
            _lastPresencePersisted = DateTimeOffset.UtcNow;
        }
        catch (Exception e) when (e is HttpRequestException or InvalidOperationException or OperationCanceledException) { }
        finally { Interlocked.Exchange(ref _presencePersistQueued, 0); }
    }
    public async Task SignOutAsync()
    {
        _session = null; _user = null; _rest.ClearSession();
        _driveAccount = null; SecureDriveAccountFile.Clear();
        await _connection.SignOutAsync(); SetStatus("Salvo neste computador"); Changed?.Invoke();
    }

    public async Task SynchronizeAsync(List<WorkspaceBoard> boards, IEnumerable<ClipboardHistoryEntry> history)
    {
        if (!Connected) return;
        try
        {
            await EnsureReadyAsync(_lifetime.Token); SetStatus("Sincronizando…");
            await ConnectLiveAsync();
            await _gate.WaitAsync(_lifetime.Token);
            try
            {
                // A brand-new cloud workspace needs its owner membership before
                // Storage RLS can authorize the first attachment. Existing
                // workspaces skip this preliminary push, keeping new images to
                // one entity write and one realtime notification.
                var needsWorkspace = boards.Where(board => board.SyncMode != WorkspaceSyncMode.Local)
                    .Any(board => !_storage.Database.Entities(board.Id.ToString("N"))
                        .Any(entity => entity.Kind == "workspace" && entity.Version > 0 && !entity.Deleted));
                if (needsWorkspace) { Stage(boards, []); await PushAsync(_lifetime.Token); }
                await PrepareAttachmentsAsync(boards, _lifetime.Token);
                Stage(boards, []); await PushAsync(_lifetime.Token);
            }
            finally { _gate.Release(); }
            // Realtime fetches changed entities directly. Poll frequently only
            // when the live channel is unavailable.
            var fallback = _liveConnected ? TimeSpan.FromSeconds(12) : TimeSpan.FromSeconds(2);
            if (_remoteDirty || DateTimeOffset.UtcNow - _lastPull > fallback)
            {
                _remoteDirty = false;
                try
                {
                    // Network reads do not hold the write gate. An edit made
                    // during this read can be sent immediately by the fast path.
                    var remote = await FetchRemoteAsync(_lifetime.Token);
                    await _gate.WaitAsync(_lifetime.Token);
                    try { ApplyRemote(remote); _lastPull = DateTimeOffset.UtcNow; }
                    finally { _gate.Release(); }
                }
                catch { _remoteDirty = true; throw; }
            }
            await RestoreSupabaseAttachmentsAsync(_lifetime.Token);
            SetStatus(_storage.Database.ConflictCount > 0 ? "Conflito preservado para revisão" : _storage.Database.Pending().Count > 0 ? "Sincronização pendente" : "Sincronizado");
        }
        catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        { SetStatus("Salvo localmente · aguardando limite da nuvem"); throw; }
        catch (HttpRequestException) { SetStatus("Salvo localmente · aguardando conexão"); throw; }
    }
    public void Stage(List<WorkspaceBoard> boards, List<ClipboardHistoryEntry> history)
    {
        if (User is null) return;
        var projected = CloudProjection.Project(boards.Where(x => !_inaccessible.Contains(x.Id.ToString("N"))), [], User.Id, false).ToList();
        var ids = projected.Select(x => x.Id).ToHashSet();
        var active = boards.Where(x => x.SyncMode != WorkspaceSyncMode.Local).Select(x => x.Id.ToString("N")).ToHashSet();
        StageDerived(_presented, projected);
        foreach (var old in _presented.Values.Where(x => !x.Deleted && !ids.Contains(x.Id)))
            if ((old.Kind is "item" or "boardObject") && old.WorkspaceId is not null && active.Contains(old.WorkspaceId))
                if (_storage.Database.Entities(old.Id).FirstOrDefault() is { } latest) _storage.Database.Stage(latest with { Deleted = true });
        _presented = projected.ToDictionary(x => x.Id);
        _storage.Database.Write("supabase-sync-presentation", CloudRules.Serialize(projected));
    }
    private void StageDerived(IReadOnlyDictionary<string, CloudEntity> before, IEnumerable<CloudEntity> after)
    {
        var current = _storage.Database.Entities().ToDictionary(x => x.Id);
        foreach (var entity in after)
        {
            if (entity.Kind == "workspace" && entity.Data["ownerId"]?.GetValue<string>() != User?.Id) continue;
            if (!current.TryGetValue(entity.Id, out var latest)) { _storage.Database.Stage(entity); continue; }
            if (latest.Deleted || _inaccessible.Contains(entity.WorkspaceId ?? entity.Id)) continue;
            var basis = before.GetValueOrDefault(entity.Id)?.Data ?? entity.Data;
            try
            {
                var merged = CloudRules.Merge(latest.Data, basis, entity.Data);
                if (!CloudRules.Same(latest.Data, merged) || latest.Deleted != entity.Deleted) _storage.Database.Stage(entity with { Data = merged });
            }
            catch (SyncConflictException) { _storage.Database.PreserveConflict(entity.Id, entity.Data); }
        }
    }
    public void StageBoardObject(WorkspaceBoard board, BoardObject obj)
    {
        if (User is null || board.SyncMode == WorkspaceSyncMode.Local || _inaccessible.Contains(board.Id.ToString("N"))) return;
        var projected = CloudProjection.ProjectBoardObject(obj, board.Id.ToString("N"));
        var latest = _storage.Database.Entities(projected.Id).FirstOrDefault();
        if (latest is null) _storage.Database.Stage(projected);
        else if (!latest.Deleted)
        {
            var basis = _presented.GetValueOrDefault(projected.Id)?.Data ?? latest.Data;
            try { var merged = CloudRules.Merge(latest.Data, basis, projected.Data); if (!CloudRules.Same(latest.Data, merged)) _storage.Database.Stage(projected with { Data = merged }); }
            catch (SyncConflictException) { _storage.Database.PreserveConflict(projected.Id, projected.Data); }
        }
        _presented[projected.Id] = projected;
        _storage.Database.Write("supabase-sync-presentation", CloudRules.Serialize(_presented.Values.ToList()));
    }
    public async Task SynchronizeBoardObjectsAsync(WorkspaceBoard board, IEnumerable<BoardObject> objects)
    {
        foreach (var item in objects.DistinctBy(x => x.Id)) StageBoardObject(board, item);
        if (!Connected) return;
        await _gate.WaitAsync(_lifetime.Token);
        try { await ConnectLiveAsync(); await PushAsync(_lifetime.Token); SetStatus(_storage.Database.Pending().Count > 0 ? "Sincronização pendente" : "Sincronizado"); }
        finally { _gate.Release(); }
    }

    private async Task PushAsync(CancellationToken cancellation)
    {
        foreach (var operation in _storage.Database.Pending())
        {
            if (_inaccessible.Contains(operation.WorkspaceId ?? operation.EntityId)) continue;
            try
            {
                var remote = operation.Kind switch
                {
                    "workspace" => await WriteWorkspaceAsync(operation, cancellation),
                    "item" or "boardObject" => await WriteBoardEntityAsync(operation, cancellation),
                    _ => null
                };
                if (remote is not null)
                {
                    _storage.Database.Accept(remote, operation);
                    if (remote.Kind is "item" or "boardObject") _ = BroadcastEntityAsync(remote.Id);
                }
            }
            catch (SyncConflictException)
            { _storage.Database.PreserveConflict(operation.EntityId, operation.Data); }
        }
    }
    private async Task<CloudEntity> WriteWorkspaceAsync(SyncOperation op, CancellationToken cancellation)
    {
        var d = op.Data;
        List<WorkspaceRow> result;
        if (op.BaseVersion == 0)
        {
            var row = new { id = op.EntityId, owner_id = User!.Id, name = d["name"]?.GetValue<string>() ?? "Mesa", mode = d["mode"]?.GetValue<string>() ?? "personal", world_width = Number(d, "worldWidth", 4800), world_height = Number(d, "worldHeight", 3200), schema_version = Int(d, "schemaVersion", 1), deleted = op.Deleted };
            // Sharing a local board creates its workspace atomically on the
            // server.  Treat the first sync as idempotent so an already-created
            // row is returned instead of failing with a duplicate primary key.
            result = await _rest.UpsertAsync<WorkspaceRow>("workspaces?on_conflict=id", row, cancellation);
        }
        else
        {
            var row = new { name = d["name"]?.GetValue<string>() ?? "Mesa", mode = d["mode"]?.GetValue<string>() ?? "personal", world_width = Number(d, "worldWidth", 4800), world_height = Number(d, "worldHeight", 3200), schema_version = Int(d, "schemaVersion", 1), deleted = op.Deleted };
            result = await _rest.PatchAsync<WorkspaceRow>($"workspaces?id=eq.{op.EntityId}&version=eq.{op.BaseVersion}", row, cancellation);
        }
        if (result.Count != 1) throw new SyncConflictException("mesa");
        return Entity(result[0]);
    }
    private async Task<CloudEntity> WriteBoardEntityAsync(SyncOperation op, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(op.WorkspaceId)) throw new InvalidOperationException("Mesa inválida.");
        object row = new { id = op.EntityId, workspace_id = op.WorkspaceId, owner_id = User!.Id, kind = op.Kind, data = op.Data, deleted = op.Deleted };
        List<BoardEntityRow> result;
        if (op.BaseVersion == 0) result = await _rest.PostAsync<BoardEntityRow>("board_entities", row, cancellation);
        else result = await _rest.PatchAsync<BoardEntityRow>($"board_entities?id=eq.{op.EntityId}&version=eq.{op.BaseVersion}", row, cancellation);
        if (result.Count != 1) throw new SyncConflictException("elemento");
        return Entity(result[0]);
    }
    private async Task<List<CloudEntity>> FetchRemoteAsync(CancellationToken cancellation)
    {
        var workspaces = await _rest.GetAsync<WorkspaceRow>("workspaces?select=*&order=updated_at.asc", cancellation);
        var entities = await _rest.GetAsync<BoardEntityRow>("board_entities?select=*&order=updated_at.asc", cancellation);
        return workspaces.Select(Entity).Concat(entities.Select(Entity)).ToList();
    }
    private void ApplyRemote(List<CloudEntity> remote)
    {
        var changed = false;
        var remoteIds = remote.Select(entity => entity.Id).ToHashSet(StringComparer.Ordinal);
        // Earlier builds stored the hyphenated UUID returned by PostgREST next
        // to the compact local ID. Preserve any unsent edits before retiring
        // those aliases; the canonical remote row is pulled below.
        foreach (var alias in _storage.Database.Entities().Where(entity =>
                     entity.Id != CloudRules.CanonicalGuidId(entity.Id)
                     && remoteIds.Contains(CloudRules.CanonicalGuidId(entity.Id))))
        {
            if (_storage.Database.Pending(alias.Id).Count > 0)
                _storage.Database.PreserveConflict(alias.Id, alias.Data);
            _storage.Database.ForgetEntity(alias.Id);
            changed = true;
        }
        var available = remote.Where(x => x.Kind == "workspace" && !x.Deleted).Select(x => x.Id).ToHashSet();
        foreach (var old in _storage.Database.Entities().Where(x => x.Kind == "workspace" && x.Version > 0 && !available.Contains(x.Id)))
        { _inaccessible.Add(old.Id); _storage.Database.ForgetWorkspace(old.Id); changed = true; }
        foreach (var entity in remote)
        {
            if (entity.Kind == "workspace" && entity.Deleted) { if (_inaccessible.Add(entity.Id)) changed = true; _storage.Database.ForgetWorkspace(entity.Id); continue; }
            if (!_inaccessible.Contains(entity.WorkspaceId ?? entity.Id))
            {
                var pending = _storage.Database.Pending(entity.Id).FirstOrDefault();
                var alreadyPublished = pending is { BaseVersion: 0, Kind: "workspace" }
                    && pending.Data.All(field => field.Key == "createdAt" || CloudRules.Same(field.Value, entity.Data[field.Key]));
                if (_storage.Database.Accept(entity, alreadyPublished ? pending : null))
                {
                    changed = true;
                    if (entity.Kind == "boardObject" && _storage.Database.Entities(entity.Id).FirstOrDefault() is { } accepted)
                        RealtimeEntityReceived?.Invoke(accepted);
                }
            }
        }
        if (changed) RemoteChanged?.Invoke();
    }
    public (List<WorkspaceBoard> Boards, List<ClipboardHistoryEntry> History) Materialize()
    {
        var deviceItems = _storage.LoadWorkspaces()
            .SelectMany(board => CloudProjection.Flatten(board.Items))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        return (CloudProjection.Materialize(_storage.Database.Entities(), AttachmentLocalPaths(), deviceItems), []);
    }

    private async Task PrepareAttachmentsAsync(IEnumerable<WorkspaceBoard> boards, CancellationToken cancellation)
    {
        foreach (var board in boards.Where(x => x.SyncMode != WorkspaceSyncMode.Local))
        foreach (var item in CloudProjection.Flatten(board.Items))
        {
            if (item.Attachments.Count == 0)
            {
                var sources = new List<(string Path, string? Relative)>();
                foreach (var path in item.FilePaths.Concat(item.StoredFilePath is null ? [] : new[] { item.StoredFilePath }))
                {
                    if (File.Exists(path)) sources.Add((path, null));
                    else if (Directory.Exists(path))
                        sources.AddRange(Directory.EnumerateFiles(path, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
                            .Select(file => (file, (string?)Path.GetRelativePath(path, file))));
                }
                foreach (var source in sources)
                {
                    var attachment = await CreateManagedAttachmentAsync(source.Path, source.Relative, cancellation);
                    item.Attachments.Add(attachment);
                    if (!attachment.IsDrive) await UploadSupabaseAttachmentAsync(board, attachment, cancellation);
                }
            }
            foreach (var attachment in item.Attachments.Where(a => !a.IsDrive && !a.Uploaded && File.Exists(a.LocalPath)))
                await UploadSupabaseAttachmentAsync(board, attachment, cancellation);
        }
    }

    private async Task<CloudAttachment> CreateManagedAttachmentAsync(string source, string? relative, CancellationToken cancellation)
    {
        var id = Guid.NewGuid().ToString("N");
        var safeName = Path.GetFileName(source);
        if (string.IsNullOrWhiteSpace(safeName)) throw new IOException("Nome de arquivo inválido.");
        var directory = Path.Combine(_storage.AssetsDirectory, id); Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, safeName);
        if (!Path.GetFullPath(source).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            await using var input = File.OpenRead(source);
            await using var output = File.Create(target);
            await input.CopyToAsync(output, cancellation);
        }
        await using var file = File.OpenRead(target);
        var attachment = new CloudAttachment
        {
            Id = id, OwnerId = User!.Id, Name = safeName, Size = file.Length,
            Sha256 = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellation)),
            IsDrive = CloudRules.UsesDrive(file.Length), LocalPath = target, RelativePath = relative
        };
        return attachment;
    }

    private async Task UploadSupabaseAttachmentAsync(WorkspaceBoard board, CloudAttachment attachment, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(attachment.LocalPath) || !File.Exists(attachment.LocalPath))
            throw new IOException("O arquivo local não está mais disponível.");
        attachment.StoragePath ??= StorageObjectPath(board.Id, attachment);
        var client = await _connection.ClientAsync();
        await client.Storage.From(AssetBucket).UploadOrResume(attachment.LocalPath, attachment.StoragePath,
            new Supabase.Storage.FileOptions { Upsert = true, CacheControl = "31536000" }, cancellationToken: cancellation);
        attachment.Uploaded = true;
    }

    private async Task RestoreSupabaseAttachmentsAsync(CancellationToken cancellation)
    {
        foreach (var attachment in RemoteAttachments().Where(a => !a.IsDrive && a.Uploaded && !string.IsNullOrWhiteSpace(a.StoragePath)))
        {
            var path = ManagedAttachmentPath(attachment);
            if (await IsValidAttachmentAsync(path, attachment, cancellation)) continue;
            try { await DownloadAsync(attachment, cancellation); }
            catch (Exception e) when (e is IOException or HttpRequestException or Supabase.Storage.Exceptions.SupabaseStorageException) { }
        }
    }

    private Dictionary<string, string> AttachmentLocalPaths()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attachment in RemoteAttachments())
        {
            var path = ManagedAttachmentPath(attachment);
            if (File.Exists(path)) result[attachment.Id] = path;
        }
        return result;
    }

    private IEnumerable<CloudAttachment> RemoteAttachments()
    {
        foreach (var entity in _storage.Database.Entities().Where(e => !e.Deleted && e.Data["attachments"] is JsonArray))
            foreach (var attachment in CloudRules.Deserialize<List<CloudAttachment>>(entity.Data["attachments"]!.ToJsonString()))
                yield return attachment;
    }

    private string ManagedAttachmentPath(CloudAttachment attachment)
        => Path.Combine(_storage.AssetsDirectory, attachment.Id, Path.GetFileName(attachment.Name));

    private static string StorageObjectPath(Guid workspaceId, CloudAttachment attachment)
    {
        var extension = new string(Path.GetExtension(attachment.Name).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (extension.Length > 10) extension = extension[..10];
        return $"{workspaceId:N}/{attachment.Id}/{attachment.Sha256.ToLowerInvariant()}{(extension.Length == 0 ? "" : "." + extension)}";
    }

    private static async Task<bool> IsValidAttachmentAsync(string path, CloudAttachment attachment, CancellationToken cancellation)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != attachment.Size) return false;
        await using var file = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, cancellation)).Equals(attachment.Sha256, StringComparison.OrdinalIgnoreCase);
    }
    private async Task<SavedDriveAccount> EnsureDriveAccountAsync(CancellationToken cancellation)
    {
        if (User is null) throw new InvalidOperationException("Entre com Google para usar o Drive.");
        if (_driveAccount is not null && _driveAccount.UserId != User.Id)
        { _driveAccount = null; SecureDriveAccountFile.Clear(); }
        if (string.IsNullOrWhiteSpace(_session?.ProviderAccessToken)
            || _session.ProviderExpiresAt is null || _session.ProviderExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(1))
        {
            var expectedUser = User.Id;
            _session = await _connection.SignInWithGoogleAsync(cancellation, requestDrive: true);
            if (_session.UserId != expectedUser)
                throw new InvalidOperationException("Autorize o Drive com a mesma conta Google conectada ao ClipDesk.");
            _rest.SetSession(_session.AccessToken);
        }
        var tokens = new GoogleTokens(_session!.ProviderAccessToken!, _session.ProviderRefreshToken ?? "",
            _session.ProviderExpiresAt ?? DateTimeOffset.UtcNow.AddMinutes(50), "");
        if (_driveAccount is null)
        {
            var root = await _google.EnsureFolderAsync(tokens.AccessToken, "ClipDesk", "clipdesk-root", null, cancellation);
            _driveAccount = new SavedDriveAccount(User.Id, tokens, root, "supabase-google");
        }
        else _driveAccount = _driveAccount with { Google = tokens };
        SecureDriveAccountFile.Save(_driveAccount);
        return _driveAccount;
    }
    public async Task UploadDriveAsync(CloudAttachment attachment, WorkspaceBoard? board, IProgress<double>? progress = null)
    {
        await EnsureReadyAsync(_lifetime.Token);
        if (User is null || attachment.OwnerId != User.Id) throw new InvalidOperationException("Somente o dono pode enviar este arquivo.");
        if (!attachment.IsDrive) throw new InvalidOperationException("Este arquivo não precisa do Google Drive.");
        var account = await EnsureDriveAccountAsync(_lifetime.Token);
        var folder = await _google.EnsureFolderAsync(account.Google.AccessToken, board?.Name ?? "Arquivos", board?.Id.ToString("N") ?? "clipdesk-files", account.RootFolderId, _lifetime.Token);
        var fileId = await _google.UploadAsync(account.Google.AccessToken, attachment, folder, _storage.Database, progress, _lifetime.Token);
        await _google.GrantLinkAsync(account.Google.AccessToken, fileId, _lifetime.Token);
        attachment.DriveFileId = fileId; attachment.Uploaded = true;
    }
    public async Task DownloadAsync(CloudAttachment attachment, CancellationToken cancellation = default, bool saveForUser = false)
    {
        if (!attachment.Uploaded || (attachment.IsDrive ? string.IsNullOrWhiteSpace(attachment.DriveFileId) : string.IsNullOrWhiteSpace(attachment.StoragePath)))
            throw new IOException("O proprietário ainda não enviou o arquivo.");
        var directory = saveForUser ? StorageService.DownloadsDirectory : Path.Combine(_storage.AssetsDirectory, attachment.Id);
        Directory.CreateDirectory(directory);
        var safeName = Path.GetFileName(attachment.Name);
        if (string.IsNullOrWhiteSpace(safeName) || safeName != attachment.Name) throw new IOException("Nome de arquivo inválido.");
        var target = saveForUser ? AvailableDownloadPath(directory, safeName) : Path.Combine(directory, safeName);
        if (!saveForUser && await IsValidAttachmentAsync(target, attachment, cancellation)) { attachment.LocalPath = target; return; }
        var temporary = target + ".download";
        if (File.Exists(temporary)) File.Delete(temporary);
        if (attachment.IsDrive) await _google.DownloadPublicAsync(attachment.DriveFileId!, temporary, cancellation);
        else
        {
            var client = await _connection.ClientAsync();
            await client.Storage.From(AssetBucket).Download(attachment.StoragePath!, temporary,
                (EventHandler<float>?)null, cancellation, null);
        }
        await using (var file = File.OpenRead(temporary))
            if (file.Length != attachment.Size || !Convert.ToHexString(await SHA256.HashDataAsync(file, cancellation)).Equals(attachment.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("O arquivo baixado falhou na verificação de integridade.");
        File.Move(temporary, target, true); attachment.LocalPath = target;
    }
    private static string AvailableDownloadPath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate) && !File.Exists(candidate + ".download")) return candidate;
        var stem = Path.GetFileNameWithoutExtension(fileName); var extension = Path.GetExtension(fileName);
        for (var copy = 2; copy < 10_000; copy++)
        {
            candidate = Path.Combine(directory, $"{stem} ({copy}){extension}");
            if (!File.Exists(candidate) && !File.Exists(candidate + ".download")) return candidate;
        }
        throw new IOException("Não foi possível criar um nome disponível na pasta de downloads.");
    }

    public async Task<List<CloudMember>> MembersAsync(WorkspaceBoard board)
    {
        await EnsureReadyAsync(_lifetime.Token);
        var rows = await _rest.GetAsync<MemberRow>($"workspace_members?select=user_id,role,profiles(username,picture_url)&workspace_id=eq.{board.Id:N}", _lifetime.Token);
        return rows.Select(x => new CloudMember(x.UserId, x.Profile?.Username ?? "usuário", x.Profile?.PictureUrl, x.Role)).ToList();
    }
    public async Task InviteAsync(WorkspaceBoard board, string username)
    {
        await EnsureReadyAsync(_lifetime.Token);
        username = username.Trim().TrimStart('@').ToLowerInvariant();
        if (!CloudRules.IsUsernameValid(username)) throw new InvalidOperationException("Informe um @username válido.");
        await _rest.RpcAsync<object>("share_workspace_with_user", new
        {
            target_workspace = board.Id.ToString("N"),
            target_username = username,
            workspace_name = board.Name,
            workspace_world_width = board.WorldWidth,
            workspace_world_height = board.WorldHeight,
            workspace_schema_version = board.SchemaVersion
        }, _lifetime.Token);
        board.SyncMode = WorkspaceSyncMode.Shared;
        try
        {
            var remote = (await _rest.GetAsync<WorkspaceRow>($"workspaces?select=*&id=eq.{board.Id:N}", _lifetime.Token)).SingleOrDefault();
            if (remote is not null)
            {
                var sent = _storage.Database.Pending(board.Id.ToString("N")).FirstOrDefault();
                _storage.Database.Accept(Entity(remote), sent);
            }
        }
        catch (HttpRequestException) { /* The invitation committed; the next sync will fetch the workspace. */ }
    }
    public async Task RemoveMemberAsync(WorkspaceBoard board, CloudMember member)
    {
        await EnsureReadyAsync(_lifetime.Token);
        await _rest.RpcAsync<object>("remove_workspace_member", new { target_workspace = board.Id.ToString("N"), target_user = member.UserId }, _lifetime.Token);
    }
    public async Task DeleteWorkspaceAsync(WorkspaceBoard board)
    {
        await EnsureReadyAsync(_lifetime.Token);
        var row = await _rest.PatchAsync<WorkspaceRow>($"workspaces?id=eq.{board.Id:N}", new { deleted = true }, _lifetime.Token);
        if (row.Count != 1) throw new InvalidOperationException("Não foi possível excluir esta mesa.");
        _inaccessible.Add(board.Id.ToString("N")); _storage.Database.ForgetWorkspace(board.Id.ToString("N"));
    }
    public async Task<List<CloudInvitation>> InvitationsAsync()
    {
        await EnsureReadyAsync(_lifetime.Token);
        // Pending recipients are intentionally not workspace members yet, so
        // normal workspace SELECT policies hide the board until acceptance.
        // This narrow RPC returns only the invitation card's safe metadata.
        var rows = await _rest.RpcAsync<List<InvitationDetailsRow>>("list_pending_workspace_invitations", new { }, _lifetime.Token) ?? [];
        return rows.Select(x => new CloudInvitation(x.Id, CloudRules.CanonicalGuidId(x.WorkspaceId), x.WorkspaceName, x.FromUsername, x.FromDisplayName, x.FromPictureUrl)).ToList();
    }
    public async Task AcceptInviteAsync(string id)
    {
        await EnsureReadyAsync(_lifetime.Token);
        await _rest.RpcAsync<object>("accept_workspace_invitation", new { invitation_id = id }, _lifetime.Token);
        _remoteDirty = true;
        RemoteChanged?.Invoke();
    }
    public async Task DeclineInviteAsync(string id) { await EnsureReadyAsync(_lifetime.Token); await _rest.PatchAsync<InviteRow>($"workspace_invitations?id=eq.{id}", new { status = "declined" }, _lifetime.Token); }
    public async Task MakePersonalAsync(WorkspaceBoard board) { await EnsureReadyAsync(_lifetime.Token); await _rest.RpcAsync<object>("make_workspace_personal", new { target_workspace = board.Id.ToString("N") }, _lifetime.Token); board.SyncMode = WorkspaceSyncMode.PersonalCloud; }
    public async Task MakeLocalAsync(WorkspaceBoard board)
    {
        await DeleteWorkspaceAsync(board);
        board.Id = Guid.NewGuid(); board.OwnerId = null; board.SyncMode = WorkspaceSyncMode.Local;
        foreach (var item in CloudProjection.Flatten(board.Items)) { item.Id = Guid.NewGuid().ToString("N"); item.Attachments.Clear(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return; _disposed = true; _lifetime.Cancel();
        foreach (var channel in _presenceChannels.Values) channel.Unsubscribe();
        _liveChannel?.Unsubscribe(); _rest.Dispose(); await _connection.DisposeAsync(); _lifetime.Dispose();
        _gate.Dispose(); _sessionGate.Dispose(); _liveGate.Dispose(); _presenceChannelGate.Dispose();
    }
    private static double Number(JsonObject data, string name, double fallback) => data[name]?.GetValue<double>() ?? fallback;
    private static int Int(JsonObject data, string name, int fallback) => data[name]?.GetValue<int>() ?? fallback;
    private static Dictionary<string, object> PresencePayload(CloudPresence presence) => new()
    {
        ["userId"] = presence.UserId, ["username"] = presence.Username, ["picture"] = presence.Picture ?? "",
        ["workspaceId"] = presence.WorkspaceId, ["x"] = presence.X, ["y"] = presence.Y,
        ["itemId"] = presence.ItemId ?? "", ["viewCenterX"] = presence.ViewCenterX,
        ["viewCenterY"] = presence.ViewCenterY, ["zoom"] = presence.Zoom,
        ["drags"] = presence.Drags ?? []
    };
    private static bool TryPresence(Dictionary<string, object> payload, out CloudPresence presence)
    {
        presence = default!;
        var userId = PayloadText(payload, "userId"); var username = PayloadText(payload, "username");
        var workspaceId = PayloadText(payload, "workspaceId");
        if (!Guid.TryParse(userId, out _) || string.IsNullOrWhiteSpace(username) || !Guid.TryParse(workspaceId, out _)) return false;
        if (!PayloadDouble(payload, "x", out var x) || !PayloadDouble(payload, "y", out var y)
            || !PayloadDouble(payload, "viewCenterX", out var viewX) || !PayloadDouble(payload, "viewCenterY", out var viewY)
            || !PayloadDouble(payload, "zoom", out var zoom)) return false;
        presence = new CloudPresence(userId!, username!, PayloadText(payload, "picture"), CloudRules.CanonicalGuidId(workspaceId!),
            x, y, string.IsNullOrWhiteSpace(PayloadText(payload, "itemId")) ? null : CloudRules.CanonicalGuidId(PayloadText(payload, "itemId")!),
            viewX, viewY, zoom, ParsePresenceDrags(payload));
        return true;
    }
    private static PresenceDrag[]? ParsePresenceDrags(Dictionary<string, object> payload)
    {
        if (!payload.TryGetValue("drags", out var raw) || raw is null) return null;
        try
        {
            var json = raw is JsonElement element ? element.GetRawText() : JsonSerializer.Serialize(raw);
            return JsonSerializer.Deserialize<PresenceDrag[]>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?
                .Where(d => Guid.TryParse(d.Id, out _) && double.IsFinite(d.X) && double.IsFinite(d.Y))
                .Take(64).Select(d => d with { Id = CloudRules.CanonicalGuidId(d.Id) }).ToArray();
        }
        catch (JsonException) { return null; }
    }
    private static string? PayloadText(Dictionary<string, object> payload, string key)
    {
        if (!payload.TryGetValue(key, out var raw) || raw is null) return null;
        return raw is JsonElement json ? json.ValueKind == JsonValueKind.String ? json.GetString() : json.ToString() : raw.ToString();
    }
    private static bool PayloadDouble(Dictionary<string, object> payload, string key, out double value)
    {
        value = 0;
        if (!payload.TryGetValue(key, out var raw) || raw is null) return false;
        // Realtime's ObjectToInferredTypesConverter returns boxed doubles/longs.
        // Formatting them using the OS culture and parsing as invariant rejected
        // fractional coordinates (and practically every zoom) on pt-BR machines.
        if (raw is JsonElement json)
        {
            if (json.ValueKind == JsonValueKind.Number)
                return json.TryGetDouble(out value) && double.IsFinite(value);
            if (json.ValueKind != JsonValueKind.String) return false;
            raw = json.GetString() ?? "";
        }
        if (raw is string text)
            return double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
        value = raw switch
        {
            double number => number, float number => number, decimal number => (double)number,
            long number => number, int number => number, short number => number, sbyte number => number,
            ulong number => number, uint number => number, ushort number => number, byte number => number,
            _ => double.NaN
        };
        return double.IsFinite(value);
    }
    private static CloudEntity Entity(WorkspaceRow row) => new(CloudRules.CanonicalGuidId(row.Id), "workspace", null, row.Version, new JsonObject { ["name"] = row.Name, ["ownerId"] = row.OwnerId, ["mode"] = row.Mode, ["worldWidth"] = row.WorldWidth, ["worldHeight"] = row.WorldHeight, ["schemaVersion"] = row.SchemaVersion, ["createdAt"] = row.CreatedAt.UtcDateTime.ToString("O") }, row.Deleted);
    private static CloudEntity Entity(BoardEntityRow row) => new(CloudRules.CanonicalGuidId(row.Id), row.Kind, CloudRules.CanonicalGuidId(row.WorkspaceId), row.Version, row.Data ?? [], row.Deleted);
    private sealed class ProfileRow { public string Id { get; set; } = ""; public string? Username { get; set; } [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "Usuário"; [JsonPropertyName("picture_url")] public string? PictureUrl { get; set; } }
    private sealed class WorkspaceRow { public string Id { get; set; } = ""; [JsonPropertyName("owner_id")] public string OwnerId { get; set; } = ""; public string Name { get; set; } = "Mesa"; public string Mode { get; set; } = "personal"; [JsonPropertyName("world_width")] public double WorldWidth { get; set; } [JsonPropertyName("world_height")] public double WorldHeight { get; set; } [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } public long Version { get; set; } public bool Deleted { get; set; } [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; } }
    private sealed class BoardEntityRow { public string Id { get; set; } = ""; [JsonPropertyName("workspace_id")] public string WorkspaceId { get; set; } = ""; public string Kind { get; set; } = "item"; public long Version { get; set; } public JsonObject? Data { get; set; } public bool Deleted { get; set; } }
    private sealed class MemberRow { [JsonPropertyName("user_id")] public string UserId { get; set; } = ""; public string Role { get; set; } = "editor"; [JsonPropertyName("profiles")] public ProfileRow? Profile { get; set; } }
    private sealed class InviteRow { public string Id { get; set; } = ""; [JsonPropertyName("workspace_id")] public string WorkspaceId { get; set; } = ""; [JsonPropertyName("from_user_id")] public string FromUserId { get; set; } = ""; [JsonPropertyName("to_user_id")] public string ToUserId { get; set; } = ""; }
    private sealed class InvitationDetailsRow { public string Id { get; set; } = ""; [JsonPropertyName("workspace_id")] public string WorkspaceId { get; set; } = ""; [JsonPropertyName("workspace_name")] public string WorkspaceName { get; set; } = "Mesa"; [JsonPropertyName("from_username")] public string FromUsername { get; set; } = ""; [JsonPropertyName("from_display_name")] public string FromDisplayName { get; set; } = ""; [JsonPropertyName("from_picture_url")] public string? FromPictureUrl { get; set; } }
    private sealed class PresenceRow { [JsonPropertyName("workspace_id")] public string WorkspaceId { get; set; } = ""; [JsonPropertyName("user_id")] public string UserId { get; set; } = ""; public double X { get; set; } public double Y { get; set; } [JsonPropertyName("item_id")] public string? ItemId { get; set; } [JsonPropertyName("view_center_x")] public double ViewCenterX { get; set; } [JsonPropertyName("view_center_y")] public double ViewCenterY { get; set; } public double Zoom { get; set; } [JsonPropertyName("profiles")] public ProfileRow? Profile { get; set; } }
}
