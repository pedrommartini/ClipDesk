using System.Diagnostics;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using ClipDesk.Core;
using ClipDesk.Models;
using ClipDesk.Services;
using ClipDesk.Views;
using Path=System.IO.Path;

namespace ClipDesk;

public partial class MainWindow
{
    private CloudSyncService? _cloud;
    private readonly DispatcherTimer _cloudTimer=new() {Interval=TimeSpan.FromSeconds(2)};
    private readonly DispatcherTimer _invitationTimer=new() {Interval=TimeSpan.FromSeconds(5)};
    private readonly DispatcherTimer _syncDebounce=new() {Interval=TimeSpan.FromMilliseconds(160)};
    private readonly DispatcherTimer _cursorExpiryTimer=new() {Interval=TimeSpan.FromSeconds(2)};
    private readonly DispatcherTimer _presenceHeartbeatTimer=new() {Interval=TimeSpan.FromSeconds(3)};
    private readonly DispatcherTimer _presenceTravelTimer=new() {Interval=TimeSpan.FromMilliseconds(16)};
    private PresenceMessage? _pendingPresence;
    private int _presencePumpRunning;
    private bool _cloudClosing;
    private readonly ConcurrentDictionary<string,CloudPresence> _incomingPresence = new();
    private bool _cloudBusy;
    private bool _syncRequestedWhileBusy;
    private bool _checkingInvitations;
    private bool _wasCloudConnected;
    private bool _cloudActionBusy;
    private bool _applyingCloud;
    private readonly HashSet<string> _hiddenCursors=[];
    private readonly Dictionary<string,(FrameworkElement Visual,DateTimeOffset Seen)> _remoteCursors=[];
    private readonly Dictionary<string,string?> _remoteSelections=[];
    private readonly Dictionary<string,CloudPresence> _latestPresence=[];
    private DateTimeOffset _lastPresence;
    private Point _lastLocalPresencePoint;
    private bool _hasLocalPresence;
    private DateTimeOffset _lastCloudFailureToast;
    private DateTimeOffset _lastMembers;
    private DateTimeOffset _lastInvitations;
    private DateTimeOffset _presenceTravelStarted;
    private Point _presenceTravelFrom;
    private Point _presenceTravelTo;
    private double _presenceTravelZoomFrom;
    private double _presenceTravelZoomTo;
    private double _presenceTravelDurationMs=520;
    private long _presenceTravelLastTick;
    private Point _followCameraCenter;
    private Point _followCameraTarget;
    private double _followCameraZoom;
    private double _followCameraZoomTarget;
    private int _followCameraBannerAnimationVersion;
    private bool _applyingPresenceTravel;
    private string? _followedMemberId;
    private string? _followedMemberUsername;
    private CancellationTokenSource? _loginCancellation;
    private string? _avatarUrl;
    private int _profileGeneration;
    private CloudInvitation? _activeInvite;
    private Guid? _acceptedInvitationWorkspaceId;
    private void InitializeCloud()
    {
        _cloud=new CloudSyncService(_storageService);
        _cloud.Changed+=()=>Dispatcher.BeginInvoke(() =>
        {
            UpdateCloudPresentation();
            var connected=_cloud?.Connected==true;
            if(connected && !_wasCloudConnected)
            {
                _lastInvitations=DateTimeOffset.MinValue;
                _=CheckInvitationsAsync();
                _=PrepareActivePresenceAsync();
                QueueCloudSync();
            }
            _wasCloudConnected=connected;
        });
        _cloud.RemoteChanged+=()=>Dispatcher.BeginInvoke(QueueCloudSync);
        _cloud.InvitationsChanged+=()=>Dispatcher.BeginInvoke(async () =>
        {
            _lastInvitations=DateTimeOffset.MinValue;
            await CheckInvitationsAsync();
        });
        _cloud.RealtimeEntityReceived+=entity=>Dispatcher.BeginInvoke(()=>ApplyRealtimeEntity(entity));
        // Consume only the newest position per collaborator once per rendered frame.
        _cloud.PresenceReceived+=presence=>_incomingPresence[presence.UserId]=presence;
        CompositionTarget.Rendering+=RenderCollaborationFrame;
        _cloudTimer.Tick+=async (_,_)=>
        {
            _cloudTimer.Stop();
            try { ExpireCursors(); await RunCloudSyncAsync(); await CheckInvitationsAsync(); }
            finally { if (!_isExitRequested) _cloudTimer.Start(); }
        };
        _syncDebounce.Tick+=async (_,_)=> { _syncDebounce.Stop(); await RunCloudSyncAsync(); };
        _invitationTimer.Tick+=async (_,_)=>await CheckInvitationsAsync();
        _cursorExpiryTimer.Tick+=(_,_)=>ExpireCursors();
        _presenceHeartbeatTimer.Tick+=(_,_)=>RefreshPresenceViewport();
        _presenceTravelTimer.Tick+=(_,_)=> { if(_followedMemberId is null) AdvancePresenceTravel(); };
        _cloudTimer.Start();
        _invitationTimer.Start();
        _presenceHeartbeatTimer.Start();
        PreviewMouseDown+=(_,e)=> { if(CloudPanel.Visibility==Visibility.Visible && !CloudPanel.IsMouseOver && !CloudActions.IsMouseOver && !CloudStatusButton.IsMouseOver) CloudPanel.Visibility=Visibility.Collapsed; };
        PreviewKeyDown+=(_,e)=> { if(e.Key==Key.Escape && CloudPanel.Visibility==Visibility.Visible) { CloudPanel.Visibility=Visibility.Collapsed; e.Handled=true; } };
        Root.AddHandler(Mouse.MouseMoveEvent,new MouseEventHandler((_,e)=>
        {
            if(!WorkspaceScroll.IsMouseOver && Mouse.Captured is null)return;
            MotionDiagnostics.Record(MotionDiagnostics.Stage.Input);
            _lastLocalPresencePoint=e.GetPosition(WorkspaceCanvas);_hasLocalPresence=true;_presenceInputDirty=true;
        }),true);
        Root.AddHandler(Mouse.MouseUpEvent,new MouseButtonEventHandler((_,_)=>_presenceInputDirty=true),true);
        Closed+=async (_,_)=> { _cloudClosing=true; CompositionTarget.Rendering-=RenderCollaborationFrame; _cloudTimer.Stop(); _invitationTimer.Stop(); _syncDebounce.Stop(); _cursorExpiryTimer.Stop(); _presenceHeartbeatTimer.Stop(); _presenceTravelTimer.Stop(); _loginCancellation?.Cancel(); if(_cloud is not null) await _cloud.DisposeAsync(); };
        UpdateCloudPresentation();
    }
    private string? CurrentPresenceItemId() => _selectedBoardObjectView?.Object.Id ?? _selectedCards.FirstOrDefault()?.Item.Id;
    private void QueuePresence(string? itemId,Point point)
    {
        if(_cloudClosing || _cloud?.Connected!=true || _activeWorkspace.SyncMode!=WorkspaceSyncMode.Shared) return;
        _lastLocalPresencePoint=point;
        _hasLocalPresence=true;
        var zoom=Math.Max(WorkspaceBoardScale.ScaleX,BoardViewport.MinimumZoom);
        var centerX=(WorkspaceScroll.HorizontalOffset+WorkspaceScroll.ViewportWidth/2)/zoom;
        var centerY=(WorkspaceScroll.VerticalOffset+WorkspaceScroll.ViewportHeight/2)/zoom;
        Interlocked.Exchange(ref _pendingPresence,new PresenceMessage(_activeWorkspace.Id.ToString("N"),point.X,point.Y,itemId,centerX,centerY,zoom,CapturePresenceDrags()));
        MotionDiagnostics.Record(MotionDiagnostics.Stage.Queued);
        StartPresencePump();
    }
    private void RefreshPresenceViewport()
    {
        if(_cloudClosing || _cloud?.Connected!=true || _activeWorkspace.SyncMode!=WorkspaceSyncMode.Shared) return;
        var point=_hasLocalPresence?_lastLocalPresencePoint:Mouse.GetPosition(WorkspaceCanvas);
        QueuePresence(CurrentPresenceItemId(),point);
    }
    private void StartPresencePump()
    {
        if(Interlocked.CompareExchange(ref _presencePumpRunning,1,0)==0) _=Task.Run(PumpPresenceAsync);
    }
    private async Task PumpPresenceAsync()
    {
        try
        {
            while(!_cloudClosing)
            {
                var presence=Interlocked.Exchange(ref _pendingPresence,null);
                if(presence is null) break;
                // Input is sampled once per rendered frame. A short safety interval
                // avoids doubling a 16 ms Windows timer tick as the old 20 ms wait did.
                // Slow sends still replace pending positions, never accumulate them.
                var remaining=TimeSpan.FromMilliseconds(12)-(DateTimeOffset.UtcNow-_lastPresence);
                if(remaining>TimeSpan.Zero) await Task.Delay(remaining);
                presence=Interlocked.Exchange(ref _pendingPresence,null)??presence;
                var cloud=_cloud;
                if(cloud?.Connected!=true) break;
                _lastPresence=DateTimeOffset.UtcNow;
                await cloud.PresenceAsync(presence).ConfigureAwait(false);
            }
        }
        catch(Exception e) when(e is not OutOfMemoryException) { }
        finally
        {
            Interlocked.Exchange(ref _presencePumpRunning,0);
            if(!_cloudClosing && Volatile.Read(ref _pendingPresence) is not null) StartPresencePump();
        }
    }
    private async Task PrepareActivePresenceAsync()
    {
        try
        {
            if(_cloud?.Connected==true && _activeWorkspace.SyncMode==WorkspaceSyncMode.Shared)
                await _cloud.PreparePresenceAsync(_activeWorkspace.Id.ToString("N"));
        }
        catch(Exception e) when(e is not OutOfMemoryException) { }
    }
    private void ApplyRealtimeEntity(CloudEntity entity)
    {
        if(entity.Id==_editingBoardObjectId || _pendingRealtimeBoardObjects.ContainsKey(entity.Id)
            || _storageService.Database.Pending(entity.Id).Count>0) return;
        if(entity.Kind!="boardObject" || entity.WorkspaceId!=_activeWorkspace.Id.ToString("N")) { QueueCloudSync(); return; }
        var existing=_activeWorkspace.Objects.FirstOrDefault(obj=>obj.Id==entity.Id);
        var existingView=WorkspaceCanvas.Children.OfType<BoardObjectView>().FirstOrDefault(view=>view.Object.Id==entity.Id);
        if(existingView?.IsEditingPluginInput==true) return;
        if(entity.Deleted)
        {
            if(existing is not null) _activeWorkspace.Objects.Remove(existing);
            if(existingView is not null) WorkspaceCanvas.Children.Remove(existingView);
            if(_selectedBoardObjectView==existingView) ClearBoardObjectSelection();
            EnsureWorkspaceExtent();
            return;
        }
        var incoming=CloudProjection.MaterializeBoardObject(entity);
        if(existing is null)
        {
            _activeWorkspace.Objects.Add(incoming);
            AddBoardObjectView(incoming).PlayPopIn();
            EnsureWorkspaceExtent();
            return;
        }
        var visualChanged=BoardObjectVisualChanged(existing,incoming);
        existing.Kind=incoming.Kind;existing.X=incoming.X;existing.Y=incoming.Y;existing.Width=incoming.Width;existing.Height=incoming.Height;
        existing.Rotation=incoming.Rotation;existing.ZIndex=incoming.ZIndex;existing.Locked=incoming.Locked;existing.CreatedBy=incoming.CreatedBy;
        existing.CreatedAt=incoming.CreatedAt;existing.UpdatedAt=incoming.UpdatedAt;existing.Style=incoming.Style;existing.Content=incoming.Content;
        if(existingView is null) AddBoardObjectView(existing);
        else { if(visualChanged)existingView.RefreshFromObject();PositionBoardObjectView(existingView);Panel.SetZIndex(existingView,existing.Kind==BoardObjectKind.Connector?-2:existing.ZIndex); }
        ReconcileDragPreviews();
        RefreshConnectorsForNodes(ConnectedComponent(ObjectNodeId(existing.Id)));
        EnsureWorkspaceExtent();
    }
    private async Task CheckInvitationsAsync()
    {
        if(_cloud?.Connected!=true || _activeInvite is not null || _checkingInvitations
            || DateTimeOffset.UtcNow-_lastInvitations<TimeSpan.FromSeconds(4)) return;
        _checkingInvitations=true;
        _lastInvitations=DateTimeOffset.UtcNow;
        try
        {
            var invitations=await _cloud.InvitationsAsync();
            InviteNotificationDot.Visibility=invitations.Count>0?Visibility.Visible:Visibility.Collapsed;
            if(invitations.Count>0) ShowInvitePopup(invitations[0]);
        }
        catch(Exception e) when(e is not OutOfMemoryException)
        {
            _lastInvitations=DateTimeOffset.MinValue;
            ShowCloudFailure("Convites",e);
        }
        finally { _checkingInvitations=false; }
    }
    private void ShowInvitePopup(CloudInvitation invite)
    {
        _activeInvite=invite;
        InviteSenderName.Text=string.IsNullOrWhiteSpace(invite.FromName)?"@"+invite.FromUsername:invite.FromName;
        InviteSenderUsername.Text="@"+invite.FromUsername;
        InviteWorkspaceName.Text=invite.WorkspaceName;
        InviteSenderAvatar.Fill=new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6D5BD0"));
        if(Uri.TryCreate(invite.FromPicture,UriKind.Absolute,out var picture) && picture.Scheme=="https")
        {
            try
            {
                var image=new BitmapImage(); image.BeginInit(); image.UriSource=picture; image.DecodePixelWidth=84; image.EndInit();
                InviteSenderAvatar.Fill=new ImageBrush(image) {Stretch=Stretch.UniformToFill};
            }
            catch (Exception e) when (e is UriFormatException or IOException or System.Net.WebException) { }
        }
        InviteBackdrop.Visibility=Visibility.Visible;
        InvitePopup.Visibility=Visibility.Visible;
        InvitePopup.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(150)));
        if(!IsVisible) { Show(); WindowState=WindowState.Normal; }
        Activate();
    }
    private async void InviteAccept_Click(object sender,RoutedEventArgs e)
    {
        if(_activeInvite is null || _cloud is null) return;
        InviteAcceptButton.IsEnabled=InviteDeclineButton.IsEnabled=false;
        try { await AcceptInvitationAndOpenAsync(_activeInvite); InvitePopup.Visibility=InviteBackdrop.Visibility=Visibility.Collapsed; _activeInvite=null; InviteNotificationDot.Visibility=Visibility.Collapsed; _lastInvitations=DateTimeOffset.MinValue; _=CheckInvitationsAsync(); }
        catch(Exception ex) when(ex is not OutOfMemoryException) {ShowCloudFailure("Convite",ex);}
        finally {InviteAcceptButton.IsEnabled=InviteDeclineButton.IsEnabled=true;}
    }
    private async Task AcceptInvitationAndOpenAsync(CloudInvitation invite)
    {
        await _cloud!.AcceptInviteAsync(invite.Id);
        if(Guid.TryParse(invite.WorkspaceId,out var workspaceId)) _acceptedInvitationWorkspaceId=workspaceId;
        QueueCloudSync();
    }
    private async void InviteDecline_Click(object sender,RoutedEventArgs e)
    {
        if(_activeInvite is null || _cloud is null) return;
        InviteAcceptButton.IsEnabled=InviteDeclineButton.IsEnabled=false;
        try { await _cloud.DeclineInviteAsync(_activeInvite.Id); InvitePopup.Visibility=InviteBackdrop.Visibility=Visibility.Collapsed; _activeInvite=null; InviteNotificationDot.Visibility=Visibility.Collapsed; _lastInvitations=DateTimeOffset.MinValue; _=CheckInvitationsAsync(); ShowToast("Convite recusado"); }
        catch(Exception ex) when(ex is not OutOfMemoryException) {ShowCloudFailure("Convite",ex);}
        finally {InviteAcceptButton.IsEnabled=InviteDeclineButton.IsEnabled=true;}
    }
    private void QueueCloudSync()
    {
        if(_applyingCloud || _cloud?.Connected!=true) return;
        _syncDebounce.Stop(); _syncDebounce.Start();
        CloudStatusIcon.Text=_activeWorkspace.SyncMode==WorkspaceSyncMode.Local?"✓":"↑";
        if(_activeWorkspace.SyncMode!=WorkspaceSyncMode.Local) CloudStatusText.Text="Salvando";
    }
    private async Task RunCloudSyncAsync()
    {
        if(_cloudBusy) { _syncRequestedWhileBusy=true; return; }
        if(_cloudActionBusy || _cloud?.Connected!=true || string.IsNullOrEmpty(_cloud.User?.Username)
            || Mouse.Captured is not null || _isSwitchingWorkspace || FolderOverlay.Visibility==Visibility.Visible || _applyingCloud
            || _boardObjectRealtimeBusy || _pendingRealtimeBoardObjects.Count>0)
            return;
        _cloudBusy=true;
        var profileGeneration=_profileGeneration;
        var boardSnapshot=CloudRules.Serialize(_workspaces);
        var historySnapshot=CloudRules.Serialize(_history);
        try
        {
            await _cloud.SynchronizeAsync(_workspaces,_history);
            _=PrepareActivePresenceAsync();
            if(profileGeneration!=_profileGeneration || _cloud?.Connected!=true) return;
            // An edit may arrive while the network is awaiting. Stage it before applying any remote view.
            if (boardSnapshot!=CloudRules.Serialize(_workspaces) || historySnapshot!=CloudRules.Serialize(_history))
            { _cloud.Stage(_workspaces,_history.ToList()); QueueCloudSync(); return; }
            if(Mouse.Captured is not null || _isSwitchingWorkspace || FolderOverlay.Visibility==Visibility.Visible
                || _boardObjectRealtimeBusy || _pendingRealtimeBoardObjects.Count>0) return;
            // Focus may have entered an editor while the network was awaiting.
            // Applying a snapshot now would replace its TextBox and redirect typing.
            if(_editingBoardObjectId is not null || WorkspaceCanvas.Children.OfType<BoardObjectView>().Any(view=>view.IsEditingPluginInput))
                return;
            var result=_cloud.Materialize();
            var local=_workspaces.Where(b=>b.SyncMode==WorkspaceSyncMode.Local).ToList();
            var activeId=_acceptedInvitationWorkspaceId ?? _activeWorkspace.Id;
            var combined=local.Concat(result.Boards).ToList();
            if(combined.Count==0) combined.Add(new WorkspaceBoard());
            var before=CloudRules.Serialize(_workspaces);
            var boardsChanged=before!=CloudRules.Serialize(combined);
            var acceptedBoardAvailable=_acceptedInvitationWorkspaceId is Guid acceptedId
                && combined.Any(board=>board.Id==acceptedId);
            if(boardsChanged || acceptedBoardAvailable)
            {
                _applyingCloud=true;
                var sameBoard=activeId==_activeWorkspace.Id && combined.Any(board=>board.Id==activeId);
                if(sameBoard)
                {
                    var incoming=combined.First(board=>board.Id==activeId);
                    if(!ReferenceEquals(incoming,_activeWorkspace)
                        && CloudRules.Serialize(incoming)!=CloudRules.Serialize(_activeWorkspace))
                        ApplySharedBoardSnapshot(incoming);
                    combined[combined.IndexOf(incoming)]=_activeWorkspace;
                }
                if(boardsChanged) { _workspaces.Clear(); _workspaces.AddRange(combined); }
                _activeWorkspace=_workspaces.FirstOrDefault(b=>b.Id==activeId) ?? _workspaces[0]; _items=_activeWorkspace.Items;
                if(_acceptedInvitationWorkspaceId==_activeWorkspace.Id)
                {
                    _acceptedInvitationWorkspaceId=null;
                    ShowToast("Mesa compartilhada adicionada");
                }
                if(!sameBoard)
                {
                    ClearSelection(); _undoStack.Clear(); _redoStack.Clear(); UpdateUndoRedoButtons();
                    RenderAllItems(false);
                }
                EnsureWorkspaceExtent(); UpdateWorkspacePresentation(); _=PrepareActivePresenceAsync();
            }
            if(_cloud.SyncHistory && historySnapshot!=CloudRules.Serialize(result.History))
            {
                _history.Clear(); foreach(var entry in result.History) _history.Add(entry);
            }
            if(boardsChanged)_storageService.SaveWorkspaces(_workspaces);
            if(historySnapshot!=CloudRules.Serialize(_history))_storageService.SaveHistory(_history);
            _cloud.ObservePresentation(_workspaces,_history);
            if(_activeWorkspace.SyncMode!=WorkspaceSyncMode.Local && DateTimeOffset.UtcNow-_lastMembers>TimeSpan.FromSeconds(60))
            { _lastMembers=DateTimeOffset.UtcNow; await UpdateMembersAsync(); }
        }
        catch(Exception e) when(e is not OutOfMemoryException) { ShowCloudFailure("Nuvem",e); }
        finally
        {
            _applyingCloud=false; _cloudBusy=false; UpdateCloudPresentation();
            if(_syncRequestedWhileBusy) { _syncRequestedWhileBusy=false; QueueCloudSync(); }
        }
    }
    private void ShowCloudFailure(string area,Exception error)
    {
        if(_isExitRequested || DateTimeOffset.UtcNow-_lastCloudFailureToast<TimeSpan.FromSeconds(30)) return;
        _lastCloudFailureToast=DateTimeOffset.UtcNow;
        ShowToast(area+": "+error.Message);
    }
    private void UpdateCloudPresentation()
    {
        if(_cloud is null || _activeWorkspace is null) return;
        var local=_activeWorkspace.SyncMode==WorkspaceSyncMode.Local;
        var synchronized=local || _cloud.Status is "Sincronizado" || _cloud.Status.StartsWith("Mesa salva",StringComparison.Ordinal);
        var syncing=_cloud.Connected && !synchronized;
        CloudStatusText.Text=syncing?"Salvando":"Salvo";
        CloudStatusButton.ToolTip=$"{(_activeWorkspace.SyncMode==WorkspaceSyncMode.Shared?"Mesa compartilhada":local?"Mesa local":"Nuvem pessoal")} · {(_cloud.Connected||local?_cloud.Status:"entre para sincronizar")} · clique para alterar";
        CloudStatusIcon.Text=syncing?"↑":"✓";
        CloudAccountText.Text=_cloud.User is { } user ? "@"+(user.Username ?? "escolher nome") : "Entrar com Google";
        AccountInitial.Text=_cloud.User?.Name.FirstOrDefault().ToString() ?? "G";
        if(_avatarUrl!=_cloud.User?.Picture)
        {
            _avatarUrl=_cloud.User?.Picture;
            while(AccountAvatar.Children.Count>2) AccountAvatar.Children.RemoveAt(2);
            if(Uri.TryCreate(_avatarUrl,UriKind.Absolute,out var picture) && picture.Scheme=="https")
            {
                var bitmap=new BitmapImage(); bitmap.BeginInit();bitmap.UriSource=picture;bitmap.DecodePixelWidth=48;bitmap.EndInit();
                AccountAvatar.Children.Add(new Image {Source=bitmap,Width=22,Height=22,Clip=new EllipseGeometry(new Point(11,11),11,11)});
            }
        }
        foreach(var card in WorkspaceCanvas.Children.OfType<ItemCard>()) card.SetCloudPresentation(_cloud.User?.Id,!local);
        if(local) CollaboratorAvatars.Children.Clear();
    }
    private void StartPanel(string title)
    {
        CloudPanelContent.Children.Clear();
        var header=new DockPanel(); var close=new Button {Content="×",Width=24,Height=24,HorizontalAlignment=HorizontalAlignment.Right};
        close.SetResourceReference(ForegroundProperty,"MutedBrush"); close.Click+=(_,_)=>CloudPanel.Visibility=Visibility.Collapsed;
        DockPanel.SetDock(close,Dock.Right); header.Children.Add(close); header.Children.Add(Label(title,14,true)); CloudPanelContent.Children.Add(header);
        CloudPanel.Visibility=Visibility.Visible;
        CloudPanel.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(140)));
    }
    private static TextBlock Label(string text,double size=12,bool bold=false)
    {
        var label=new TextBlock {Text=text,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,7)};
        label.SetResourceReference(TextBlock.ForegroundProperty,bold?"TextBrush":"MutedBrush"); return label;
    }
    private Button PanelButton(string text,Func<Task> action)
    {
        var button=new Button {Content=Label(text,12),Padding=new Thickness(10,7,10,2),HorizontalContentAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,3,0,0)};
        button.Click+=async (_,_)=>
        {
            button.IsEnabled=false;
            try { await action(); }
            catch(OperationCanceledException) { CloudPanelContent.Children.Add(Label("Ação cancelada.")); }
            catch(Exception e)
            {
                // A cloud or storage fault must remain inside this panel: a
                // completed sign-in must never terminate the desktop app.
                var message=e is HttpRequestException or IOException or InvalidOperationException or ArgumentException
                    ? e.Message
                    : "Não foi possível concluir esta ação. Tente novamente.";
                CloudPanelContent.Children.Add(Label(message));
            }
            finally {button.IsEnabled=true;}
        };
        CloudPanelContent.Children.Add(button); return button;
    }
    private TextBox PanelInput(string hint)
    {
        CloudPanelContent.Children.Add(Label(hint,11));
        var text=new TextBox {Height=32,Padding=new Thickness(8,5,8,5),FontSize=13,BorderThickness=new Thickness(0),Background=Brushes.Transparent}; text.SetResourceReference(TextBox.ForegroundProperty,"TextBrush");
        var border=new Border {CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1),Child=text}; border.SetResourceReference(Border.BorderBrushProperty,"BorderBrush"); CloudPanelContent.Children.Add(border); return text;
    }
    private async void CloudAccount_Click(object sender,RoutedEventArgs e)
    {
        e.Handled=true; if(_cloud is null) return;
        if(_cloud.Connected) { await ShowAccountAsync(); return; }
        StartPanel("Conectar com Google");
        CloudPanelContent.Children.Add(Label("Mesas continuam locais até você escolher sincronizar. Seu histórico continua privado. O Google é usado apenas para confirmar sua identidade; as mesas ficam protegidas na nuvem do ClipDesk."));
        PanelButton("Entrar com Google",async ()=>
        {
            _loginCancellation=new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var importLocal=_storageService.Profile=="local";
            await _cloud.SignInAsync(_loginCancellation.Token);
            var previous=CloudRules.Deserialize<List<WorkspaceBoard>>(CloudRules.Serialize(_workspaces));
            var previousHistory=CloudRules.Deserialize<List<ClipboardHistoryEntry>>(CloudRules.Serialize(_history));
            _storageService.SaveWorkspaces(_workspaces); _storageService.SaveHistory(_history);
            _storageService.SwitchProfile(_cloud.User!.Id); _cloud.LoadPaths();
            _profileGeneration++;
            var existing=_storageService.Database.Read("workspaces.json");
            if(existing is null)
            {
                if(!importLocal) {previous=[new WorkspaceBoard()];previousHistory=[];}
                foreach(var board in previous) { board.SyncMode=WorkspaceSyncMode.Local; board.OwnerId=null; }
                _storageService.SaveWorkspaces(previous); _storageService.SaveHistory(previousHistory);
            }
            ReloadProfile();
            if(string.IsNullOrEmpty(_cloud.User.Username)) ShowUsernamePanel(); else await ShowAccountAsync();
            await CheckInvitationsAsync();
            QueueCloudSync();
        });
    }
    private void ShowUsernamePanel()
    {
        StartPanel("Escolha seu username");
        CloudPanelContent.Children.Add(Label("É por ele que outras pessoas poderão convidar você para uma mesa."));
        var input=PanelInput("3–24 letras, números ou _");
        PanelButton("Salvar username",async ()=> { await _cloud!.SetUsernameAsync(input.Text.Trim().TrimStart('@')); await ShowAccountAsync(); QueueCloudSync(); }); input.Focus();
    }
    private async Task ShowAccountAsync()
    {
        StartPanel(_cloud!.User!.Name); CloudPanelContent.Children.Add(Label("@"+(_cloud.User.Username ?? "escolher nome"),12));
        CloudPanelContent.Children.Add(Label("Google conectado · nuvem do ClipDesk pronta",11));
        if(_cloud.Status.StartsWith("Reconecte",StringComparison.Ordinal))
            PanelButton("Reconectar Google",async ()=>
            {
                _loginCancellation=new CancellationTokenSource(TimeSpan.FromMinutes(5));
                await _cloud.SignInAsync(_loginCancellation.Token);await ShowAccountAsync();QueueCloudSync();
            });
        foreach(var attachment in _history.SelectMany(e=>e.Attachments).Where(a=>a.IsDrive && !a.Uploaded && a.OwnerId==_cloud.User.Id).Take(5))
            PanelButton("↑ "+attachment.Name,async ()=> {await _cloud.UploadDriveAsync(attachment,null);_storageService.SaveHistory(_history);_cloud.Stage(_workspaces,_history.ToList());QueueCloudSync();await ShowAccountAsync();});
        if(string.IsNullOrEmpty(_cloud.User.Username)) PanelButton("Escolher username",()=> {ShowUsernamePanel(); return Task.CompletedTask;});
        PanelButton(_cloud.SyncHistory?"✓ Histórico pessoal na nuvem":"Histórico somente neste computador",()=> { _cloud.SyncHistory=!_cloud.SyncHistory; QueueCloudSync(); return ShowAccountAsync(); });
        if(_storageService.Database.ConflictCount>0) PanelButton($"Revisar {_storageService.Database.ConflictCount} conflito(s)",()=> {ShowConflicts();return Task.CompletedTask;});
        try
        {
            var invitations=await _cloud.InvitationsAsync();
            foreach(var invite in invitations)
                PanelButton($"Aceitar: {invite.WorkspaceName} · @{invite.FromUsername}",async ()=> {await AcceptInvitationAndOpenAsync(invite); CloudPanel.Visibility=Visibility.Collapsed;});
        }
        catch(HttpRequestException) {CloudPanelContent.Children.Add(Label("Convites indisponíveis enquanto estiver offline.",11));}
        PanelButton("Sair",async ()=>
        {
            _profileGeneration++;
            await _cloud.SignOutAsync(); _storageService.SwitchProfile("local"); _cloud.LoadPaths(); ReloadProfile(); CloudPanel.Visibility=Visibility.Collapsed;
        });
    }
    private void ReloadProfile()
    {
        _remoteCursors.Clear();_remoteSelections.Clear();CollaboratorAvatars.Children.Clear();
        _workspaces.Clear(); _workspaces.AddRange(_storageService.LoadWorkspaces()); _activeWorkspace=_workspaces[0]; _items=_activeWorkspace.Items;
        _history.Clear(); foreach(var entry in _storageService.LoadHistory()) _history.Add(entry);
        ClearSelection(); _undoStack.Clear(); _redoStack.Clear(); RenderAllItems(false); UpdateWorkspacePresentation(); UpdateCloudPresentation();
    }
    private void ShowConflicts()
    {
        StartPanel("Alterações preservadas");
        CloudPanelContent.Children.Add(Label("Recupere sua versão em uma nova mesa local. A versão compartilhada será preservada."));
        foreach(var conflict in _storageService.Database.Conflicts())
        {
            var name=conflict.Data["name"]?.GetValue<string>() ?? conflict.Data["title"]?.GetValue<string>() ?? "Alteração";
            PanelButton("Recuperar: "+name,async ()=>
            {
                var item=new ClipboardItem {Type=ClipboardItemType.Text,DisplayName=name,Text=conflict.Data["text"]?.GetValue<string>() ?? conflict.Data["url"]?.GetValue<string>() ?? name,X=60,Y=150};
                // Preserve every field and attachment reference before dismissing the conflict.
                var directory=Path.Combine(_storageService.BaseDirectory,"Recovery");Directory.CreateDirectory(directory);
                var snapshot=Path.Combine(directory,conflict.Id+".json");
                File.WriteAllText(snapshot,conflict.Data.ToJsonString(new JsonSerializerOptions {WriteIndented=true}));
                var board=new WorkspaceBoard {Name="Recuperação · "+name,Items=[item,new ClipboardItem {Type=ClipboardItemType.File,DisplayName="Registro completo da alteração",FilePaths=[snapshot],X=440,Y=150}]};
                _workspaces.Add(board);_storageService.SaveWorkspaces(_workspaces);_storageService.Database.ResolveConflict(conflict.Id);
                await SwitchWorkspaceAsync(board);Save();CloudPanel.Visibility=Visibility.Collapsed;
            });
        }
    }
    private void CloudMode_Click(object sender,RoutedEventArgs e)
    {
        e.Handled=true;
        if(WorkspaceDropdown.Visibility==Visibility.Visible) ShowWorkspaceModePage(_activeWorkspace);
        else OpenWorkspaceDropdown(_activeWorkspace);
    }
    private async Task SetWorkspaceModeFromDropdownAsync(WorkspaceBoard target,WorkspaceSyncMode mode)
    {
        if(!_workspaces.Contains(target)) {HideWorkspaceDropdown();return;}
        if(mode==target.SyncMode)
        {
            HideWorkspaceDropdown();
            return;
        }
        if(mode==WorkspaceSyncMode.Local)
        {
            HideWorkspaceDropdown(immediate:true);
            if(_cloud?.Connected!=true) {ShowToast("Conecte o Google para retirar esta mesa da nuvem com segurança.");return;}
            StartPanel("Manter somente local");
            CloudPanelContent.Children.Add(Label("A mesa será retirada da nuvem e do acesso dos colaboradores. A cópia deste computador será mantida. Anexos locais não são enviados por esta etapa."));
            PanelButton("Retirar mesa da nuvem",()=>WithWorkspaceActionAsync(target,board=>_cloud.MakeLocalAsync(board)));
            return;
        }
        if(mode==WorkspaceSyncMode.PersonalCloud)
        {
            HideWorkspaceDropdown(immediate:true);
            if(_cloud?.Connected!=true) {CloudAccount_Click(this,new RoutedEventArgs()); return;}
            if(target.SyncMode==WorkspaceSyncMode.Shared)
            {
                StartPanel("Tornar mesa pessoal");CloudPanelContent.Children.Add(Label("Os colaboradores perderão o acesso à mesa. Cópias locais que já existam nos computadores deles continuarão lá."));
                PanelButton("Remover compartilhamento",()=>WithWorkspaceActionAsync(target,board=>_cloud.MakePersonalAsync(board)));
                return;
            }
            target.SyncMode=WorkspaceSyncMode.PersonalCloud; target.OwnerId=_cloud.User!.Id; Save(); CloudPanel.Visibility=Visibility.Collapsed; await RunCloudSyncAsync();
            UpdateWorkspacePresentation();
            return;
        }
        HideWorkspaceDropdown(immediate:true);
        if(target.Id!=_activeWorkspace.Id) await SwitchWorkspaceAsync(target);
        Invite_Click(this,new RoutedEventArgs());
    }
    private void RequestWorkspaceDeletion(WorkspaceBoard board)
    {
        HideWorkspaceDropdown(immediate:true);
        if(!CanDeleteWorkspace(board)) {ShowToast("Somente o proprietário pode excluir esta mesa.");return;}
        StartPanel("Excluir mesa");
        CloudPanelContent.Children.Add(Label($"“{board.Name}” e todo o conteúdo da mesa serão removidos. Esta ação não pode ser desfeita."));
        PanelButton("Cancelar",()=> {CloudPanel.Visibility=Visibility.Collapsed;return Task.CompletedTask;});
        var delete=PanelButton("Excluir completamente",()=>DeleteWorkspaceCompletelyAsync(board));
        delete.Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FB7185"));
    }
    private async Task DeleteWorkspaceCompletelyAsync(WorkspaceBoard board)
    {
        if(!_workspaces.Contains(board)) return;
        if(board.SyncMode!=WorkspaceSyncMode.Local)
        {
            if(_cloud?.Connected!=true) throw new InvalidOperationException("Conecte o Google para excluir esta mesa da nuvem.");
            await _cloud.DeleteWorkspaceAsync(board);
        }
        var deletingActive=board.Id==_activeWorkspace.Id;
        _appearanceSettings.BoardViewports.Remove(board.Id.ToString("N"));
        _workspaces.Remove(board);
        if(_workspaces.Count==0) _workspaces.Add(new WorkspaceBoard {Name="Mesa principal"});
        if(deletingActive)
        {
            _activeWorkspace=_workspaces[0];_items=_activeWorkspace.Items;
            HideFolderOverlay();ClearSelection();WorkspaceCanvas.Children.Clear();
            ApplyWorkspaceViewport(_activeWorkspace);RenderAllItems(false);EnsureWorkspaceExtent();UpdateWorkspacePresentation();
        }
        _storageService.SaveWorkspaces(_workspaces);SaveAppearanceSettings();
        CloudPanel.Visibility=Visibility.Collapsed;QueueCloudSync();ShowToast("Mesa excluída");
    }
    private Task WithWorkspaceActionAsync(Func<WorkspaceBoard,Task> action)=>WithWorkspaceActionAsync(_activeWorkspace,action);
    private async Task WithWorkspaceActionAsync(WorkspaceBoard target,Func<WorkspaceBoard,Task> action)
    {
        if(_cloudActionBusy) throw new InvalidOperationException("Aguarde a alteração da mesa terminar.");
        var id=target.Id;var generation=_profileGeneration;_cloudActionBusy=true;
        try
        {
            while(_cloudBusy) await Task.Delay(50);
            if(generation!=_profileGeneration || _cloud?.Connected!=true) throw new InvalidOperationException("Entre novamente antes de alterar esta mesa.");
            var board=_workspaces.FirstOrDefault(b=>b.Id==id) ?? throw new InvalidOperationException("Esta mesa não está mais disponível.");
            await action(board);Save();
            if(board.Id==_activeWorkspace.Id) {RenderAllItems(false);UpdateWorkspacePresentation();}
            CloudPanel.Visibility=Visibility.Collapsed;
        }
        finally {_cloudActionBusy=false;UpdateCloudPresentation();QueueCloudSync();}
    }
    private void Invite_Click(object sender,RoutedEventArgs e)
    {
        e.Handled=true;
        if(_cloud?.Connected!=true) { CloudAccount_Click(sender,e); return; }
        if(string.IsNullOrEmpty(_cloud.User?.Username)) {ShowUsernamePanel();return;}
        StartPanel("Compartilhar mesa");
        var workspaceId=_activeWorkspace.Id;
        CloudPanelContent.Children.Add(Label("Convidados poderão ver e editar esta mesa. Seu histórico pessoal não será compartilhado."));
        if(_activeWorkspace.SyncMode==WorkspaceSyncMode.Local) CloudPanelContent.Children.Add(Label("Enviar o convite também ativa a nuvem para esta mesa.",11));
        var username=PanelInput("Convidar pelo @username");
        PanelButton("Enviar convite",async ()=>
        {
            while(_cloudBusy || _cloudActionBusy) await Task.Delay(50);
            var board=_workspaces.FirstOrDefault(b=>b.Id==workspaceId) ?? throw new InvalidOperationException("Esta mesa não está mais disponível.");
            await _cloud.InviteAsync(board,username.Text.Trim().TrimStart('@'));
            board.SyncMode=WorkspaceSyncMode.Shared; board.OwnerId=_cloud.User!.Id; Save();
            await RunCloudSyncAsync();
            CloudPanelContent.Children.Add(Label("Convite enviado. A mesa ficará compartilhada quando ele for aceito."));
        });
    }
    private async void Card_CloudFileAction(object? sender,EventArgs e)
    {
        if(sender is not ItemCard card) return;
        var downloaded=card.Item.Attachments.FirstOrDefault(attachment=>attachment.IsDrive && attachment.Uploaded && File.Exists(attachment.LocalPath)
            && StorageService.IsUserDownload(attachment.LocalPath));
        if(downloaded?.LocalPath is { } downloadedPath)
        {
            var explorer=new ProcessStartInfo("explorer.exe") {UseShellExecute=true};
            explorer.ArgumentList.Add("/select,");explorer.ArgumentList.Add(downloadedPath);
            Process.Start(explorer);return;
        }
        if(_cloud?.Connected!=true) {CloudAccount_Click(this,new RoutedEventArgs());return;}
        try
        {
            foreach(var attachment in card.Item.Attachments.Where(a=>a.IsDrive))
            {
                if(!attachment.Uploaded && attachment.OwnerId==_cloud.User!.Id)
                {
                    await _cloud.UploadDriveAsync(attachment,_activeWorkspace,new Progress<double>(progress=>{CloudStatusIcon.Text="↑";CloudStatusText.Text=$"{progress:P0}";}));
                }
                else if(attachment.Uploaded && (!File.Exists(attachment.LocalPath) || attachment.OwnerId!=_cloud.User!.Id && !StorageService.IsUserDownload(attachment.LocalPath)))
                    await _cloud.DownloadAsync(attachment,saveForUser:true);
            }
            card.SetCloudPresentation(_cloud.User!.Id,true); Save(); QueueCloudSync(); ShowToast($"Arquivo salvo em Documentos\\ClipDesk\\Downloads");
        }
        catch(Exception ex) when(ex is IOException or HttpRequestException or InvalidOperationException) {ShowToast(ex.Message);}
    }
    private bool TryOpenCloudFile(ClipboardItem item)
    {
        var drive=item.Attachments.FirstOrDefault(a=>a.IsDrive);
        if(drive is null || _activeWorkspace.SyncMode==WorkspaceSyncMode.Local) return false;
        if(drive.OwnerId==(_cloud?.User?.Id ?? _storageService.Profile))
        {
            var path=drive.LocalPath;
            if(path is null || !File.Exists(path)) {ShowToast(drive.Uploaded?"Use a seta de baixar para salvar o arquivo neste computador.":"Arquivo local indisponível neste computador.");return true;}
            Process.Start(new ProcessStartInfo(path) {UseShellExecute=true}); return true;
        }
        if(!drive.Uploaded || drive.DriveFileId is null) {ShowToast("Aguardando upload do proprietário.");return true;}
        Process.Start(new ProcessStartInfo($"https://drive.google.com/file/d/{Uri.EscapeDataString(drive.DriveFileId)}/view") {UseShellExecute=true}); return true;
    }
    private static SolidColorBrush PresenceColor(string id)
    {
        var palette=new[]{"#22D3EE","#FB7185","#A78BFA","#34D399","#FBBF24","#60A5FA"};
        var hash=SHA256.HashData(Encoding.UTF8.GetBytes(id)); return new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette[hash[0]%palette.Length]));
    }
    private SolidColorBrush LocalPresenceColor() => PresenceColor(_cloud?.User?.Id ?? _storageService.Profile);
    private async Task UpdateMembersAsync()
    {
        if(_cloud?.Connected!=true) return;
        var id=_activeWorkspace.Id; var members=await _cloud.MembersAsync(_activeWorkspace); if(id!=_activeWorkspace.Id) return;
        CollaboratorAvatars.Children.Clear();
        foreach(var member in members.Where(m=>m.UserId!=_cloud.User!.Id).Take(3))
        {
            var color=PresenceColor(member.UserId); var avatar=new Grid {Width=26,Height=26};
            avatar.Children.Add(new System.Windows.Shapes.Ellipse {Stroke=color,StrokeThickness=1.5,Fill=new SolidColorBrush(Color.FromArgb(24,color.Color.R,color.Color.G,color.Color.B))});
            var label=Label(member.Username.FirstOrDefault().ToString(),11,true); label.HorizontalAlignment=HorizontalAlignment.Center; label.VerticalAlignment=VerticalAlignment.Center; label.Margin=new Thickness(0); avatar.Children.Add(label);
            if(Uri.TryCreate(member.Picture,UriKind.Absolute,out var picture) && picture.Scheme=="https")
            {
                var image=new Image {Width=22,Height=22,Clip=new EllipseGeometry(new Point(11,11),11,11)};
                var bitmap=new BitmapImage(); bitmap.BeginInit(); bitmap.UriSource=picture; bitmap.DecodePixelWidth=48; bitmap.EndInit(); image.Source=bitmap; avatar.Children.Add(image);
            }
            var button=new Button {Content=avatar,Padding=new Thickness(3),ToolTip="@"+member.Username};
            void ShowMember()
            {
                StartPanel("@"+member.Username);
                PanelButton(_followedMemberId==member.UserId?"Parar de acompanhar":"Acompanhar visão",()=>ToggleFollowCameraAsync(member));
                PanelButton(_hiddenCursors.Contains(member.UserId)?"Mostrar cursor":"Ocultar cursor",()=> {if(!_hiddenCursors.Add(member.UserId)) _hiddenCursors.Remove(member.UserId); ExpireCursors(); CloudPanel.Visibility=Visibility.Collapsed; return Task.CompletedTask;});
                if(_activeWorkspace.OwnerId==_cloud.User?.Id)
                    PanelButton("Remover colaborador",async ()=>
                    {
                        if(_followedMemberId==member.UserId) StopFollowingCamera(false);
                        await _cloud.RemoveMemberAsync(_activeWorkspace,member);
                        _hiddenCursors.Add(member.UserId); ExpireCursors();
                        await UpdateMembersAsync(); CloudPanel.Visibility=Visibility.Collapsed;
                    });
            }
            var singleClick=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(220)};
            singleClick.Tick+=(_,_)=> {singleClick.Stop();ShowMember();};
            button.PreviewMouseLeftButtonDown+=(_,args)=>
            {
                args.Handled=true;
                if(args.ClickCount>=2) {singleClick.Stop();StopFollowingCamera(false);TravelToCollaborator(member);}
                else {singleClick.Stop();singleClick.Start();}
            };
            button.KeyDown+=(_,args)=> {if(args.Key is Key.Enter or Key.Space) {singleClick.Stop();ShowMember();args.Handled=true;}};
            CollaboratorAvatars.Children.Add(button);
        }
    }
    private Task ToggleFollowCameraAsync(CloudMember member)
    {
        if(_followedMemberId==member.UserId)
        {
            StopFollowingCamera(true);CloudPanel.Visibility=Visibility.Collapsed;return Task.CompletedTask;
        }
        if(!_latestPresence.TryGetValue(member.UserId,out var presence) || presence.WorkspaceId!=_activeWorkspace.Id.ToString("N"))
        {ShowToast($"@{member.Username} ainda não compartilhou a visão nesta mesa");return Task.CompletedTask;}
        _followedMemberId=member.UserId;_followedMemberUsername=member.Username;CloudPanel.Visibility=Visibility.Collapsed;
        StartPresenceFollow(presence);ShowFollowCameraBanner(member.UserId,member.Username);ShowToast($"Acompanhando a visão de @{member.Username}");
        return Task.CompletedTask;
    }
    private void StopFollowingCamera(bool showToast)
    {
        if(_followedMemberId is null) return;
        var username=_followedMemberUsername;_followedMemberId=null;_followedMemberUsername=null;_presenceTravelTimer.Stop();
        _presenceTravelLastTick=0;HideFollowCameraBanner();_appearanceSaveTimer.Stop();_appearanceSaveTimer.Start();
        if(showToast) ShowToast(string.IsNullOrWhiteSpace(username)?"Acompanhamento encerrado":$"Você parou de acompanhar @{username}");
    }
    private void StopFollowCamera_Click(object sender,RoutedEventArgs e)
    {
        StopFollowingCamera(true);e.Handled=true;
    }
    private void ShowFollowCameraBanner(string userId,string username)
    {
        var color=PresenceColor(userId);var translucent=new SolidColorBrush(Color.FromArgb(34,color.Color.R,color.Color.G,color.Color.B));
        FollowCameraAccent.Background=color;FollowCameraAvatarRing.Stroke=color;FollowCameraAvatarRing.Fill=translucent;
        FollowCameraAvatarInitial.Text=username.FirstOrDefault().ToString().ToUpperInvariant();
        FollowCameraTitle.Text=$"Acompanhando @{username}";
        var version=++_followCameraBannerAnimationVersion;
        FollowCameraBanner.Visibility=Visibility.Visible;FollowCameraBanner.BeginAnimation(OpacityProperty,null);FollowCameraBannerTranslate.BeginAnimation(TranslateTransform.YProperty,null);
        FollowCameraBanner.Opacity=0;FollowCameraBannerTranslate.Y=14;
        var ease=new CubicEase{EasingMode=EasingMode.EaseOut};
        FollowCameraBanner.BeginAnimation(OpacityProperty,new DoubleAnimation(1,TimeSpan.FromMilliseconds(170)){EasingFunction=ease});
        var movement=new DoubleAnimation(0,TimeSpan.FromMilliseconds(230)){EasingFunction=ease};
        movement.Completed+=(_,_)=> {if(version==_followCameraBannerAnimationVersion)FollowCameraBannerTranslate.Y=0;};
        FollowCameraBannerTranslate.BeginAnimation(TranslateTransform.YProperty,movement);
    }
    private void HideFollowCameraBanner()
    {
        if(FollowCameraBanner is null||FollowCameraBanner.Visibility!=Visibility.Visible)return;
        var version=++_followCameraBannerAnimationVersion;var ease=new QuadraticEase{EasingMode=EasingMode.EaseIn};
        var fade=new DoubleAnimation(0,TimeSpan.FromMilliseconds(130)){EasingFunction=ease};
        fade.Completed+=(_,_)=> {if(version==_followCameraBannerAnimationVersion)FollowCameraBanner.Visibility=Visibility.Collapsed;};
        FollowCameraBanner.BeginAnimation(OpacityProperty,fade);
        FollowCameraBannerTranslate.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(10,TimeSpan.FromMilliseconds(150)){EasingFunction=ease});
    }
    private void TravelToCollaborator(CloudMember member)
    {
        if(!_latestPresence.TryGetValue(member.UserId,out var presence) || presence.WorkspaceId!=_activeWorkspace.Id.ToString("N"))
        { ShowToast($"@{member.Username} ainda não movimentou o cursor nesta mesa"); return; }
        CloudPanel.Visibility=Visibility.Collapsed;StartPresenceTravel(presence,520);
    }
    private void StartPresenceTravel(CloudPresence presence,double durationMs)
    {
        var zoom=Math.Max(_workspaceZoom,BoardViewport.MinimumZoom);
        _presenceTravelFrom=new((WorkspaceScroll.HorizontalOffset+WorkspaceScroll.ViewportWidth/2)/zoom,(WorkspaceScroll.VerticalOffset+WorkspaceScroll.ViewportHeight/2)/zoom);
        _presenceTravelTo=new(presence.ViewCenterX,presence.ViewCenterY);
        _presenceTravelZoomFrom=zoom;_presenceTravelZoomTo=Math.Clamp(presence.Zoom,BoardViewport.MinimumZoom,1);
        _presenceTravelDurationMs=Math.Clamp(durationMs,120,800);_presenceTravelStarted=DateTimeOffset.UtcNow;_presenceTravelLastTick=0;_presenceTravelTimer.Start();
    }
    private void StartPresenceFollow(CloudPresence presence)
    {
        var zoom=Math.Max(_workspaceZoom,BoardViewport.MinimumZoom);
        _followCameraCenter=new((WorkspaceScroll.HorizontalOffset+WorkspaceScroll.ViewportWidth/2)/zoom,(WorkspaceScroll.VerticalOffset+WorkspaceScroll.ViewportHeight/2)/zoom);
        _followCameraTarget=new(presence.ViewCenterX,presence.ViewCenterY);
        _followCameraZoom=zoom;
        _followCameraZoomTarget=Math.Clamp(presence.Zoom,BoardViewport.MinimumZoom,1);
        _presenceTravelLastTick=Stopwatch.GetTimestamp();
        _presenceTravelTimer.Start();
    }
    private void UpdatePresenceFollowTarget(CloudPresence presence)
    {
        _followCameraTarget=new(presence.ViewCenterX,presence.ViewCenterY);
        _followCameraZoomTarget=Math.Clamp(presence.Zoom,BoardViewport.MinimumZoom,1);
        if(_presenceTravelTimer.IsEnabled)return;
        // Keep the logical camera between packets. ScrollViewer may clamp offsets
        // at the board edge or still have a pending layout from the preceding zoom.
        // Restarting from those offsets creates a new jump whenever motion resumes.
        _presenceTravelLastTick=Stopwatch.GetTimestamp();_presenceTravelTimer.Start();
    }
    private void AdvancePresenceTravel()
    {
        if(_followedMemberId is not null) {AdvancePresenceFollow();return;}
        var elapsed=(DateTimeOffset.UtcNow-_presenceTravelStarted).TotalMilliseconds;
        var progress=Math.Clamp(elapsed/_presenceTravelDurationMs,0,1);
        var eased=progress<.5 ? 4*progress*progress*progress : 1-Math.Pow(-2*progress+2,3)/2;
        var zoom=_presenceTravelZoomFrom+(_presenceTravelZoomTo-_presenceTravelZoomFrom)*eased;
        var center=new Point(_presenceTravelFrom.X+(_presenceTravelTo.X-_presenceTravelFrom.X)*eased,_presenceTravelFrom.Y+(_presenceTravelTo.Y-_presenceTravelFrom.Y)*eased);
        ApplyPresenceViewport(center,zoom);
        if(progress>=1) { _presenceTravelTimer.Stop();_appearanceSaveTimer.Stop();_appearanceSaveTimer.Start(); }
    }
    private void AdvancePresenceFollow()
    {
        var now=Stopwatch.GetTimestamp();
        var elapsed=_presenceTravelLastTick==0?1d/60:(double)(now-_presenceTravelLastTick)/Stopwatch.Frequency;
        _presenceTravelLastTick=now;
        var dt=Math.Clamp(elapsed,1d/240,1d/15);
        var distance=(_followCameraTarget-_followCameraCenter).Length;
        var visibleWorld=Math.Max(WorkspaceScroll.ViewportWidth,WorkspaceScroll.ViewportHeight)/Math.Max(_followCameraZoom,BoardViewport.MinimumZoom);
        var response=distance>visibleWorld*.7?42d:30d;
        var positionBlend=1-Math.Exp(-response*dt);
        var zoomBlend=1-Math.Exp(-26d*dt);
        _followCameraCenter=new(
            _followCameraCenter.X+(_followCameraTarget.X-_followCameraCenter.X)*positionBlend,
            _followCameraCenter.Y+(_followCameraTarget.Y-_followCameraCenter.Y)*positionBlend);
        _followCameraZoom+=(_followCameraZoomTarget-_followCameraZoom)*zoomBlend;
        var centerSettled=(_followCameraTarget-_followCameraCenter).Length<.08;
        var zoomSettled=Math.Abs(_followCameraZoomTarget-_followCameraZoom)<.00008;
        if(centerSettled)_followCameraCenter=_followCameraTarget;
        if(zoomSettled)_followCameraZoom=_followCameraZoomTarget;
        ApplyPresenceViewport(_followCameraCenter,_followCameraZoom);
        if(centerSettled&&zoomSettled)_presenceTravelTimer.Stop();
    }
    private void ApplyPresenceViewport(Point center,double zoom)
    {
        _applyingPresenceTravel=true;
        try
        {
            var zoomChanged=Math.Abs(_workspaceZoom-zoom)>.00005;
            _workspaceZoom=zoom;
            if(Math.Abs(ZoomSlider.Value-zoom*100)>.005)ZoomSlider.Value=zoom*100;
            if(zoomChanged)
            {
                WorkspaceBoardScale.BeginAnimation(ScaleTransform.ScaleXProperty,null);WorkspaceBoardScale.BeginAnimation(ScaleTransform.ScaleYProperty,null);
                WorkspaceBoardScale.ScaleX=zoom;WorkspaceBoardScale.ScaleY=zoom;WorkspaceExtentHost.Width=_activeWorkspace.WorldWidth*zoom;WorkspaceExtentHost.Height=_activeWorkspace.WorldHeight*zoom;
                ZoomValueText.Text=$"{Math.Round(zoom*100):0}%";UpdateRemoteCursorScales();
            }
            WorkspaceScroll.ScrollToHorizontalOffset(Math.Clamp(center.X*zoom-WorkspaceScroll.ViewportWidth/2,0,WorkspaceScroll.ScrollableWidth));
            WorkspaceScroll.ScrollToVerticalOffset(Math.Clamp(center.Y*zoom-WorkspaceScroll.ViewportHeight/2,0,WorkspaceScroll.ScrollableHeight));
        }
        finally { _applyingPresenceTravel=false; }
    }
    private void RenderPresence(CloudPresence presence)
    {
        if(presence.WorkspaceId!=_activeWorkspace.Id.ToString("N")) return;
        MotionDiagnostics.Record(MotionDiagnostics.Stage.Applied);
        _latestPresence[presence.UserId]=presence;
        if(_followedMemberId==presence.UserId) UpdatePresenceFollowTarget(presence);
        ApplyDragPreviews(presence);
        if(_hiddenCursors.Contains(presence.UserId)) return;
        // Remote outlines exist only while dragging, never for retained selection.
        var draggingItemId=presence.Drags?.FirstOrDefault(drag=>drag.Id==presence.ItemId)?.Id
            ?? presence.Drags?.FirstOrDefault()?.Id;
        var signature=presence.Username;
        if(_remoteCursors.TryGetValue(presence.UserId,out var existing) && Equals(existing.Visual.Tag,signature)
            && existing.Visual.Parent==WorkspaceCanvas
            && existing.Visual.RenderTransform is TranslateTransform position)
        {
            SmoothRemoteCursor(position,presence.X-7,presence.Y-7);
            _remoteCursors[presence.UserId]=(existing.Visual,DateTimeOffset.UtcNow);
            if(_remoteSelections.GetValueOrDefault(presence.UserId)!=draggingItemId)
            {
                _remoteSelections[presence.UserId]=draggingItemId;
                RefreshRemoteSelections();
            }
            return;
        }
        if(_remoteCursors.Remove(presence.UserId,out var previous)) WorkspaceCanvas.Children.Remove(previous.Visual);
        var color=PresenceColor(presence.UserId); var cursor=new Canvas {Width=120,Height=35,IsHitTestVisible=false,Tag=signature,RenderTransform=new TranslateTransform(presence.X-7,presence.Y-7)};
        var marker=new Canvas {Width=120,Height=35,Tag="presence-marker",RenderTransformOrigin=new Point(0,0)};
        var ring=new Ellipse {Width=14,Height=14,Stroke=color,StrokeThickness=1.4,Fill=new SolidColorBrush(Color.FromArgb(28,color.Color.R,color.Color.G,color.Color.B)),Effect=new System.Windows.Media.Effects.DropShadowEffect {BlurRadius=4,ShadowDepth=1,Opacity=.25}};
        var dot=new Ellipse {Width=3,Height=3,Fill=color}; Canvas.SetLeft(dot,5.5);Canvas.SetTop(dot,5.5);marker.Children.Add(ring);marker.Children.Add(dot);
        var name=Label("@"+presence.Username,10);name.Foreground=color;Canvas.SetLeft(name,18);Canvas.SetTop(name,-1);marker.Children.Add(name);cursor.Children.Add(marker);
        marker.RenderTransform=new ScaleTransform(1/Math.Max(_workspaceZoom,BoardViewport.MinimumZoom),1/Math.Max(_workspaceZoom,BoardViewport.MinimumZoom));
        Panel.SetZIndex(cursor,100);WorkspaceCanvas.Children.Add(cursor);
        _cursorExpiryTimer.Start();
        _remoteCursors[presence.UserId]=(cursor,DateTimeOffset.UtcNow);
        _remoteSelections[presence.UserId]=draggingItemId;
        RefreshRemoteSelections();
    }
    private void UpdateRemoteCursorScales()
    {
        var inverse=1/Math.Max(_workspaceZoom,BoardViewport.MinimumZoom);
        foreach(var marker in _remoteCursors.Values.SelectMany(entry=>entry.Visual.FindVisualChildren<Canvas>()).Where(canvas=>Equals(canvas.Tag,"presence-marker")))
            marker.RenderTransform=new ScaleTransform(inverse,inverse);
    }
    private void RefreshRemoteSelections()
    {
        foreach(var card in WorkspaceCanvas.Children.OfType<ItemCard>())
        {
            var selected=_remoteSelections.FirstOrDefault(p=>p.Value==card.Item.Id && _remoteCursors.ContainsKey(p.Key));
            card.SetCollaboratorSelection(selected.Key is null?null:PresenceColor(selected.Key));
        }
        foreach(var view in WorkspaceCanvas.Children.OfType<BoardObjectView>())
        {
            var selected=_remoteSelections.FirstOrDefault(p=>p.Value==view.Object.Id && _remoteCursors.ContainsKey(p.Key));
            view.SetCollaboratorSelection(selected.Key is null?null:PresenceColor(selected.Key));
        }
    }
    private void ExpireCursors()
    {
        var changed=false;
        foreach(var entry in _remoteCursors.Where(e=>_hiddenCursors.Contains(e.Key) || DateTimeOffset.UtcNow-e.Value.Seen>TimeSpan.FromSeconds(8)).ToList())
        {
            if(_followedMemberId==entry.Key && !_hiddenCursors.Contains(entry.Key)) StopFollowingCamera(true);
            WorkspaceCanvas.Children.Remove(entry.Value.Visual);_remoteCursors.Remove(entry.Key);_remoteSelections.Remove(entry.Key);changed=true;
        }
        if(changed) RefreshRemoteSelections();
        if(_remoteCursors.Count==0) _cursorExpiryTimer.Stop();
    }
}
