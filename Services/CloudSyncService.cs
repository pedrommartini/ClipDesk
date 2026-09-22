using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ClipDesk.Core;
using ClipDesk.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace ClipDesk.Services;

/// <summary>
/// Compatibility client for the retired self-hosted development server. New
/// desktop sessions use <see cref="SupabaseCloudSyncService"/> through the
/// public CloudSyncService façade below; keeping this implementation isolated
/// lets the existing local server tests remain useful during the migration.
/// </summary>
internal sealed class LegacyCloudSyncService : ICloudSyncBackend
{
    private readonly StorageService _storage;
    private readonly CloudConfiguration _config;
    private readonly GoogleConnection _google;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate=new(1,1);
    private readonly SemaphoreSlim _realtimeGate=new(1,1);
    private readonly SemaphoreSlim _liveGate=new(1,1);
    private CancellationTokenSource _lifetime=new();
    private HubConnection? _hub;
    private SavedCloudAccount? _account;
    private readonly bool _enableRealtime;
    private readonly HashSet<string> _inaccessible=[];
    private Dictionary<string,CloudEntity> _presented=[];
    private readonly Dictionary<string,string> _paths=[];
    private readonly HashSet<string> _registeredAttachments=[];
    private DateTimeOffset _lastDriveReconcile;
    public bool RealtimeConnected => _hub?.State == HubConnectionState.Connected;
    public CloudUser? User=>_account?.Session.User;
    public bool Connected=>_account is not null;
    public bool SyncHistory { get; set; } = true;
    public string Status { get; private set; }="Salvo neste computador";
    public event Action? Changed;
    public event Action? RemoteChanged;
    public event Action? InvitationsChanged;
    public event Action<CloudPresence>? PresenceReceived;
    public event Action<CloudEntity>? RealtimeEntityReceived;
    public LegacyCloudSyncService(StorageService storage,CloudConfiguration? configuration=null,SavedCloudAccount? account=null,HttpClient? http=null,bool enableRealtime=true)
    {
        _storage=storage; _config=configuration ?? CloudConfiguration.Load(); _enableRealtime=enableRealtime;
        _http=http ?? new HttpClient(new RequestCooldownHandler()) { Timeout=TimeSpan.FromMinutes(3) };
        _google=new GoogleConnection(_config);
        _google.TokensRefreshed+=tokens=> { if(_account is not null) { _account=_account with {Google=tokens}; SecureAccountFile.Save(_account); } };
        try
        {
            _http.BaseAddress=_config.ServerUri(); var saved=account ?? SecureAccountFile.Load();
            if(saved is not null && saved.ServerUrl==_http.BaseAddress.AbsoluteUri && storage.Profile==saved.Session.User.Id)
            { SetAccount(saved); if(saved.GoogleClientId.Length>0) _config.GoogleClientId=saved.GoogleClientId; }
        }
        catch(InvalidOperationException) { Status="Configure a conexão com a nuvem"; }
        SyncHistory=storage.Database.Read("sync-history")!="false";
        LoadPaths();
        LoadPresentation();
    }
    public void LoadPaths()
    {
        _inaccessible.Clear(); _registeredAttachments.Clear(); _lastDriveReconcile=default; SyncHistory=_storage.Database.Read("sync-history")!="false";
        _paths.Clear(); var saved=_storage.Database.Read("attachment-paths");
        if(saved is not null) foreach(var p in CloudRules.Deserialize<Dictionary<string,string>>(saved)) _paths[p.Key]=p.Value;
        LoadPresentation();
    }
    private void LoadPresentation()
    {
        var saved=_storage.Database.Read("sync-presentation");
        _presented=saved is null?[]:CloudRules.Deserialize<List<CloudEntity>>(saved).ToDictionary(e=>e.Id);
    }
    public void ObservePresentation(List<WorkspaceBoard> boards,IEnumerable<ClipboardHistoryEntry> history)
    {
        if(User is null) return;
        _presented=CloudProjection.Project(boards,history,User.Id,SyncHistory).ToDictionary(e=>e.Id);
        _storage.Database.Write("sync-presentation",CloudRules.Serialize(_presented.Values.ToList()));
    }
    private void SavePaths()=>_storage.Database.Write("attachment-paths",CloudRules.Serialize(_paths));
    private void SetAccount(SavedCloudAccount account)
    {
        _account=account; _http.DefaultRequestHeaders.Authorization=new("Bearer",account.Session.Token);
    }
    private void SetStatus(string status) { if (Status == status) return; Status=status; Changed?.Invoke(); }
    public async Task SignInAsync(CancellationToken cancellation)
    {
        _http.BaseAddress ??= _config.ServerUri();
        SetStatus("Conectando ao Google…");
        try
        {
            var config=await _http.GetFromJsonAsync<JsonElement>("config",cancellation);
            _config.GoogleClientId=config.GetProperty("googleClientId").GetString() ?? "";
            if(_config.GoogleClientId.Length==0) throw new InvalidOperationException("O servidor local está pronto. Configure o cliente OAuth Google Desktop para entrar.");
            using var challengeResponse=await _http.PostAsync("auth/challenge",null,cancellation); await EnsureAsync(challengeResponse);
            var challenge=await challengeResponse.Content.ReadFromJsonAsync<LoginChallenge>(cancellation);
            var tokens=await _google.SignInAsync(challenge!.Nonce,cancellation);
            using var response=await _http.PostAsJsonAsync("auth/google",new GoogleLogin(tokens.IdToken,challenge.Nonce),cancellation); await EnsureAsync(response);
            var session=(await response.Content.ReadFromJsonAsync<CloudSession>(cancellation))!;
            var folder=await _google.EnsureFolderAsync(tokens.AccessToken,AppEnvironment.IsDevelopment?"ClipDesk DEV":"ClipDesk",AppEnvironment.Identity+"-root",null,cancellation);
            SetAccount(new(session,tokens,_http.BaseAddress.AbsoluteUri,folder,_config.GoogleClientId));
            SecureAccountFile.Save(_account!); SetStatus("Google e Drive conectados");
        }
        catch { SetStatus("Conexão não concluída"); throw; }
    }
    public async Task SetUsernameAsync(string username)
    {
        using var response=await _http.PostAsJsonAsync("api/username",new UsernameRequest(username)); await EnsureAsync(response);
        var user=(await response.Content.ReadFromJsonAsync<CloudUser>())!;
        _account=_account! with {Session=_account.Session with {User=user}}; SecureAccountFile.Save(_account); Changed?.Invoke();
    }
    public async Task ConnectLiveAsync()
    {
        if(!Connected || _hub is not null || !_enableRealtime) return;
        await _liveGate.WaitAsync(_lifetime.Token);
        try
        {
            if(!Connected || _hub is not null || !_enableRealtime) return;
            var hub=new HubConnectionBuilder().WithUrl(new Uri(_http.BaseAddress!,"live"),options=>options.AccessTokenProvider=()=>Task.FromResult<string?>(_account?.Session.Token)).WithAutomaticReconnect().Build();
            hub.On("Changed",()=> { RemoteChanged?.Invoke(); InvitationsChanged?.Invoke(); });
            hub.On<CloudEntity>("EntityChanged",entity=>
            {
                _storage.Database.Accept(entity);
                var merged=_storage.Database.Entities(entity.Id).FirstOrDefault() ?? entity;
                RealtimeEntityReceived?.Invoke(merged);
            });
            hub.On<CloudPresence>("Presence",presence=>PresenceReceived?.Invoke(presence));
            hub.Reconnected+=_=> { RemoteChanged?.Invoke(); InvitationsChanged?.Invoke(); return Task.CompletedTask; };
            try { await hub.StartAsync(_lifetime.Token);_hub=hub; }
            catch(HttpRequestException) { await hub.DisposeAsync(); }
        }
        finally { _liveGate.Release(); }
    }
    public async Task PresenceAsync(PresenceMessage presence)
    {
        if(_hub?.State==HubConnectionState.Connected) try { await _hub.SendAsync("Presence",presence,_lifetime.Token); } catch(Exception e) when(e is HttpRequestException or InvalidOperationException or OperationCanceledException) { }
    }
    public Task PreparePresenceAsync(string workspaceId) => Task.CompletedTask;
    public async Task SignOutAsync()
    {
        _lifetime.Cancel(); await _gate.WaitAsync(); await _realtimeGate.WaitAsync();
        try
        {
            if(_hub is not null) { await _hub.DisposeAsync(); _hub=null; }
            try { using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(4)); if(Connected) using(await _http.PostAsync("api/logout",null,timeout.Token)) { } } catch(Exception e) when(e is HttpRequestException or OperationCanceledException) { }
            SecureAccountFile.Clear(); _account=null; _http.DefaultRequestHeaders.Authorization=null;
            _lifetime.Dispose(); _lifetime=new(); _paths.Clear(); SetStatus("Salvo neste computador");
        } finally { _realtimeGate.Release();_gate.Release(); }
    }
    public async Task SynchronizeAsync(List<WorkspaceBoard> boards,IEnumerable<ClipboardHistoryEntry> history)
    {
        if(!Connected || string.IsNullOrEmpty(User?.Username) || !await _gate.WaitAsync(0)) return;
        var cancellation=_lifetime.Token;
        try
        {
            SetStatus("Sincronizando…");
            var entries=history.ToList();
            Stage(boards,entries);
            await ConnectLiveAsync();
            await PullAsync(cancellation);
            // Positions, text and workspace edits are urgent. Send them before any local
            // hashing, copying or binary upload so a large attachment cannot freeze the board.
            await PushAsync(cancellation);
            var working=Materialize();
            var before=CloudProjection.Project(working.Boards,working.History,User!.Id,SyncHistory).ToDictionary(e=>e.Id);
            var sources=boards.SelectMany(b=>CloudProjection.Flatten(b.Items)).ToDictionary(i=>i.Id);
            var historySources=entries.ToDictionary(e=>e.Id);
            foreach(var board in working.Boards)
                foreach(var item in CloudProjection.Flatten(board.Items))
                {
                    var original=sources.GetValueOrDefault(item.Id);
                    await PrepareAsync(item.Attachments,original?.FilePaths ?? item.FilePaths,original?.StoredFilePath ?? item.StoredFilePath,board.Id.ToString("N"),cancellation);
                }
            if(SyncHistory) foreach(var entry in working.History)
            {
                var original=historySources.GetValueOrDefault(entry.Id);
                await PrepareAsync(entry.Attachments,original?.FilePaths ?? entry.FilePaths,original?.StoredFilePath ?? entry.StoredFilePath,null,cancellation);
            }
            StageDerived(before,CloudProjection.Project(working.Boards,working.History,User.Id,SyncHistory));
            await PushAsync(cancellation);
            before=CloudProjection.Project(working.Boards,working.History,User.Id,SyncHistory).ToDictionary(e=>e.Id);
            await RefreshAttachmentsAsync(working.Boards,working.History,cancellation);
            StageDerived(before,CloudProjection.Project(working.Boards,working.History,User.Id,SyncHistory));
            await PushAsync(cancellation);
            await PullAsync(cancellation);
            await ReconcileDriveAsync(cancellation);
            SavePaths();
            var pending=working.Boards.SelectMany(b=>CloudProjection.Flatten(b.Items)).SelectMany(i=>i.Attachments).Count(a=>a.OwnerId==User.Id && !a.Uploaded);
            SetStatus(_storage.Database.ConflictCount>0?"Conflito preservado para revisão":_storage.Database.Pending().Count>0?"Sincronização pendente":pending>0 ? $"Mesa salva · ↑ {pending} pendente{(pending==1?"":"s")}" : "Sincronizado");
        }
        catch(OperationCanceledException) { SetStatus("Salvo neste computador"); }
        catch(HttpRequestException e) { SetStatus(e.StatusCode==HttpStatusCode.Unauthorized?"Reconecte o Google":e.StatusCode==HttpStatusCode.TooManyRequests?"Salvo localmente · aguardando limite do servidor":"Salvo localmente · aguardando conexão"); }
        catch(Exception e) when(e is IOException or InvalidOperationException) { SetStatus(e.Message); }
        finally { _gate.Release(); }
    }
    public void Stage(List<WorkspaceBoard> boards,List<ClipboardHistoryEntry> history)
    {
        if(User is null) return;
        var projected=CloudProjection.Project(boards.Where(b=>!_inaccessible.Contains(b.Id.ToString("N"))),history,User.Id,SyncHistory).ToList();
        var ids=projected.Select(e=>e.Id).ToHashSet();
        var activeBoards=boards.Where(b=>b.SyncMode!=WorkspaceSyncMode.Local).Select(b=>b.Id.ToString("N")).ToHashSet();
        StageDerived(_presented,projected);
        foreach(var old in _presented.Values.Where(e=>!e.Deleted && !ids.Contains(e.Id)))
            if((old.Kind is "item" or "boardObject") && activeBoards.Contains(old.WorkspaceId!) || old.Kind=="history" && SyncHistory)
            {
                var latest=_storage.Database.Entities(old.Id).FirstOrDefault();
                if(latest is not null) _storage.Database.Stage(latest with {Deleted=true});
            }
        _presented=projected.ToDictionary(e=>e.Id);
        _storage.Database.Write("sync-presentation",CloudRules.Serialize(projected));
        _storage.Database.Write("sync-history",SyncHistory?"true":"false");
    }
    private void StageDerived(IReadOnlyDictionary<string,CloudEntity> before,IEnumerable<CloudEntity> after)
    {
        var current=_storage.Database.Entities().ToDictionary(e=>e.Id);
        foreach(var entity in after)
        {
            if(entity.Kind=="workspace" && entity.Data["ownerId"]?.GetValue<string>()!=User?.Id) continue;
            if(!current.TryGetValue(entity.Id,out var latest)) { _storage.Database.Stage(entity); continue; }
            if(latest.Deleted || _inaccessible.Contains(entity.WorkspaceId ?? entity.Id)) continue;
            var basis=before.GetValueOrDefault(entity.Id)?.Data ?? entity.Data;
            try
            {
                var merged=CloudRules.Merge(latest.Data,basis,entity.Data);
                if (!CloudRules.Same(latest.Data,merged) || latest.Deleted != entity.Deleted) _storage.Database.Stage(entity with {Data=merged});
            }
            catch(SyncConflictException) { _storage.Database.PreserveConflict(entity.Id,entity.Data); }
        }
    }
    public void StageBoardObject(WorkspaceBoard board,BoardObject obj)
    {
        if(User is null || board.SyncMode==WorkspaceSyncMode.Local || _inaccessible.Contains(board.Id.ToString("N"))) return;
        var projected=CloudProjection.ProjectBoardObject(obj,board.Id.ToString("N"));
        var latest=_storage.Database.Entities(projected.Id).FirstOrDefault();
        if(latest is null) _storage.Database.Stage(projected);
        else if(!latest.Deleted)
        {
            var basis=_presented.GetValueOrDefault(projected.Id)?.Data ?? latest.Data;
            try
            {
                var merged=CloudRules.Merge(latest.Data,basis,projected.Data);
                if(!CloudRules.Same(latest.Data,merged)) _storage.Database.Stage(projected with {Data=merged});
            }
            catch(SyncConflictException) { _storage.Database.PreserveConflict(projected.Id,projected.Data); }
        }
        _presented[projected.Id]=projected;
        _storage.Database.Write("sync-presentation",CloudRules.Serialize(_presented.Values.ToList()));
    }
    public async Task SynchronizeBoardObjectsAsync(WorkspaceBoard board,IEnumerable<BoardObject> objects)
    {
        var ids=objects.DistinctBy(obj=>obj.Id).Select(obj=> {StageBoardObject(board,obj);return obj.Id;}).ToList();
        if(ids.Count==0 || !Connected || string.IsNullOrEmpty(User?.Username) || !await _realtimeGate.WaitAsync(0)) return;
        try
        {
            await ConnectLiveAsync();
            foreach(var id in ids) await PushAsync(_lifetime.Token,entityId:id);
            SetStatus(_storage.Database.Pending().Count>0?"Sincronização pendente":"Sincronizado");
        }
        catch(OperationCanceledException) { }
        catch(HttpRequestException e) { SetStatus(e.StatusCode==HttpStatusCode.TooManyRequests?"Salvo localmente · aguardando limite do servidor":"Salvo localmente · aguardando conexão"); }
        finally { _realtimeGate.Release(); }
    }
    private async Task PushAsync(CancellationToken cancellation,bool headersOnly=false,string? entityId=null)
    {
        foreach(var operation in _storage.Database.Pending(entityId))
        {
            if(headersOnly && operation.Kind!="workspace") continue;
            if(_inaccessible.Contains(operation.WorkspaceId ?? operation.EntityId)) continue;
            using var response=await _http.PostAsJsonAsync("api/sync",operation,cancellation);
            if(response.StatusCode==HttpStatusCode.Forbidden)
            {
                await PullAsync(cancellation);
                if(_inaccessible.Contains(operation.WorkspaceId ?? operation.EntityId)) continue;
            }
            if(response.StatusCode==HttpStatusCode.Conflict)
            {
                var remote=await _http.GetFromJsonAsync<List<CloudEntity>>("api/entities",cancellation);
                var current=remote?.FirstOrDefault(e=>e.Id==operation.EntityId);
                if(current is not null) _storage.Database.Accept(current);
                SetStatus("Conflito preservado para revisão"); continue;
            }
            await EnsureAsync(response); var result=(await response.Content.ReadFromJsonAsync<SyncResult>(cancellation))!;
            _storage.Database.Accept(result.Entity,operation);
        }
    }
    private async Task PullAsync(CancellationToken cancellation)
    {
        using var response=await _http.GetAsync("api/entities",cancellation); await EnsureAsync(response);
        var remote=(await response.Content.ReadFromJsonAsync<List<CloudEntity>>(cancellation))!;
        var available=remote.Where(e=>e.Kind=="workspace" && !e.Deleted).Select(e=>e.Id).ToHashSet();
        foreach(var old in _storage.Database.Entities().Where(e=>e.Kind=="workspace" && e.Version>0 && !available.Contains(e.Id)))
        { _inaccessible.Add(old.Id); _storage.Database.ForgetWorkspace(old.Id); }
        foreach(var entity in remote)
        {
            if(entity.Kind=="workspace" && entity.Deleted) { _inaccessible.Add(entity.Id); _storage.Database.ForgetWorkspace(entity.Id); continue; }
            if(_inaccessible.Contains(entity.WorkspaceId ?? entity.Id)) continue;
            _storage.Database.Accept(entity);
        }
    }
    private async Task PrepareAsync(List<CloudAttachment> attachments,List<string> filePaths,string? imagePath,string? workspace,CancellationToken cancellation)
    {
        if(attachments.Count==0)
        {
            var sources=new List<(string path,string? relative)>();
            foreach(var path in filePaths.Concat(imagePath is null?[]:new[]{imagePath}))
            {
                if(File.Exists(path)) sources.Add((path,null));
                else if(Directory.Exists(path))
                    sources.AddRange(Directory.EnumerateFiles(path,"*",new EnumerationOptions {RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint,IgnoreInaccessible=false}).Select(f=>(f,(string?)Path.GetRelativePath(path,f))));
                else throw new IOException("Um arquivo local não foi encontrado. A mesa foi preservada.");
            }
            foreach(var (path,relative) in sources)
            {
                var id=Guid.NewGuid().ToString("N"); var directory=Path.Combine(_storage.AssetsDirectory,id); Directory.CreateDirectory(directory);
                var target=Path.Combine(directory,Path.GetFileName(path));
                await using(var source=File.OpenRead(path)) await using(var destination=File.Create(target)) await source.CopyToAsync(destination,cancellation);
                await using var file=File.OpenRead(target); var hash=Convert.ToHexString(await SHA256.HashDataAsync(file,cancellation));
                var a=new CloudAttachment {Id=id,OwnerId=User!.Id,Name=Path.GetFileName(path),Size=file.Length,Sha256=hash,IsDrive=CloudRules.UsesDrive(file.Length),LocalPath=target,RelativePath=relative};
                attachments.Add(a); _paths[id]=target;
                SavePaths();
            }
        }
        foreach(var a in attachments.Where(a=>a.OwnerId==User!.Id))
        {
            var registrationKey=a.Id+":"+workspace;
            if (_registeredAttachments.Add(registrationKey))
            {
                try { using var response=await _http.PostAsJsonAsync("api/attachments",new AttachmentRegistration(a.Id,workspace,a.Name,a.Size,a.Sha256,a.IsDrive),cancellation); await EnsureAsync(response); }
                catch { _registeredAttachments.Remove(registrationKey); throw; }
            }
            if(!a.IsDrive && !a.Uploaded)
            {
                var path=a.LocalPath ?? _paths.GetValueOrDefault(a.Id);
                if(path is null || !File.Exists(path)) continue;
                await using var file=File.OpenRead(path);
                using var content=new StreamContent(file); using var upload=await _http.PutAsync($"api/attachments/{a.Id}/content",content,cancellation); await EnsureAsync(upload); a.Uploaded=true;
            }
        }
    }
    private async Task RefreshAttachmentsAsync(List<WorkspaceBoard> boards,List<ClipboardHistoryEntry> history,CancellationToken cancellation)
    {
        var attachments=boards.Where(b=>b.SyncMode!=WorkspaceSyncMode.Local && !_inaccessible.Contains(b.Id.ToString("N"))).SelectMany(b=>CloudProjection.Flatten(b.Items)).SelectMany(i=>i.Attachments)
            .Concat(SyncHistory?history.SelectMany(e=>e.Attachments):[]).DistinctBy(a=>a.Id);
        foreach(var a in attachments)
        {
            // Completed immutable files need neither status polling nor repeated downloads.
            if (a.Uploaded && (a.IsDrive || (_paths.TryGetValue(a.Id,out var local) && File.Exists(local)))) continue;
            var json=await _http.GetFromJsonAsync<JsonElement>($"api/attachments/{a.Id}",cancellation);
            a.Uploaded=json.GetProperty("ready").GetInt32()==1;
            if(json.TryGetProperty("file_id",out var fileId) && fileId.ValueKind==JsonValueKind.String) a.DriveFileId=fileId.GetString();
            if(!a.IsDrive && a.Uploaded && (!_paths.TryGetValue(a.Id,out var cached) || !File.Exists(cached))) await DownloadAsync(a,cancellation);
        }
    }
    public (List<WorkspaceBoard> Boards,List<ClipboardHistoryEntry> History) Materialize()
    {
        var boards=CloudProjection.Materialize(_storage.Database.Entities(),_paths);
        foreach(var item in boards.SelectMany(b=>CloudProjection.Flatten(b.Items)).Where(i=>i.Type==ClipboardItemType.Folder)) RestoreFolder(item);
        return (boards,CloudProjection.History(_storage.Database.Entities(),_paths));
    }
    private void RestoreFolder(ClipboardItem item)
    {
        var root=Path.GetFullPath(Path.Combine(_storage.AssetsDirectory,"Folders",item.Id));
        Directory.CreateDirectory(root);
        foreach(var a in item.Attachments.Where(a=>a.LocalPath is not null && File.Exists(a.LocalPath)))
        {
            var relative=a.RelativePath ?? a.Name;
            if(Path.IsPathRooted(relative)) throw new IOException("Caminho de pasta inválido.");
            var destination=Path.GetFullPath(Path.Combine(root,relative));
            if(!destination.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("Caminho de pasta inválido.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if(!File.Exists(destination)) File.Copy(a.LocalPath!,destination);
        }
        item.FilePaths=[root];
    }
    public async Task UploadDriveAsync(CloudAttachment attachment,WorkspaceBoard? board,IProgress<double>? progress=null)
    {
        if(User is null || attachment.OwnerId!=User.Id) throw new InvalidOperationException("Somente o dono pode enviar este arquivo.");
        await _gate.WaitAsync(_lifetime.Token);
        try
        {
            var tokens=await _google.RefreshAsync(_account!.Google,_lifetime.Token);
            var folder=await _google.EnsureFolderAsync(tokens.AccessToken,board?.Name ?? "Histórico",board?.Id.ToString("N") ?? AppEnvironment.Identity+"-history",_account.RootFolderId,_lifetime.Token);
            var fileId=await _google.UploadAsync(tokens.AccessToken,attachment,folder,_storage.Database,progress,_lifetime.Token);
            using var request=new HttpRequestMessage(HttpMethod.Post,$"api/attachments/{attachment.Id}/drive") { Content=JsonContent.Create(new DriveCompletion(fileId)) };
            request.Headers.Add("X-Google-Access-Token",tokens.AccessToken);
            using var response=await _http.SendAsync(request,_lifetime.Token); await EnsureAsync(response);
            attachment.DriveFileId=fileId; attachment.Uploaded=true;
            _lastDriveReconcile=default;
            await ReconcileDriveAsync(_lifetime.Token);
        } finally { _gate.Release(); }
    }
    public async Task DownloadAsync(CloudAttachment attachment,CancellationToken cancellation=default,bool saveForUser=false)
    {
        var directory=saveForUser?StorageService.DownloadsDirectory:Path.Combine(_storage.AssetsDirectory,attachment.Id); Directory.CreateDirectory(directory);
        var safeName=Path.GetFileName(attachment.Name); if(string.IsNullOrEmpty(safeName) || safeName!=attachment.Name) throw new IOException("Nome de arquivo inválido.");
        var known=_paths.GetValueOrDefault(attachment.Id);
        var target=saveForUser && known is not null && File.Exists(known) && Path.GetDirectoryName(known)?.Equals(directory,StringComparison.OrdinalIgnoreCase)==true
            ? known
            : AvailableDownloadPath(directory,safeName);
        if(attachment.IsDrive)
        {
            if(_account is null || !attachment.Uploaded || attachment.DriveFileId is null) throw new IOException("O proprietário ainda não enviou o arquivo.");
            var tokens=await _google.RefreshAsync(_account.Google,cancellation); await _google.DownloadAsync(tokens.AccessToken,attachment.DriveFileId,target+".download",cancellation);
        }
        else
        {
            using var response=await _http.GetAsync($"api/attachments/{attachment.Id}/content",HttpCompletionOption.ResponseHeadersRead,cancellation); await EnsureAsync(response);
            await using var output=File.Create(target+".download"); await response.Content.CopyToAsync(output,cancellation);
        }
        await using(var file=File.OpenRead(target+".download"))
            if(file.Length!=attachment.Size || !Convert.ToHexString(await SHA256.HashDataAsync(file,cancellation)).Equals(attachment.Sha256,StringComparison.OrdinalIgnoreCase)) throw new IOException("O arquivo baixado falhou na verificação de integridade.");
        File.Move(target+".download",target,true); attachment.LocalPath=target; _paths[attachment.Id]=target; SavePaths();
    }
    private static string AvailableDownloadPath(string directory,string fileName)
    {
        var candidate=Path.Combine(directory,fileName);
        if(!File.Exists(candidate) && !File.Exists(candidate+".download")) return candidate;
        var stem=Path.GetFileNameWithoutExtension(fileName);var extension=Path.GetExtension(fileName);
        for(var copy=2;copy<10_000;copy++)
        {
            candidate=Path.Combine(directory,$"{stem} ({copy}){extension}");
            if(!File.Exists(candidate) && !File.Exists(candidate+".download")) return candidate;
        }
        throw new IOException("Não foi possível criar um nome disponível na pasta de downloads.");
    }
    private async Task ReconcileDriveAsync(CancellationToken cancellation)
    {
        if (DateTimeOffset.UtcNow-_lastDriveReconcile < TimeSpan.FromMinutes(1)) return;
        var grants=await _http.GetFromJsonAsync<List<DriveGrant>>("api/drive/grants",cancellation) ?? [];
        foreach(var grant in grants)
        {
            var key="drive-grant:"+grant.FileId+":"+grant.Email+":"+grant.MembershipVersion;
            if(_storage.Database.Read(key)=="granted") continue;
            var tokens=await _google.RefreshAsync(_account!.Google,cancellation); await _google.GrantAsync(tokens.AccessToken,grant,cancellation); _storage.Database.Write(key,"granted");
        }
        _lastDriveReconcile=DateTimeOffset.UtcNow;
    }
    public async Task<List<CloudMember>> MembersAsync(WorkspaceBoard board)=>await _http.GetFromJsonAsync<List<CloudMember>>($"api/workspaces/{board.Id:N}/members",_lifetime.Token) ?? [];
    public async Task InviteAsync(WorkspaceBoard board,string username)
    {
        using var response=await _http.PostAsJsonAsync($"api/workspaces/{board.Id:N}/invites",new InviteRequest(username),_lifetime.Token); await EnsureAsync(response);
    }
    public async Task RemoveMemberAsync(WorkspaceBoard board,CloudMember member)
    {
        if(board.OwnerId!=User?.Id) throw new InvalidOperationException("Somente o proprietário pode remover colaboradores.");
        await _gate.WaitAsync(_lifetime.Token);
        try
        {
            var path=$"api/workspaces/{board.Id:N}/members/{Uri.EscapeDataString(member.UserId)}";
            var grants=await _http.GetFromJsonAsync<List<DriveGrant>>(path+"/drive-grants",_lifetime.Token) ?? [];
            foreach(var grant in grants)
            {
                var tokens=await _google.RefreshAsync(_account!.Google,_lifetime.Token);
                await _google.RevokeAsync(tokens.AccessToken,grant,_lifetime.Token);
            }
            using var response=await _http.DeleteAsync(path,_lifetime.Token); await EnsureAsync(response);
        }
        finally { _gate.Release(); }
    }
    public async Task DeleteWorkspaceAsync(WorkspaceBoard board)
    {
        if(User is null || board.OwnerId!=User.Id) throw new InvalidOperationException("Somente o proprietário pode excluir esta mesa.");
        await _gate.WaitAsync(_lifetime.Token);
        try
        {
            var workspaceId=board.Id.ToString("N");
            if(board.SyncMode==WorkspaceSyncMode.Shared)
            {
                var grants=await _http.GetFromJsonAsync<List<DriveGrant>>("api/drive/grants",_lifetime.Token) ?? [];
                foreach(var grant in grants.Where(grant=>grant.WorkspaceId==workspaceId))
                {
                    var tokens=await _google.RefreshAsync(_account!.Google,_lifetime.Token);
                    await _google.RevokeAsync(tokens.AccessToken,grant,_lifetime.Token);
                }
            }
            var driveFiles=CloudProjection.Flatten(board.Items).SelectMany(item=>item.Attachments)
                .Where(attachment=>attachment.IsDrive && attachment.Uploaded && attachment.DriveFileId is not null)
                .Select(attachment=>attachment.DriveFileId!).Distinct().ToList();
            if(driveFiles.Count>0)
            {
                var tokens=await _google.RefreshAsync(_account!.Google,_lifetime.Token);
                foreach(var fileId in driveFiles) await _google.DeleteAsync(tokens.AccessToken,fileId,_lifetime.Token);
            }
            var entities=_storage.Database.Entities()
                .Where(entity=>entity.Id==workspaceId || entity.WorkspaceId==workspaceId)
                .OrderBy(entity=>entity.Kind=="workspace" ? 1 : 0)
                .ToList();
            foreach(var entity in entities)
            {
                var operation=new SyncOperation(Guid.NewGuid().ToString("N"),entity.Id,entity.Kind,entity.WorkspaceId,entity.Version,entity.Data,entity.Data,true);
                using var response=await _http.PostAsJsonAsync("api/sync",operation,_lifetime.Token);await EnsureAsync(response);
            }
            _inaccessible.Add(workspaceId);
            _storage.Database.ForgetWorkspace(workspaceId);
            _presented.Remove(workspaceId);
            foreach(var entity in entities.Where(entity=>entity.WorkspaceId==workspaceId)) _presented.Remove(entity.Id);
            _storage.Database.Write("sync-presentation",CloudRules.Serialize(_presented.Values.ToList()));
        }
        finally { _gate.Release(); }
    }
    public async Task<List<CloudInvitation>> InvitationsAsync()=>await _http.GetFromJsonAsync<List<CloudInvitation>>("api/invites",_lifetime.Token) ?? [];
    public async Task AcceptInviteAsync(string id) { using var response=await _http.PostAsync($"api/invites/{id}/accept",null,_lifetime.Token); await EnsureAsync(response); }
    public async Task DeclineInviteAsync(string id) { using var response=await _http.PostAsync($"api/invites/{id}/decline",null,_lifetime.Token); await EnsureAsync(response); }
    public async Task MakeLocalAsync(WorkspaceBoard board)
    {
        if(board.OwnerId!=User?.Id) throw new InvalidOperationException("Somente o proprietário pode remover a mesa da nuvem.");
        await _gate.WaitAsync(_lifetime.Token);
        try
        {
            foreach(var attachment in CloudProjection.Flatten(board.Items).SelectMany(i=>i.Attachments))
                if(attachment.LocalPath is null || !File.Exists(attachment.LocalPath))
                {
                    if(!attachment.Uploaded) throw new IOException("Conclua o upload pendente no computador de origem antes de manter uma cópia somente local neste dispositivo.");
                    await DownloadAsync(attachment,_lifetime.Token);
                }
            if(board.SyncMode==WorkspaceSyncMode.Shared) await MakePersonalCoreAsync(board);
            var entity=_storage.Database.Entities().FirstOrDefault(e=>e.Id==board.Id.ToString("N"));
            if(entity is not null)
            {
                var op=new SyncOperation(Guid.NewGuid().ToString("N"),entity.Id,"workspace",null,entity.Version,entity.Data,entity.Data,true);
                using var response=await _http.PostAsJsonAsync("api/sync",op,_lifetime.Token); await EnsureAsync(response);
            }
            _storage.Database.ForgetWorkspace(board.Id.ToString("N")); board.Id=Guid.NewGuid(); board.SyncMode=WorkspaceSyncMode.Local; board.OwnerId=null;
            foreach(var item in CloudProjection.Flatten(board.Items))
            {
                if(item.Type==ClipboardItemType.Folder) RestoreFolder(item);
                if(item.Type==ClipboardItemType.Image) item.StoredFilePath=item.Attachments.FirstOrDefault()?.LocalPath ?? item.StoredFilePath;
                else if(item.Type!=ClipboardItemType.Folder && item.Attachments.Count>0) item.FilePaths=item.Attachments.Where(a=>a.LocalPath is not null).Select(a=>a.LocalPath!).ToList();
                item.Id=Guid.NewGuid().ToString("N");
                item.Attachments.Clear();
            }
        } finally { _gate.Release(); }
    }
    public async Task MakePersonalAsync(WorkspaceBoard board)
    {
        await _gate.WaitAsync(_lifetime.Token);try {await MakePersonalCoreAsync(board);} finally {_gate.Release();}
    }
    private async Task MakePersonalCoreAsync(WorkspaceBoard board)
    {
        if(board.OwnerId!=User?.Id) throw new InvalidOperationException("Somente o proprietário pode alterar o compartilhamento.");
        using var check=await _http.GetAsync($"api/workspaces/{board.Id:N}/private-readiness",_lifetime.Token);await EnsureAsync(check);
        var grants=await _http.GetFromJsonAsync<List<DriveGrant>>("api/drive/grants",_lifetime.Token) ?? [];
        foreach(var grant in grants.Where(g=>g.WorkspaceId==board.Id.ToString("N")))
        {
            var tokens=await _google.RefreshAsync(_account!.Google,_lifetime.Token);await _google.RevokeAsync(tokens.AccessToken,grant,_lifetime.Token);
        }
        using var response=await _http.PostAsync($"api/workspaces/{board.Id:N}/personal",null,_lifetime.Token);await EnsureAsync(response);
        board.SyncMode=WorkspaceSyncMode.PersonalCloud;
    }
    private static async Task EnsureAsync(HttpResponseMessage response)
    {
        if(response.IsSuccessStatusCode) return;
        if(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
        {
            var message=await response.Content.ReadFromJsonAsync<JsonElement>(); throw new InvalidOperationException(message.TryGetProperty("message",out var text)?text.GetString():"Operação não concluída.");
        }
        response.EnsureSuccessStatusCode();
    }
    public async ValueTask DisposeAsync() { _lifetime.Cancel(); if(_hub is not null) await _hub.DisposeAsync(); _http.Dispose(); _lifetime.Dispose(); }
}
