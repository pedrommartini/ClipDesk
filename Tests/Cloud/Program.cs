using System.Net;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using ClipDesk.Core;
using ClipDesk.Models;
using ClipDesk.Server;
using ClipDesk.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Http.Connections;

var root=Path.Combine(Path.GetTempPath(),"ClipDesk-Cloud-Checks",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
var count=0;
void Check(bool condition,string name) { if(!condition) throw new Exception("FAIL: "+name); Console.WriteLine("PASS: "+name);count++; }
Environment.SetEnvironmentVariable("CLIPDESK_DEV_DATA_ROOT",Path.Combine(root,"profile-format"));
var profileStorage=new StorageService();
var standardSupabaseId=Guid.NewGuid().ToString();
profileStorage.SwitchProfile(standardSupabaseId);
Check(profileStorage.Profile==Guid.Parse(standardSupabaseId).ToString("N"),"Standard Supabase UUIDs select a safe local profile without closing the app");
await PerformanceChecks.Run(Check);
async Task Throws403(HttpClient client,string path,object body,string name)
{using var response=await client.PostAsJsonAsync(path,body);Check(response.StatusCode==HttpStatusCode.Forbidden,name);}
using var factory=new CloudFactory(Path.Combine(root,"server.db"));
using var anonymous=factory.CreateClient(); var store=factory.Services.GetRequiredService<CloudStore>();
var a=store.Login("google-a","a@example.test","Alice",null); var b=store.Login("google-b","b@example.test","Bruno",null);
store.SetUsername(a.User.Id,"alice");store.SetUsername(b.User.Id,"bruno");
using var alice=factory.CreateClient();alice.DefaultRequestHeaders.Authorization=new("Bearer",a.Token);
using var bruno=factory.CreateClient();bruno.DefaultRequestHeaders.Authorization=new("Bearer",b.Token);
Check((await anonymous.GetAsync("/api/entities")).StatusCode==HttpStatusCode.Unauthorized,"Anonymous clients cannot enumerate data");
Check(store.Authenticate(new string('x',64)) is null,"Fabricated sessions are rejected");
Check(CloudRules.IsUsernameValid("pedro_123") && !CloudRules.IsUsernameValid("admin") && !CloudRules.IsUsernameValid("Pedro") && !CloudRules.IsUsernameValid("a/b"),"Username syntax and reserved names");
Check((await bruno.PostAsJsonAsync("/api/username",new UsernameRequest("alice"))).StatusCode==HttpStatusCode.Conflict,"Username is unique across accounts");
Check(!CloudRules.UsesDrive(29_999_999) && !CloudRules.UsesDrive(30_000_000) && CloudRules.UsesDrive(30_000_001),"Exact 30 MB storage boundary");
var local=new WorkspaceBoard(); var personal=new WorkspaceBoard {SyncMode=WorkspaceSyncMode.PersonalCloud};
Check(CloudProjection.Project([local,personal],[],a.User.Id,false).Count()==1,"Login never projects local-only boards");
var localImagePath=Path.Combine(root,"pasted-image.png");File.WriteAllBytes(localImagePath,[1,2,3]);
var imageAttachmentId=Guid.NewGuid().ToString("N");
var imageStoragePath=$"{Guid.NewGuid():N}/{imageAttachmentId}/image.png";
var pastedImage=new ClipboardItem {Type=ClipboardItemType.Image,StoredFilePath=localImagePath,Attachments=
[
    new CloudAttachment {Id=imageAttachmentId,OwnerId=a.User.Id,Name="pasted-image.png",Size=3,
        Sha256="fixture",Uploaded=true,StoragePath=imageStoragePath,LocalPath=localImagePath}
]};
var imageBoard=new WorkspaceBoard {SyncMode=WorkspaceSyncMode.Shared,OwnerId=a.User.Id,Items=[pastedImage]};
var projectedImage=CloudProjection.Project([imageBoard],[],a.User.Id,false).ToList();
var imageOnSameDevice=CloudProjection.Materialize(projectedImage,new Dictionary<string,string>(),
    new Dictionary<string,ClipboardItem> { [pastedImage.Id]=pastedImage }).Single().Items.Single();
var downloadedImagePath=Path.Combine(root,"downloaded-image.png");File.WriteAllBytes(downloadedImagePath,[1,2,3]);
var imageOnOtherDevice=CloudProjection.Materialize(projectedImage,
    new Dictionary<string,string> {[imageAttachmentId]=downloadedImagePath}).Single().Items.Single();
var projectedImageJson=CloudRules.Serialize(projectedImage);
Check(imageOnSameDevice.StoredFilePath==localImagePath && imageOnOtherDevice.StoredFilePath==downloadedImagePath
    && projectedImageJson.Contains(imageStoragePath,StringComparison.Ordinal)
    && !projectedImageJson.Contains(localImagePath,StringComparison.Ordinal),
    "Pasted images publish a private storage reference and restore a device-local copy without leaking Windows paths");
var bid=Guid.NewGuid().ToString("N");
SyncOperation Create(string kind,string? workspace,JsonObject data)=>new(Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N"),kind,workspace,0,new(),data);
var boardOp=Create("workspace",null,new JsonObject {["name"]="Mesa A",["ownerId"]=b.User.Id}) with {EntityId=bid};
var board=store.Apply(a.User.Id,boardOp);
Check(board.Data["ownerId"]!.GetValue<string>()==a.User.Id,"Server owns identity assignment even when client forges owner");
Check(store.Apply(a.User.Id,boardOp).Version==board.Version,"Retried operations are idempotent");
Check(store.GetEntities(b.User.Id).Count==0,"Private board invisible to another account");
var itemOp=Create("item",bid,new JsonObject {["name"]="Nota",["text"]="original",["x"]=0,["y"]=0});
var item=store.Apply(a.User.Id,itemOp);
await Throws403(bruno,"/api/sync",itemOp with {OperationId=Guid.NewGuid().ToString("N")},"Guessing item and workspace IDs cannot write another account");
var move=(JsonObject)item.Data.DeepClone();move["x"]=44;
var moved=store.Apply(a.User.Id,new(Guid.NewGuid().ToString("N"),item.Id,"item",bid,item.Version,item.Data,move));
var edit=(JsonObject)item.Data.DeepClone();edit["text"]="novo";
var merged=store.Apply(a.User.Id,new(Guid.NewGuid().ToString("N"),item.Id,"item",bid,item.Version,item.Data,edit));
Check(merged.Data["x"]!.GetValue<int>()==44 && merged.Data["text"]!.GetValue<string>()=="novo","Independent move and text edits merge without lost updates");
var divergent=(JsonObject)item.Data.DeepClone();divergent["text"]="outro";
Check((await alice.PostAsJsonAsync("/api/sync",new SyncOperation(Guid.NewGuid().ToString("N"),item.Id,"item",bid,item.Version,item.Data,divergent))).StatusCode==HttpStatusCode.Conflict,"Concurrent text edits produce conflict instead of silent overwrite");
var history=store.Apply(a.User.Id,Create("history",null,new JsonObject {["title"]="privado"}));
var bytes=new byte[]{1,2,3,4};var hash=Convert.ToHexString(SHA256.HashData(bytes));var attachmentId=Guid.NewGuid().ToString("N");
store.RegisterAttachment(a.User.Id,new(attachmentId,bid,"test.bin",bytes.Length,hash,false));
Check((await bruno.GetAsync($"/api/attachments/{attachmentId}")).StatusCode==HttpStatusCode.Forbidden,"Attachment metadata is protected before an invite");
await Throws403(bruno,"/api/attachments",new AttachmentRegistration(Guid.NewGuid().ToString("N"),bid,"test.bin",4,hash,false),"Cannot register a file in a private foreign board");
store.StoreBytes(a.User.Id,attachmentId,bytes);
Check(store.Download(a.User.Id,attachmentId).SequenceEqual(bytes),"Small binary is persisted in the app database and recovered intact");
Check((await alice.PutAsync($"/api/attachments/{attachmentId}/content",new ByteArrayContent([0,0,0,0]))).StatusCode==HttpStatusCode.BadRequest,"Corrupted upload is rejected");
store.Invite(a.User.Id,bid,"bruno");
Check(store.GetEntities(b.User.Id).Count==0,"Pending invitation does not grant premature access");
var invitation=store.Invitations(b.User.Id).Single();
Check(store.AcceptInvitation(b.User.Id,invitation.Id)==bid,"Recipient accepts invitation");
Check(store.GetEntities(b.User.Id).Any(e=>e.Id==bid && e.Data["mode"]?.GetValue<string>()=="shared"),"Accepting an invite turns the board into a shared workspace");
Check(!store.GetEntities(b.User.Id).Any(e=>e.Id==history.Id),"Sharing a board never shares the owner's clipboard history");
Check(store.Download(b.User.Id,attachmentId).SequenceEqual(bytes),"Accepted collaborator can download this board's file");
await Throws403(bruno,"/api/workspaces/"+bid+"/invites",new InviteRequest("alice"),"Editors cannot send owner-only invitations");
await Throws403(bruno,"/api/sync",new SyncOperation(Guid.NewGuid().ToString("N"),history.Id,"history",null,history.Version,history.Data,history.Data),"Shared membership does not grant personal history writes");
var largeId=Guid.NewGuid().ToString("N");store.RegisterAttachment(a.User.Id,new(largeId,bid,"large.mp4",30_000_001,hash,true));
Check((await alice.PutAsync($"/api/attachments/{largeId}/content",new ByteArrayContent(bytes))).StatusCode==HttpStatusCode.RequestEntityTooLarge,"Large file bytes cannot be stored in app database");
Check((await bruno.PostAsJsonAsync($"/api/attachments/{largeId}/drive",new DriveCompletion("fake"))).StatusCode==HttpStatusCode.Forbidden,"Collaborator cannot claim owner's Drive upload");
var spoof=new JsonArray(new JsonObject {["id"]=largeId,["ownerId"]=b.User.Id,["uploaded"]=true,["driveFileId"]="fake"});
var fileItem=store.Apply(a.User.Id,Create("item",bid,new JsonObject {["name"]="arquivo",["attachments"]=spoof}));
Check(fileItem.Data["attachments"]![0]!["uploaded"]!.GetValue<bool>()==false && fileItem.Data["attachments"]![0]!["ownerId"]!.GetValue<string>()==a.User.Id,"Server normalizes file ownership and upload state from verified records");
var journal=new LocalDatabase(Path.Combine(root,"client.db"));
journal.Write("sample","local");journal.Stage(item);
Check(new LocalDatabase(Path.Combine(root,"client.db")).Read("sample")=="local" && journal.Pending().Count==1,"Local documents and pending operations survive process restart");
var pending=journal.Pending().Single();journal.Accept(item,pending);
Check(journal.Pending().Count==0,"Acknowledgement clears only the confirmed local operation");
journal.Accept(item);journal.Stage(item);
Check(journal.Pending(item.Id).Count==0 && journal.Entities("missing").Count==0,"Unchanged pulls and staging produce no new operations; targeted lookup isolates entities");
var changed=(JsonObject)item.Data.DeepClone();changed["text"]="local changed";journal.Stage(item with {Data=changed});
var sent=journal.Pending().Single();var changedAgain=(JsonObject)changed.DeepClone();changedAgain["text"]="edited during request";journal.Stage(item with {Data=changedAgain});
journal.Accept(item with {Version=2,Data=changed},sent);
Check(journal.Pending().Single().Data["text"]!.GetValue<string>()=="edited during request","Changes made during network request remain pending after acknowledgement");
journal.Accept(merged with {Version=3});
Check(journal.ConflictCount==1,"Conflicting local text is preserved durably");
var projection=CloudProjection.Project([new WorkspaceBoard {SyncMode=WorkspaceSyncMode.PersonalCloud,Items=[new ClipboardItem {FilePaths=[@"C:\secret\file.txt"],StoredFilePath=@"C:\secret\image.png",Attachments=[new CloudAttachment {LocalPath=@"C:\secret\managed.txt"}]}]}],[],a.User.Id,false).ToList();
Check(!CloudRules.Serialize(projection).Contains("secret",StringComparison.OrdinalIgnoreCase),"Network projection excludes Windows paths and attachment local paths");
store.Apply(a.User.Id,new(Guid.NewGuid().ToString("N"),bid,"workspace",null,board.Version,board.Data,board.Data,true));
Check((await bruno.GetAsync($"/api/attachments/{attachmentId}")).StatusCode==HttpStatusCode.Forbidden,"Removing shared board revokes collaborator downloads through app API");
Check(!store.GetEntities(b.User.Id).Any(e=>e.Kind=="item"),"Deleted board hides all its cards");
var deviceAccount=store.Login("google-devices","device@example.test","Device",null);
deviceAccount=deviceAccount with {User=store.SetUsername(deviceAccount.User.Id,"devices")};
CloudSyncService Device(string name,out StorageService storage)
{
    Environment.SetEnvironmentVariable("CLIPDESK_DEV_DATA_ROOT",Path.Combine(root,name));
    storage=new StorageService(); storage.SwitchProfile(deviceAccount.User.Id);
    var config=new CloudConfiguration {ServerUrl="http://localhost/"};
    var saved=new SavedCloudAccount(deviceAccount,new GoogleTokens("unused","unused",DateTimeOffset.MaxValue,"unused"),"http://localhost/","unused");
    return new CloudSyncService(storage,config,saved,factory.CreateClient(),enableRealtime:false);
}
await using var first=Device("device-1",out var firstStorage);
await using var second=Device("device-2",out var secondStorage);
var deviceBoard=new WorkspaceBoard
{
    SyncMode=WorkspaceSyncMode.PersonalCloud,OwnerId=deviceAccount.User.Id,
    Items=[new ClipboardItem {Type=ClipboardItemType.Text,Text="primeiro",DisplayName="Nota"}],
    Objects=[new BoardObject {Id=Guid.NewGuid().ToString("N"),Kind=BoardObjectKind.StickyNote,X=320,Y=240,Width=220,Height=180,Style=new(){{"fill","#F6D365"}},Content=new(){{"text","Criativo sincronizado"}}}]
};
var boards1=new List<WorkspaceBoard> {new() {Name="Só local"},deviceBoard};
var history1=new List<ClipboardHistoryEntry> {new() {Type=ClipboardItemType.Text,Text="histórico pessoal",Title="Clipboard"}};
await first.SynchronizeAsync(boards1,history1);
Check(first.Status=="Sincronizado",$"Real desktop sync service completes an initial personal sync (status: {first.Status}, pending: {firstStorage.Database.Pending().Count})");
await second.SynchronizeAsync([],[]);
var secondView=second.Materialize();second.ObservePresentation(secondView.Boards,secondView.History);
Check(secondView.Boards.Count==1 && secondView.Boards[0].Items[0].Text=="primeiro" && secondView.History.Count==1,"Second desktop receives board and personal history, excluding the local board");
Check(secondView.Boards[0].Objects.Single().Content["text"]=="Criativo sincronizado","Second desktop receives creative board objects");
var firstView=first.Materialize(); first.ObservePresentation(firstView.Boards,firstView.History);
firstView.Boards[0].Items[0].Text="alteração remota"; first.Stage(firstView.Boards,firstView.History);
await first.SynchronizeAsync(firstView.Boards,firstView.History);
await second.SynchronizeAsync(secondView.Boards,secondView.History);
Check(second.Materialize().Boards[0].Items[0].Text=="alteração remota","A stale UI does not re-upload or undo a remote edit");
var afterRemote=second.Materialize(); second.ObservePresentation(afterRemote.Boards,afterRemote.History);
Check(secondStorage.Database.Pending().Count==0,"Pulling remote content does not create an echo operation");
afterRemote.Boards[0].Items[0].X=87;second.Stage(afterRemote.Boards,afterRemote.History);
firstView=first.Materialize();first.ObservePresentation(firstView.Boards,firstView.History);
firstView.Boards[0].Items[0].Text="edição simultânea";first.Stage(firstView.Boards,firstView.History);
await first.SynchronizeAsync(firstView.Boards,firstView.History);
await second.SynchronizeAsync(afterRemote.Boards,afterRemote.History);
Check(second.Materialize().Boards[0].Items[0].X==87 && second.Materialize().Boards[0].Items[0].Text=="edição simultânea","Desktop clients converge after independent offline field edits");
var binaryPath=Path.Combine(root,"asset.bin");File.WriteAllBytes(binaryPath,[3,5,7,11]);
firstView=first.Materialize();first.ObservePresentation(firstView.Boards,firstView.History);
firstView.Boards[0].Items.Add(new ClipboardItem {Type=ClipboardItemType.File,DisplayName="asset.bin",FilePaths=[binaryPath]});first.Stage(firstView.Boards,firstView.History);
await first.SynchronizeAsync(firstView.Boards,firstView.History);
Check(first.Status=="Sincronizado","Desktop uploads a managed small file to the database");
secondView=second.Materialize();second.ObservePresentation(secondView.Boards,secondView.History);
await second.SynchronizeAsync(secondView.Boards,secondView.History);
secondView=second.Materialize();second.ObservePresentation(secondView.Boards,secondView.History);
await second.SynchronizeAsync(secondView.Boards,secondView.History);
var copied=second.Materialize().Boards[0].Items.Single(i=>i.Type==ClipboardItemType.File);
Check(copied.FilePaths.Count==1 && File.ReadAllBytes(copied.FilePaths[0]).SequenceEqual(new byte[]{3,5,7,11}),"Second desktop downloads the small file into its own local cache");
Check(firstStorage.BaseDirectory!=secondStorage.BaseDirectory,"Device profiles remain isolated when independent stores coexist");
var folderSource=Path.Combine(root,"source-folder");Directory.CreateDirectory(Path.Combine(folderSource,"sub"));File.WriteAllText(Path.Combine(folderSource,"sub","note.txt"),"estrutura intacta");
firstView=first.Materialize();first.ObservePresentation(firstView.Boards,firstView.History);
var folderBoard=new WorkspaceBoard {Name="Pasta",SyncMode=WorkspaceSyncMode.PersonalCloud,OwnerId=deviceAccount.User.Id,Items=[new ClipboardItem {Type=ClipboardItemType.Folder,DisplayName="Pasta",FilePaths=[folderSource]}]};
firstView.Boards.Add(folderBoard);await first.SynchronizeAsync(firstView.Boards,firstView.History);
secondView=second.Materialize();second.ObservePresentation(secondView.Boards,secondView.History);await second.SynchronizeAsync(secondView.Boards,secondView.History);
var remoteFolder=second.Materialize().Boards.Single(w=>w.Id==folderBoard.Id).Items.Single();
Check(File.ReadAllText(Path.Combine(remoteFolder.FilePaths.Single(),"sub","note.txt"))=="estrutura intacta","Folder sync restores nested files as a real local folder on the second device");
folderBoard=first.Materialize().Boards.Single(w=>w.Id==folderBoard.Id);var oldFolderId=folderBoard.Id;
await first.MakeLocalAsync(folderBoard);
Check(folderBoard.SyncMode==WorkspaceSyncMode.Local && folderBoard.Id!=oldFolderId && File.Exists(Path.Combine(folderBoard.Items[0].FilePaths[0],"sub","note.txt")),"Moving a cloud board to local preserves its complete folder and creates a fresh local identity");
secondView=second.Materialize();second.ObservePresentation(secondView.Boards,secondView.History);await second.SynchronizeAsync(secondView.Boards,secondView.History);
Check(!second.Materialize().Boards.Any(w=>w.Id==oldFolderId),"Second device removes the workspace withdrawn from the cloud");
firstView=first.Materialize();first.ObservePresentation(firstView.Boards,firstView.History);
folderBoard.SyncMode=WorkspaceSyncMode.PersonalCloud;folderBoard.OwnerId=deviceAccount.User.Id;firstView.Boards.Add(folderBoard);
folderBoard.Objects=Enum.GetValues<BoardObjectKind>().Select((kind,index)=>new BoardObject
{
    Kind=kind,WorkspaceId=folderBoard.Id.ToString("N"),X=index*12,Y=index*10,Width=220,Height=140,
    Content=new(){{"value",kind.ToString()}},Style=new(){{"accent","#A78BFA"}}
}).ToList();
await first.SynchronizeAsync(firstView.Boards,firstView.History);
Check(first.Status=="Sincronizado" && first.Materialize().Boards.Single(w=>w.Id==folderBoard.Id).Items[0].Attachments.All(a=>a.Uploaded),"A formerly local board can return to cloud without resurrecting deleted identities");
Check(Enum.GetValues<BoardObjectKind>().All(kind=>first.Materialize().Boards.Single(w=>w.Id==folderBoard.Id).Objects.Any(obj=>obj.Kind==kind)),"Cloud board contains every creative object and plugin kind before deletion");
await first.DeleteWorkspaceAsync(folderBoard);
secondView=second.Materialize();second.ObservePresentation(secondView.Boards,secondView.History);await second.SynchronizeAsync(secondView.Boards,secondView.History);
Check(!first.Materialize().Boards.Any(w=>w.Id==folderBoard.Id) && !second.Materialize().Boards.Any(w=>w.Id==folderBoard.Id),"Deleting a cloud workspace removes it completely for every device");
var projectionStable=CloudProjection.Project(first.Materialize().Boards,[],deviceAccount.User.Id,false).ToList();
var materializedA=CloudProjection.Materialize(projectionStable,new Dictionary<string,string>());
var materializedB=CloudProjection.Materialize(projectionStable,new Dictionary<string,string>());
Check(CloudRules.Serialize(materializedA)==CloudRules.Serialize(materializedB),"Repeated materialization is stable and does not invent new timestamps");
var tombstoneJournal=new LocalDatabase(Path.Combine(root,"tombstone.db"));tombstoneJournal.Accept(item);
tombstoneJournal.Stage(item with {Data=changed});tombstoneJournal.Accept(item with {Version=20,Deleted=true});
Check(tombstoneJournal.Entities().Single().Deleted && tombstoneJournal.Pending().Count==0 && tombstoneJournal.ConflictCount>0,"Remote deletion cannot be resurrected by a queued edit; that edit remains recoverable");
await using var live=new HubConnectionBuilder().WithUrl("http://localhost/live",options=> { options.AccessTokenProvider=()=>Task.FromResult<string?>(deviceAccount.Token);options.Transports=HttpTransportType.LongPolling;options.HttpMessageHandlerFactory=_=>factory.Server.CreateHandler();}).Build();
var notification=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);live.On("Changed",()=>notification.TrySetResult());
var realtimeEntity=new TaskCompletionSource<CloudEntity>(TaskCreationOptions.RunContinuationsAsynchronously);live.On<CloudEntity>("EntityChanged",entity=>realtimeEntity.TrySetResult(entity));
await live.StartAsync();
using var deviceHttp=factory.CreateClient();deviceHttp.DefaultRequestHeaders.Authorization=new("Bearer",deviceAccount.Token);
using var realtimeChange=await deviceHttp.PostAsJsonAsync("/api/sync",Create("item",deviceBoard.Id.ToString("N"),new JsonObject {["text"]="notificação"}));realtimeChange.EnsureSuccessStatusCode();
await notification.Task.WaitAsync(TimeSpan.FromSeconds(3));
Check(notification.Task.IsCompletedSuccessfully,"Authenticated SignalR client receives a persisted change notification");
using var realtimeObjectChange=await deviceHttp.PostAsJsonAsync("/api/sync",Create("boardObject",deviceBoard.Id.ToString("N"),new JsonObject
{
    ["objectKind"]=(int)BoardObjectKind.Checklist,["x"]=45,["y"]=55,["width"]=300,["height"]=240,
    ["style"]=new JsonObject(),["content"]=new JsonObject {["items"]="um\ndois"}
}));
realtimeObjectChange.EnsureSuccessStatusCode();
var deliveredEntity=await realtimeEntity.Task.WaitAsync(TimeSpan.FromSeconds(3));
Check(deliveredEntity.Kind=="boardObject" && deliveredEntity.Data["objectKind"]!.GetValue<int>()==(int)BoardObjectKind.Checklist,"Plugin changes are delivered as targeted realtime entities without a full pull");
var unauthorizedPresence=false;
try {await live.InvokeAsync("Presence",new PresenceMessage(bid,1,1,null));} catch(Microsoft.AspNetCore.SignalR.HubException) {unauthorizedPresence=true;}
Check(unauthorizedPresence,"Realtime channel rejects presence in an unauthorized workspace");
var disconnected=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);live.Closed+=_=> {disconnected.TrySetResult();return Task.CompletedTask;};
using var loggedOut=await deviceHttp.PostAsync("/api/logout",null);loggedOut.EnsureSuccessStatusCode();
await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
Check(disconnected.Task.IsCompletedSuccessfully && (await deviceHttp.GetAsync("/api/me")).StatusCode==HttpStatusCode.Unauthorized,"Logout terminates the existing realtime connection and revokes its HTTP session");
await live.StopAsync();
var sharedForPrivate=store.Apply(a.User.Id,Create("workspace",null,new JsonObject {["name"]="Privatizar"}));store.Invite(a.User.Id,sharedForPrivate.Id,"bruno");store.AcceptInvitation(b.User.Id,store.Invitations(b.User.Id).Single().Id);
var sharedEdit=Create("item",sharedForPrivate.Id,new JsonObject {["text"]="feito pelo convidado"});store.Apply(b.User.Id,sharedEdit);
store.MakePersonal(a.User.Id,sharedForPrivate.Id);
Check(!store.CanAccess(b.User.Id,sharedForPrivate.Id) && store.GetEntities(a.User.Id).Single(e=>e.Id==sharedForPrivate.Id).Data["mode"]!.GetValue<string>()=="personal","Shared board can return to personal cloud and revoke collaborator API access");
await Throws403(bruno,"/api/sync",sharedEdit,"Replaying a once-authorized operation does not reveal data after access revocation");
store.Logout(a.Token);Check((await alice.GetAsync("/api/me")).StatusCode==HttpStatusCode.Unauthorized,"Logout revokes server session");
var limitedAccount=store.Login("limited","limited@example.test","Rate test",null);
using var limitedClient=factory.CreateClient();limitedClient.DefaultRequestHeaders.Authorization=new("Bearer",limitedAccount.Token);
for(var i=0;i<600;i++) {using var response=await limitedClient.GetAsync("/api/me");response.EnsureSuccessStatusCode();}
using var rejected=await limitedClient.GetAsync("/api/me");
Check(rejected.StatusCode==HttpStatusCode.TooManyRequests && rejected.Headers.RetryAfter is not null,"Server limits bursts and supplies Retry-After");
using var isolated=await bruno.GetAsync("/api/me");
Check(isolated.IsSuccessStatusCode,"One authenticated user's limit does not throttle another account behind the same IP");
Console.WriteLine($"\n{count} cloud checks passed. Test data: {root}");

internal sealed class CloudFactory(string database) : WebApplicationFactory<CloudStore>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../Server")));
        builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?> { ["LocalDatabase"]=database,["ConnectionStrings:ClipDesk"]="",["Google:ClientId"]="" }));
    }
}
