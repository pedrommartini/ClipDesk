using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using ClipDesk;
using ClipDesk.Core;
using ClipDesk.Models;
using ClipDesk.Services;
using ClipDesk.Views;

internal static class CloudVisualChecks
{
    public static void Run(Application app)
    {
        var storage=new StorageService(); storage.SaveSettings(new AppSettings {IsDarkMode=true});
        var pending=new ClipboardItem {Type=ClipboardItemType.File,DisplayName="Vídeo da campanha.mp4",X=630,Y=170,Width=340,Height=220,Attachments=[new CloudAttachment {OwnerId="owner",Name="Vídeo da campanha.mp4",IsDrive=true,Size=86_000_000,Uploaded=false}]};
        storage.SaveWorkspaces([new WorkspaceBoard {Name="Mesa principal",SyncMode=WorkspaceSyncMode.PersonalCloud,OwnerId="owner",Items=[new ClipboardItem {Type=ClipboardItemType.Text,DisplayName="Ideias do projeto",Text="Uma mesa local, na nuvem ou compartilhada.\nVocê escolhe como trabalhar.",X=60,Y=170,Width=380,Height=230},pending]}]);
        var window=new MainWindow {WindowState=WindowState.Normal};
        var content=(FrameworkElement)window.Content;
        typeof(MainWindow).GetMethod("RenderAllItems",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[false,false]);
        var canvas=(Canvas)window.FindName("WorkspaceCanvas");
        var fileCard=canvas.Children.OfType<ItemCard>().Single(c=>c.Item.Id==pending.Id);
        fileCard.SetCloudPresentation("owner",true);
        if(((Button)fileCard.FindName("CloudFileAction")).Visibility!=Visibility.Visible) throw new Exception("Owner on a second device cannot download the missing local file.");
        var fixture=Path.Combine(storage.AssetsDirectory,"cloud-visual.bin");Directory.CreateDirectory(storage.AssetsDirectory);File.WriteAllBytes(fixture,[1]);
        fileCard.Item.Attachments[0].LocalPath=fixture;fileCard.SetCloudPresentation("owner",true);
        if(((Grid)fileCard.FindName("Root")).Opacity>=.6) throw new Exception("Pending card is not dimmed.");
        if(((Button)fileCard.FindName("CloudFileAction")).Visibility!=Visibility.Visible) throw new Exception("Pending upload action missing.");
        fileCard.Item.Attachments[0].Uploaded=true;fileCard.Item.Attachments[0].LocalPath=null;
        fileCard.SetCloudPresentation("collaborator",true);
        if(((Grid)fileCard.FindName("Root")).Opacity!=1 || !((string)((Button)fileCard.FindName("CloudFileAction")).ToolTip).Contains("Baixar")) throw new Exception("Uploaded collaborator card does not show download action.");
        fileCard.Item.Attachments[0].LocalPath=fixture;fileCard.SetCloudPresentation("owner",true);
        if(((Button)fileCard.FindName("CloudFileAction")).Visibility!=Visibility.Collapsed) throw new Exception("Owner unexpectedly has collaborator download action.");
        fileCard.Item.Attachments[0].Uploaded=false;fileCard.SetCloudPresentation("owner",true);
        void Layout(double width)
        {
            window.Width=width;window.Height=760;
            content.Measure(new Size(width,760));content.Arrange(new Rect(0,0,width,760));content.UpdateLayout();
            app.Dispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);content.UpdateLayout();
        }
        foreach(var width in new[]{900d,1280d,1920d})
        {
            Layout(width);
            var account=(FrameworkElement)window.FindName("CloudAccountButton");var trash=(FrameworkElement)window.FindName("TrashZone");var zoom=(FrameworkElement)window.FindName("ZoomSurface");var rail=(FrameworkElement)window.FindName("CreativeToolRail");
            var toolbar=(FrameworkElement)window.FindName("TopToolbar");var workspace=(FrameworkElement)window.FindName("WorkspaceButton");var search=(FrameworkElement)window.FindName("WorkspaceSearchSurface");
            var a=account.TranslatePoint(new Point(),content);var t=trash.TranslatePoint(new Point(),content);var z=zoom.TranslatePoint(new Point(),content);var r=rail.TranslatePoint(new Point(),content);
            var tb=toolbar.TranslatePoint(new Point(),content);var ws=workspace.TranslatePoint(new Point(),content);var s=search.TranslatePoint(new Point(),content);
            if(a.X<0 || a.X+account.ActualWidth>z.X+1 || z.X+zoom.ActualWidth>content.ActualWidth+1) throw new Exception($"Cloud header overlaps zoom at {width}px.");
            if(toolbar.ActualWidth>177 || ws.X-(tb.X+toolbar.ActualWidth)>8) throw new Exception($"Top-left toolbar still reserves unused horizontal space at {width}px.");
            if(Math.Abs((s.X+search.ActualWidth/2)-content.ActualWidth/2)>3) throw new Exception($"Workspace search is not geometrically centered at {width}px.");
            if(trash.Width<260 || trash.Height<70 || t.Y<content.ActualHeight/2) throw new Exception("Trash drop target is not the approved wide bottom-right surface.");
            if(rail.Width<50 || rail.Width>58 || r.X<content.ActualWidth-90) throw new Exception("Creative tool rail is not compact and aligned to the right edge.");
            if(((Button)window.FindName("CloudStatusButton")).Visibility!=Visibility.Visible) throw new Exception("Workspace saved status is missing from the header.");
            var image=new RenderTargetBitmap((int)width,760,96,96,PixelFormats.Pbgra32);image.Render(content);
            using var output=File.Create(Path.Combine(AppContext.BaseDirectory,$"cloud-screen-{width}.png"));var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));encoder.Save(output);
        }
        Layout(1280);
        var pasted=new ClipboardItem{Type=ClipboardItemType.Text,DisplayName="Colado no cursor",Text="posição do mouse"};
        typeof(MainWindow).GetMethod("AddItems",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[new[]{pasted},new Point(820,520)]);
        var pastedCard=canvas.Children.OfType<ItemCard>().Single(card=>card.Item==pasted);
        if(Math.Abs((pasted.X+160)-820)>.1||Math.Abs((pasted.Y+105)-520)>.1)throw new Exception("Pasted item is not centered at the pointer position.");
        typeof(MainWindow).GetMethod("Card_DragStarted",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[pastedCard,EventArgs.Empty]);
        if(Panel.GetZIndex(pastedCard)<canvas.Children.OfType<ItemCard>().Where(card=>card!=pastedCard).Max(Panel.GetZIndex))throw new Exception("Dragged element was not brought above overlapping elements.");
        typeof(MainWindow).GetMethod("ClearSelection",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
        var showFormat=typeof(MainWindow).GetMethod("ShowCreativeFormatMenu",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var activeWorkspace=(WorkspaceBoard)typeof(MainWindow).GetField("_activeWorkspace",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        var renderPresence=typeof(MainWindow).GetMethod("RenderPresence",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var heartbeat=(DispatcherTimer)typeof(MainWindow).GetField("_presenceHeartbeatTimer",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        if(!heartbeat.IsEnabled || heartbeat.Interval>TimeSpan.FromSeconds(3))
            throw new Exception("Shared presence heartbeat is not active enough to keep stationary cursors and follow mode alive.");
        var presence=new CloudPresence(Guid.NewGuid().ToString(),"colega",null,activeWorkspace.Id.ToString("N"),100,100,null,900,620,.4);
        renderPresence.Invoke(window,[presence]);
        var cursors=(Dictionary<string,(FrameworkElement Visual,DateTimeOffset Seen)>)typeof(MainWindow).GetField("_remoteCursors",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        var originalCursor=cursors[presence.UserId].Visual;
        renderPresence.Invoke(window,[presence with {X=140,Y=150}]);
        if(!ReferenceEquals(originalCursor,cursors[presence.UserId].Visual) || originalCursor.RenderTransform is not TranslateTransform position
            || !position.HasAnimatedProperties)
            throw new Exception("Remote pointer does not interpolate updates on the existing visual");
        void PumpFrames(int milliseconds)
        {
            var frame=new DispatcherFrame();
            var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(milliseconds)};
            timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
        }
        PumpFrames(110);
        if(Math.Abs(position.X-133)>.1 || Math.Abs(position.Y-143)>.1)
            throw new Exception("Remote pointer does not settle on the newest position");
        // A sustained stream must progress continuously without recreating cursors or a growing queue.
        for(var sample=1;sample<=30;sample++)
        {
            renderPresence.Invoke(window,[presence with {X=140+sample*4,Y=150+sample*2}]);
            PumpFrames(33);
            if(!ReferenceEquals(originalCursor,cursors[presence.UserId].Visual))throw new Exception("Pointer stream recreates visuals");
        }
        PumpFrames(100);
        if(Math.Abs(position.X-253)>.1 || Math.Abs(position.Y-203)>.1)throw new Exception("Pointer stream accumulated stale positions");
        Console.WriteLine("PASS: continuous pointer interpolation converges without queued stale positions.");
        canvas.Children.Remove(originalCursor);
        renderPresence.Invoke(window,[presence]);
        originalCursor=cursors[presence.UserId].Visual;
        if(originalCursor.Parent!=canvas)throw new Exception("Cursor cannot recover after the board redraws");
        Console.WriteLine("PASS: remote pointer reattaches after a board redraw.");
        var backend=typeof(CloudSyncService).Assembly.GetType("ClipDesk.Services.SupabaseCloudSyncService")!;
        var presenceEncoder=backend.GetMethod("PresencePayload",BindingFlags.NonPublic|BindingFlags.Static)!;
        var presenceDecoder=backend.GetMethod("TryPresence",BindingFlags.NonPublic|BindingFlags.Static)!;
        var protocolClient=new Supabase.Realtime.Client("wss://example.invalid/realtime/v1");
        CloudPresence ThroughSdk(CloudPresence outgoing)
        {
            var payload=(Dictionary<string,object>)presenceEncoder.Invoke(null,[outgoing])!;
            var wire=System.Text.Json.JsonSerializer.Serialize(new Supabase.Realtime.Models.BaseBroadcast{Event="presence",Payload=payload},protocolClient.SerializerSettings);
            var received=System.Text.Json.JsonSerializer.Deserialize<Supabase.Realtime.Models.BaseBroadcast>(wire,protocolClient.SerializerSettings)!.Payload!;
            object?[] arguments=[received,null];
            if(!(bool)presenceDecoder.Invoke(null,arguments)!)throw new Exception("Live SDK motion never reaches the renderer");
            return (CloudPresence)arguments[1]!;
        }
        var fractionalPresence=ThroughSdk(presence with {UserId=Guid.NewGuid().ToString(),X=333.375,Y=444.625,Zoom=.73});
        renderPresence.Invoke(window,[fractionalPresence]);PumpFrames(110);
        var fractionalPosition=(TranslateTransform)cursors[fractionalPresence.UserId].Visual.RenderTransform;
        if(Math.Abs(fractionalPosition.X-326.375)>.01 || Math.Abs(fractionalPosition.Y-437.625)>.01)
            throw new Exception("Fractional SDK coordinates do not reach the WPF cursor");
        canvas.Children.Remove(cursors[fractionalPresence.UserId].Visual);cursors.Remove(fractionalPresence.UserId);
        var parseDrags=backend.GetMethod("ParsePresenceDrags",BindingFlags.NonPublic|BindingFlags.Static)!;
        var dragId=Guid.NewGuid().ToString("N");
        var dragPayload=System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("[{\"Id\":\""+dragId+"\",\"X\":123,\"Y\":456}]");
        var decoded=(PresenceDrag[]?)parseDrags.Invoke(null,[new Dictionary<string,object>{{"drags",dragPayload}}]);
        if(decoded is not {Length:1} || decoded[0].X!=123 || decoded[0].Id!=dragId)throw new Exception("SDK broadcast drag payload cannot be decoded");
        Console.WriteLine("PASS: Supabase SDK drag payload decodes with compatible optional fields.");
        var marker=FindTaggedCanvas(originalCursor,"presence-marker") ?? throw new Exception("Remote cursor marker is missing.");
        var currentZoom=((Slider)window.FindName("ZoomSlider")).Value/100;
        if(marker.RenderTransform is not ScaleTransform markerScale || Math.Abs(markerScale.ScaleX-1/Math.Max(.1,currentZoom))>.01)
            throw new Exception("Remote cursor does not compensate for workspace zoom.");
        var beforeTravel=(double)typeof(MainWindow).GetField("_workspaceZoom",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        typeof(MainWindow).GetMethod("TravelToCollaborator",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[new CloudMember(presence.UserId,presence.Username,null,"editor")]);
        if(Math.Abs((double)typeof(MainWindow).GetField("_workspaceZoom",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!-beforeTravel)>.001)
            throw new Exception("Collaborator camera jump is immediate instead of animated.");
        typeof(MainWindow).GetField("_presenceTravelStarted",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,DateTimeOffset.UtcNow.AddSeconds(-1));
        typeof(MainWindow).GetMethod("AdvancePresenceTravel",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
        if(Math.Abs((double)typeof(MainWindow).GetField("_workspaceZoom",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!-.4)>.01)
            throw new Exception("Collaborator camera travel does not finish at the shared zoom.");
        ((Slider)window.FindName("ZoomSlider")).Value=100;Layout(1280);
        var member=new CloudMember(presence.UserId,presence.Username,null,"editor");
        ((Task)typeof(MainWindow).GetMethod("ToggleFollowCameraAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[member])!).GetAwaiter().GetResult();
        var followBanner=(Border)window.FindName("FollowCameraBanner");
        var followTitle=(TextBlock)window.FindName("FollowCameraTitle");
        if(followBanner.Visibility!=Visibility.Visible||!followTitle.Text.Contains("@"+presence.Username,StringComparison.Ordinal))
            throw new Exception("Follow-camera status and stop control are not visible.");
        Layout(1280);
        followBanner.BeginAnimation(UIElement.OpacityProperty,null);followBanner.Opacity=1;
        var followImage=new RenderTargetBitmap(1280,760,96,96,PixelFormats.Pbgra32);followImage.Render(content);
        using(var output=File.Create(Path.Combine(AppContext.BaseDirectory,"follow-camera-banner.png"))){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(followImage));encoder.Save(output);}
        renderPresence.Invoke(window,[presence with {ViewCenterX=1120,ViewCenterY=740,Zoom=.25}]);
        var followTick=typeof(MainWindow).GetField("_presenceTravelLastTick",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var advanceTravel=typeof(MainWindow).GetMethod("AdvancePresenceTravel",BindingFlags.Instance|BindingFlags.NonPublic)!;
        for(var frame=0;frame<45;frame++)
        {
            followTick.SetValue(window,System.Diagnostics.Stopwatch.GetTimestamp()-System.Diagnostics.Stopwatch.Frequency/60);
            advanceTravel.Invoke(window,null);
        }
        if((string?)typeof(MainWindow).GetField("_followedMemberId",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!=presence.UserId
            || Math.Abs((double)typeof(MainWindow).GetField("_workspaceZoom",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!-.25)>.01)
            throw new Exception("Follow-camera mode does not remain attached to collaborator presence updates.");
        for(var sample=1;sample<=60;sample++)
        {
            var movingPresence=presence with {ViewCenterX=1120+sample*5,ViewCenterY=740+sample*2,Zoom=.25+sample*.001};
            renderPresence.Invoke(window,[movingPresence]);
            for(var frame=0;frame<2;frame++)
            {
                followTick.SetValue(window,System.Diagnostics.Stopwatch.GetTimestamp()-System.Diagnostics.Stopwatch.Frequency/60);
                advanceTravel.Invoke(window,null);
            }
            var camera=(Point)typeof(MainWindow).GetField("_followCameraCenter",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            if((camera-new Point(movingPresence.ViewCenterX,movingPresence.ViewCenterY)).Length>12
                || Math.Abs((double)typeof(MainWindow).GetField("_workspaceZoom",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!-movingPresence.Zoom)>.003)
                throw new Exception($"Continuous camera updates accumulate position/zoom lag: sample={sample}, camera={camera}, target={movingPresence.ViewCenterX},{movingPresence.ViewCenterY}, zoom={typeof(MainWindow).GetField("_workspaceZoom",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)}, targetZoom={movingPresence.Zoom}");
        }
        Console.WriteLine("PASS: 60 continuous camera and zoom targets remain within bounded tracking error.");
        typeof(MainWindow).GetMethod("StopFollowingCamera",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[false]);
        ((Slider)window.FindName("ZoomSlider")).Value=100;Layout(1280);
        cursors[presence.UserId]=(originalCursor,DateTimeOffset.UtcNow.AddSeconds(-10));
        typeof(MainWindow).GetMethod("ExpireCursors",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
        if(cursors.Count!=0 || canvas.Children.Contains(originalCursor)) throw new Exception("Inactive remote cursor was not removed");
        Console.WriteLine("PASS: remote pointer reuses its visual and inactive cursors expire independently of cloud polling.");
        var note=new BoardObject {Kind=BoardObjectKind.StickyNote,X=260,Y=320,Width=220,Height=110,Style=new(){{"fill","#DCCBFF"},{"fontSize","24"},{"align","center"}},Content=new(){{"text","Nota curta"}}};activeWorkspace.Objects.Add(note);
        var noteView=(BoardObjectView)typeof(MainWindow).GetMethod("AddBoardObjectView",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[note])!;noteView.SetSelected(true);
        var previewX=note.X;var previewY=note.Y;
        var originalNoteTransform=noteView.RenderTransform;
        renderPresence.Invoke(window,[ThroughSdk(presence with {X=333.375,Y=444.625,Zoom=.73,
            Drags=[new PresenceDrag(note.Id,previewX+100,previewY+60)]})]);
        PumpFrames(110);
        var previewOffset=((TransformGroup)noteView.RenderTransform).Children.OfType<TranslateTransform>().Last();
        if(Math.Abs(Canvas.GetLeft(noteView)+previewOffset.X-(previewX+100-BoardObjectView.SelectionPadding))>.1
            || Canvas.GetLeft(noteView)!=previewX-BoardObjectView.SelectionPadding
            || note.X!=previewX || note.Y!=previewY)throw new Exception("Drag preview is absent or overwrites authoritative data");
        var applySnapshot=typeof(MainWindow).GetMethod("ApplySharedBoardSnapshot",BindingFlags.Instance|BindingFlags.NonPublic)!;
        WorkspaceBoard Snapshot() => System.Text.Json.JsonSerializer.Deserialize<WorkspaceBoard>(CloudRules.Serialize(activeWorkspace),CloudRules.Json)!;
        var currentCursor=cursors[presence.UserId].Visual;
        var snapshot=Snapshot();snapshot.Objects.Single(obj=>obj.Id==note.Id).X+=30;
        applySnapshot.Invoke(window,[snapshot]);
        if(!ReferenceEquals(noteView,canvas.Children.OfType<BoardObjectView>().Single(view=>view.Object.Id==note.Id))
            || !ReferenceEquals(note,noteView.Object) || !ReferenceEquals(currentCursor,cursors[presence.UserId].Visual)
            || !ReferenceEquals(fileCard,canvas.Children.OfType<ItemCard>().Single(card=>card.Item.Id==pending.Id)))
            throw new Exception("Incremental snapshot discarded model/view or cursor identity");
        if(Math.Abs(Canvas.GetLeft(noteView)+previewOffset.X-(previewX+100-BoardObjectView.SelectionPadding))>.1)
            throw new Exception("An intermediate durable position made the live drag jump backwards");
        note.X=previewX;note.Y=previewY;
        note.X+=100;note.Y+=60;
        typeof(MainWindow).GetMethod("PositionBoardObjectView",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[noteView]);
        typeof(MainWindow).GetMethod("ReconcileDragPreviews",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
        if(!ReferenceEquals(noteView.RenderTransform,originalNoteTransform))throw new Exception("Confirmed drag preview was not released");
        note.X=previewX;note.Y=previewY;
        typeof(MainWindow).GetMethod("PositionBoardObjectView",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[noteView]);
        Console.WriteLine("PASS: live drag preview interpolates geometry and reconciles without changing persistent data.");
        var savedFileX=fileCard.Item.X;var savedFileY=fileCard.Item.Y;
        var originalFileTransform=fileCard.RenderTransform;
        renderPresence.Invoke(window,[presence with {Drags=[new PresenceDrag(fileCard.Item.Id,savedFileX+180,savedFileY+80)]}]);
        PumpFrames(110);
        typeof(MainWindow).GetMethod("SaveCardPositionsToItems",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
        if(fileCard.Item.X!=savedFileX || fileCard.Item.Y!=savedFileY)
            throw new Exception("Saving the board persists a collaborator's temporary drag preview");
        snapshot=Snapshot();var finalFile=snapshot.Items.Single(item=>item.Id==fileCard.Item.Id);
        finalFile.X+=180;finalFile.Y+=80;applySnapshot.Invoke(window,[snapshot]);
        if(!ReferenceEquals(fileCard.RenderTransform,originalFileTransform) || fileCard.Item.X!=savedFileX+180)
            throw new Exception("Card preview does not reconcile with the committed snapshot");
        Console.WriteLine("PASS: incremental snapshots preserve live views; intermediate positions do not rewind previews; Save cannot persist them.");
        // A retained selection must not keep any remote outline after release.
        foreach(var releasedId in new[]{fileCard.Item.Id,note.Id})
        {
            renderPresence.Invoke(window,[ThroughSdk(presence with {ItemId=releasedId,
                Drags=[new PresenceDrag(releasedId,600.125,500.875)]})]);
            var draggingCursor=cursors[presence.UserId].Visual;
            var remoteSelections=(Dictionary<string,string?>)typeof(MainWindow).GetField("_remoteSelections",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            if(remoteSelections[presence.UserId]!=releasedId)throw new Exception("Active drag has no collaborator outline");
            renderPresence.Invoke(window,[ThroughSdk(presence with {ItemId=releasedId,Drags=[],X=740.25,Y=430.75})]);
            PumpFrames(70);
            if(!ReferenceEquals(draggingCursor,cursors[presence.UserId].Visual)
                || ((Canvas)cursors[presence.UserId].Visual).Children.OfType<Border>().Any())
                throw new Exception("Releasing a selected item leaves a card outline attached to the remote pointer");
            if(remoteSelections[presence.UserId] is not null)throw new Exception("Released item retains a collaborator outline");
            if(releasedId==fileCard.Item.Id && ((Border)fileCard.FindName("CollaboratorSelection")).Visibility!=Visibility.Collapsed)
                throw new Exception("Released card still displays a collaborator outline");
            if(releasedId==note.Id && typeof(BoardObjectView).GetField("_collaboratorSelection",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(noteView) is not null)
                throw new Exception("Released note still draws a collaborator outline");
            renderPresence.Invoke(window,[ThroughSdk(presence with {ItemId=null,Drags=[]})]);
            if(remoteSelections[presence.UserId] is not null)throw new Exception("Reused cursor retains an outdated selection");
        }
        Console.WriteLine("PASS: collaborator outlines exist during drag and clear on release even when the item stays selected.");
        var setDraggingVisual=typeof(BoardObjectView).GetMethod("SetDraggingVisual",BindingFlags.Instance|BindingFlags.NonPublic)!;
        foreach(var kind in new[]{BoardObjectKind.Text,BoardObjectKind.Shape,BoardObjectKind.StickyNote,BoardObjectKind.Checklist,BoardObjectKind.Calculator,BoardObjectKind.Translator,BoardObjectKind.CurrencyConverter})
        {
            var floating=new BoardObjectView(new BoardObject{Kind=kind,Width=260,Height=180});
            if(floating.Effect is not DropShadowEffect shadow)throw new Exception($"{kind} does not have the approved floating shadow.");
            floating.PlayPopIn();
            if(!floating.HasAnimatedProperties)throw new Exception($"{kind} does not animate when it appears.");
            setDraggingVisual.Invoke(floating,[true]);
            if(!shadow.HasAnimatedProperties)throw new Exception($"{kind} does not lift its shadow while being dragged.");
        }
        showFormat.Invoke(window,[note,noteView]);Layout(1280);
        var formatMenu=(Border)window.FindName("CreativeFormatMenu");var formatRows=(StackPanel)window.FindName("CreativeFormatMenuContent");
        if(formatMenu.Visibility!=Visibility.Visible || formatRows.Children.Count!=2 || formatMenu.TranslatePoint(new Point(),content).Y>=note.Y)
            throw new Exception("Creative formatting menu is not positioned above the new object.");
        var resizeNote=typeof(MainWindow).GetMethod("EnsureNoteContainsContent",BindingFlags.Instance|BindingFlags.NonPublic)!;
        note.Width=420;note.Height=260;resizeNote.Invoke(window,[note]);var shortHeight=note.Height;
        if(note.Width!=420||note.Height<260)throw new Exception("Sticky note loses its user-defined size when editing starts.");
        note.Content["text"]=string.Join(' ',Enumerable.Repeat("conteúdo responsivo",80));resizeNote.Invoke(window,[note]);noteView.RefreshFromObject();
        if(note.Height<=shortHeight || note.Width!=420) throw new Exception("Sticky note does not preserve width while growing for overflowing text.");
        if(noteView.Width<=note.Width || noteView.Height<=note.Height) throw new Exception("Selection transform handles are not available around creative objects.");
        var connect=typeof(MainWindow).GetMethod("ConnectCard",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var connectNode=typeof(MainWindow).GetMethod("ConnectNode",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var visibleCards=canvas.Children.OfType<ItemCard>().Take(2).ToList();connect.Invoke(window,[visibleCards[0]]);connect.Invoke(window,[visibleCards[1]]);
        var connector=activeWorkspace.Objects.Single(o=>o.Kind==BoardObjectKind.Connector);
        if(connector.Content["nodeIds"].Split(';').Length!=2 || connector.Style["thickness"]!="1.35") throw new Exception("Connector does not complete a two-element connection.");
        var activeTool=typeof(MainWindow).GetField("_activeCreativeTool",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!.ToString();
        if(activeTool!="Select")throw new Exception("Connector tool does not return to selector after creating a connection.");
        var magnet=(Button)window.FindName("MagnetToolButton");var magnetIndicator=(FrameworkElement)window.FindName("MagnetToolIndicator");
        if(magnet.Background==Brushes.Transparent||magnetIndicator.Visibility!=Visibility.Visible)throw new Exception("Magnetic alignment is not enabled and visible by default.");
        var alignmentTargets=(List<Rect>)typeof(MainWindow).GetField("_alignmentTargets",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        alignmentTargets.Clear();alignmentTargets.Add(new Rect(400,300,200,100));
        typeof(MainWindow).GetField("_workspaceZoom",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,1d);
        object?[] snapArguments=[new Rect(405,450,100,80),null,null];
        var snapped=(Point)typeof(MainWindow).GetMethod("SnapAlignment",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,snapArguments)!;
        if(Math.Abs(snapped.X-400)>.01||snapArguments[1] is not double guideX||Math.Abs(guideX-400)>.01)
            throw new Exception("Magnetic alignment did not snap a nearby edge or expose its guide.");
        typeof(MainWindow).GetField("_moveConnectedTogether",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,true);
        typeof(MainWindow).GetMethod("ExpandConnectedDragSelection",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[visibleCards[0]]);
        var selected=(System.Collections.ICollection)typeof(MainWindow).GetField("_selectedCards",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        if(selected.Count!=2) throw new Exception("Connected movement toggle does not expand the dragged card to its complete group.");
        Layout(1280);var creativeImage=new RenderTargetBitmap(1280,760,96,96,PixelFormats.Pbgra32);creativeImage.Render(content);
        using(var output=File.Create(Path.Combine(AppContext.BaseDirectory,"creative-tools.png"))) {var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(creativeImage));encoder.Save(output);}
        typeof(MainWindow).GetMethod("BreakConnector",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[connector,0]);
        if(activeWorkspace.Objects.Contains(connector)) throw new Exception("Middle cut control did not sever the selected connection segment.");
        typeof(MainWindow).GetMethod("ClearBoardObjectSelection",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
        if(formatMenu.Visibility!=Visibility.Collapsed) throw new Exception("Creative menu remains visible after clearing selection.");
        var savedItems=activeWorkspace.Items.ToList();var savedObjects=activeWorkspace.Objects.ToList();
        activeWorkspace.Items.Clear();activeWorkspace.Objects.Clear();
        typeof(MainWindow).GetMethod("EnsureWorkspaceExtent",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
        if(((StackPanel)window.FindName("WorkspaceEmptyState")).Visibility!=Visibility.Visible) throw new Exception("Empty workspace guidance is missing on a truly empty board.");
        activeWorkspace.Objects.Add(new BoardObject{Kind=BoardObjectKind.Text,Width=240,Height=80});
        typeof(MainWindow).GetMethod("EnsureWorkspaceExtent",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
        if(((StackPanel)window.FindName("WorkspaceEmptyState")).Visibility!=Visibility.Collapsed) throw new Exception("Empty workspace guidance remains visible after a creative object is added.");
        activeWorkspace.Items.AddRange(savedItems);activeWorkspace.Objects.Clear();activeWorkspace.Objects.AddRange(savedObjects);
        var startupIcon=(System.Windows.Shapes.Path)window.FindName("StartupIcon");
        if(startupIcon.Data.Bounds.Width<400 || startupIcon.Data.Bounds.Height<400) throw new Exception("Startup action does not use the aligned Windows brand vector.");
        var widgetCanvas=new Canvas{Width=1100,Height=520,Background=new SolidColorBrush(Color.FromRgb(11,21,36))};
        var widgets=new[]{
            new BoardObject{Kind=BoardObjectKind.Checklist,X=20,Y=20,Width=300,Height=235,Content=new(){{"title","Checklist"},{"items","Pesquisar ícones\nValidar menu\nCompilar DEV"},{"checked","0;1"}}},
            new BoardObject{Kind=BoardObjectKind.Calculator,X=350,Y=20,Width=270,Height=365,Content=new(){{"display","42"},{"expression","6*7"}}},
            new BoardObject{Kind=BoardObjectKind.Translator,X=650,Y=20,Width=370,Height=250,Content=new(){{"sourceLanguage","auto"},{"targetLanguage","en"},{"input","Olá, mundo"},{"output","Hello, world"}}},
            new BoardObject{Kind=BoardObjectKind.CurrencyConverter,X=650,Y=285,Width=350,Height=215,Content=new(){{"sourceCurrency","BRL"},{"targetCurrency","USD"},{"amount","100"},{"result","USD 18.50"}}}
        };
        var widgetViews=new List<BoardObjectView>();
        foreach(var widget in widgets){var widgetView=new BoardObjectView(widget){IsDarkMode=true};widgetViews.Add(widgetView);widgetCanvas.Children.Add(widgetView);Canvas.SetLeft(widgetView,widget.X-BoardObjectView.SelectionPadding);Canvas.SetTop(widgetView,widget.Y-BoardObjectView.RotationSpace);}
        for(var themeCycle=0;themeCycle<20;themeCycle++)
        {
            ((Button)window.FindName("ThemeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            foreach(var widgetView in widgetViews)
            {
                widgetView.IsDarkMode=themeCycle%2==0;
                widgetView.RefreshFromObject();
            }
            Layout(1280);
            widgetCanvas.Measure(new Size(1100,520));widgetCanvas.Arrange(new Rect(0,0,1100,520));widgetCanvas.UpdateLayout();
            var themeImage=new RenderTargetBitmap(1100,520,96,96,PixelFormats.Pbgra32);themeImage.Render(widgetCanvas);
        }
        Console.WriteLine("PASS: 20 repeated theme switches render cards and all four plugins without an exception.");
        widgetCanvas.Measure(new Size(1100,520));widgetCanvas.Arrange(new Rect(0,0,1100,520));widgetCanvas.UpdateLayout();
        var widgetImage=new RenderTargetBitmap(1100,520,96,96,PixelFormats.Pbgra32);widgetImage.Render(widgetCanvas);
        using(var output=File.Create(Path.Combine(AppContext.BaseDirectory,"board-widgets.png"))){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(widgetImage));encoder.Save(output);}
        if(widgets.Any(widget=>widget.Width<=0||widget.Height<=0))throw new Exception("Native board widgets have invalid dimensions.");
        foreach(var view in widgetViews)
        {
            var before=TaggedFontSizes(view).DefaultIfEmpty(0).Average();var width=view.Object.Width;var height=view.Object.Height;
            view.Object.Width*=1.55;view.Object.Height*=1.55;view.RefreshFromObject();widgetCanvas.Measure(new Size(1800,1000));widgetCanvas.Arrange(new Rect(0,0,1800,1000));widgetCanvas.UpdateLayout();
            var after=TaggedFontSizes(view).DefaultIfEmpty(0).Average();
            if(after<=before*1.2)throw new Exception($"{view.Object.Kind} typography did not scale responsively: {before:0.0} -> {after:0.0}.");
            view.Object.Width=width*4;view.Object.Height=height*4;view.RefreshFromObject();widgetCanvas.Measure(new Size(4400,2200));widgetCanvas.Arrange(new Rect(0,0,4400,2200));widgetCanvas.UpdateLayout();
            var large=TaggedFontSizes(view).DefaultIfEmpty(0).Average();
            if(large<before*3.5)throw new Exception($"{view.Object.Kind} typography stays tiny at large sizes: {before:0.0} -> {large:0.0}.");
            var edit=VisualDescendants<Button>(view).FirstOrDefault(button=>Equals(button.ToolTip,"Editar conteúdo"));
            if(view.Object.Kind==BoardObjectKind.Checklist)
            {
                if(edit is null||edit.Width<110||edit.Height<100||edit.Opacity<.85)throw new Exception($"{view.Object.Kind} edit action is not visible and responsive at large sizes.");
            }
            if((view.Object.Kind is BoardObjectKind.Translator or BoardObjectKind.CurrencyConverter)
                && (edit is not null || !VisualDescendants<TextBox>(view).Any(input=>!input.IsReadOnly)))
                throw new Exception($"{view.Object.Kind} should accept live input without an edit button.");
            view.Object.Width=width;view.Object.Height=height;view.RefreshFromObject();
        }
        Console.WriteLine("PASS: contextual menus, transforms, fixed-size notes, connector reset and native widgets are functional.");
        ((List<WorkspaceBoard>)typeof(MainWindow).GetField("_workspaces",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!).Add(new WorkspaceBoard{Name="Mesa local",SyncMode=WorkspaceSyncMode.Local});
        ((Button)window.FindName("WorkspaceButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((Border)window.FindName("WorkspaceDropdown")).BeginAnimation(UIElement.OpacityProperty,null);((Border)window.FindName("WorkspaceDropdown")).Opacity=1;
        Layout(1280);
        var home=(StackPanel)window.FindName("WorkspaceDropdownHome");var modePage=(StackPanel)window.FindName("WorkspaceDropdownModePage");
        if(home.Visibility!=Visibility.Visible || modePage.Visibility!=Visibility.Collapsed) throw new Exception("Workspace selector does not open on its home page.");
        var otherRows=((StackPanel)window.FindName("WorkspaceDropdownItems")).Children.OfType<Grid>().ToList();
        if(otherRows.Count!=1 || otherRows[0].Children.OfType<Button>().Count()!=3 || otherRows[0].Children.OfType<Button>().Single(button=>Grid.GetColumn(button)==2).ToolTip?.ToString()!="Somente local")
            throw new Exception("A workspace row is missing its right-aligned status, open or delete action.");
        var currentStatus=(Button)window.FindName("WorkspaceDropdownCurrentStatusButton");var rowStatus=otherRows[0].Children.OfType<Button>().Single(button=>Grid.GetColumn(button)==2);
        var currentTitle=(TextBlock)window.FindName("WorkspaceDropdownTitle");var rowOpen=otherRows[0].Children.OfType<Button>().Single(button=>Grid.GetColumn(button)==0);var rowLabel=((Grid)rowOpen.Content).Children.OfType<TextBlock>().Single(text=>Grid.GetColumn(text)==1);
        var currentStatusX=currentStatus.TranslatePoint(new Point(),content).X;var rowStatusX=rowStatus.TranslatePoint(new Point(),content).X;var currentTitleX=currentTitle.TranslatePoint(new Point(),content).X;var rowLabelX=rowLabel.TranslatePoint(new Point(),content).X;
        if(Math.Abs(currentStatusX-rowStatusX)>1||Math.Abs(currentTitleX-rowLabelX)>1)
            throw new Exception($"Workspace names or status icons are not aligned in a shared column: status {currentStatusX}/{rowStatusX}; names {currentTitleX}/{rowLabelX}.");
        var workspaceHomeImage=new RenderTargetBitmap(1280,760,96,96,PixelFormats.Pbgra32);workspaceHomeImage.Render(content);
        using(var output=File.Create(Path.Combine(AppContext.BaseDirectory,"workspace-home-menu.png"))){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(workspaceHomeImage));encoder.Save(output);}
        otherRows[0].Children.OfType<Button>().Single(button=>Grid.GetColumn(button)==2).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var modes=(StackPanel)window.FindName("WorkspaceDropdownModes");
        var labels=modes.Children.OfType<Button>().Select(button=>((Grid)button.Content).Children.OfType<TextBlock>().First(text=>Grid.GetColumn(text)==1).Text).ToList();
        if(home.Visibility!=Visibility.Collapsed || modePage.Visibility!=Visibility.Visible || !labels.Contains("Somente local") || !labels.Contains("Nuvem pessoal") || !labels.Contains("Compartilhada"))
            throw new Exception("Status icon does not open the storage-only page.");
        if(((Button)window.FindName("WorkspaceDropdownDeleteButton")).Visibility!=Visibility.Visible) throw new Exception("Current workspace has no complete-delete action.");
        Layout(1280);
        var panelImage=new RenderTargetBitmap(1280,760,96,96,PixelFormats.Pbgra32);panelImage.Render(content);
        using(var output=File.Create(Path.Combine(AppContext.BaseDirectory,"workspace-storage-menu.png"))) {var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(panelImage));encoder.Save(output);}
        ((Button)window.FindName("WorkspaceModeBackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(home.Visibility!=Visibility.Visible || modePage.Visibility!=Visibility.Collapsed) throw new Exception("Storage page back arrow does not return to the workspace list.");
        ((Button)window.FindName("CloudStatusButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(modePage.Visibility!=Visibility.Visible) throw new Exception("Header status does not open the storage-only page.");
        typeof(MainWindow).GetMethod("HideWorkspaceDropdown",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[true]);
        ((Button)window.FindName("WorkspaceButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(home.Visibility!=Visibility.Visible || modePage.Visibility!=Visibility.Collapsed) throw new Exception("Closing the menu does not reset it to the workspace home page.");
        typeof(MainWindow).GetMethod("HideWorkspaceDropdown",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[true]);
        typeof(MainWindow).GetMethod("ShowCanvasContextMenu",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[new Point(820,180)]);Layout(1280);
        var addMenu=(Border)window.FindName("CanvasContextMenu");
        if(addMenu.Visibility!=Visibility.Visible||((StackPanel)addMenu.Child).Children.OfType<Button>().Count()!=6)throw new Exception("Add-to-board menu does not expose the six approved rows.");
        if(addMenu.Width>233||addMenu.CornerRadius!=new CornerRadius(0))throw new Exception("Add-to-board menu was not reduced by 30% with square corners.");
        addMenu.BeginAnimation(UIElement.OpacityProperty,null);addMenu.Opacity=1;
        var addMenuScale=(ScaleTransform)window.FindName("CanvasContextMenuScale");addMenuScale.BeginAnimation(ScaleTransform.ScaleXProperty,null);addMenuScale.BeginAnimation(ScaleTransform.ScaleYProperty,null);addMenuScale.ScaleX=1;addMenuScale.ScaleY=1;
        var addMenuImage=new RenderTargetBitmap(1280,760,96,96,PixelFormats.Pbgra32);addMenuImage.Render(content);
        using(var output=File.Create(Path.Combine(AppContext.BaseDirectory,"add-to-board-menu.png"))){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(addMenuImage));encoder.Save(output);}
        var expectedDownloads=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"ClipDesk","Downloads");
        if(!StorageService.DownloadsDirectory.Equals(expectedDownloads,StringComparison.OrdinalIgnoreCase)
            || !StorageService.IsUserDownload(Path.Combine(expectedDownloads,"arquivo.pdf")))
            throw new Exception("User downloads are not routed to Documents\\ClipDesk\\Downloads.");
        var showInvite=typeof(MainWindow).GetMethod("ShowInvitePopup",BindingFlags.Instance|BindingFlags.NonPublic)!;
        showInvite.Invoke(window,[new CloudInvitation(Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N"),"Planejamento da equipe","colega","Colega de equipe",null)]);
        foreach(var dark in new[]{true,false})
        {
            typeof(MainWindow).GetField("_isDarkMode",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,dark);
            typeof(MainWindow).GetMethod("ApplyTheme",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
            Layout(1280);
            var inviteImage=new RenderTargetBitmap(1280,760,96,96,PixelFormats.Pbgra32);inviteImage.Render(content);
            using var output=File.Create(Path.Combine(AppContext.BaseDirectory,dark?"invite-dark.png":"invite-light.png"));
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(inviteImage));encoder.Save(output);
        }
        if(((Border)window.FindName("InvitePopup")).ActualWidth<360) throw new Exception("Invitation card does not fit the updated layout.");
        Console.WriteLine("PASS: invitation card renders in light and dark themes.");
        Console.WriteLine("PASS: pending cards dim; collaborator download changes to a folder action; approved trash and creative rail are visible.");
        Console.WriteLine("PASS: header at 900/1280/1920px; status icons open a storage-only page with back and close-reset behavior.");
    }

    private static IEnumerable<double> TaggedFontSizes(DependencyObject root)
    {
        if(root is TextBlock {Tag:string tag} text && tag.StartsWith("plugin-font:",StringComparison.Ordinal))yield return text.FontSize;
        if(root is Control {Tag:string controlTag} control && controlTag.StartsWith("plugin-font:",StringComparison.Ordinal))yield return control.FontSize;
        for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++)
            foreach(var size in TaggedFontSizes(VisualTreeHelper.GetChild(root,index)))yield return size;
    }
    private static Canvas? FindTaggedCanvas(DependencyObject root,string tag)
    {
        if(root is Canvas canvas && Equals(canvas.Tag,tag)) return canvas;
        for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++)
            if(FindTaggedCanvas(VisualTreeHelper.GetChild(root,index),tag) is { } found) return found;
        return null;
    }
    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T:DependencyObject
    {
        for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++)
        {
            var child=VisualTreeHelper.GetChild(root,index);
            if(child is T match) yield return match;
            foreach(var nested in VisualDescendants<T>(child)) yield return nested;
        }
    }
}
