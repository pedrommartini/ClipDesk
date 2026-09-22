using ClipDesk.Models;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClipDesk.Core;

public static class CloudProjection
{
    public static IEnumerable<CloudEntity> Project(IEnumerable<WorkspaceBoard> boards,IEnumerable<ClipboardHistoryEntry> history,string userId,bool syncHistory)
    {
        foreach(var board in boards.Where(b=>b.SyncMode!=WorkspaceSyncMode.Local))
        {
            var id=board.Id.ToString("N");
            yield return new(id,"workspace",null,0,new JsonObject { ["name"]=board.Name,["ownerId"]=board.OwnerId ?? userId,["mode"]=board.SyncMode==WorkspaceSyncMode.Shared ? "shared" : "personal",["worldWidth"]=board.WorldWidth,["worldHeight"]=board.WorldHeight,["schemaVersion"]=board.SchemaVersion,["createdAt"]=board.CreatedAt.ToUniversalTime().ToString("O") });
            foreach(var entity in Items(board.Items,id,null)) yield return entity;
            foreach(var obj in board.Objects)
                yield return ProjectBoardObject(obj,id);
        }
        if(syncHistory)
            foreach(var entry in history)
                yield return new(entry.Id,"history",null,0,new JsonObject { ["type"]=(int)entry.Type,["title"]=entry.Title,["preview"]=entry.Preview,["text"]=entry.Text,["url"]=entry.Url,["capturedAt"]=entry.CapturedAt.ToUniversalTime().ToString("O"),["attachments"]=Attachments(entry.Attachments) });
    }
    public static CloudEntity ProjectBoardObject(BoardObject obj,string workspaceId)=>new(obj.Id,"boardObject",workspaceId,0,new JsonObject
    {
        ["objectKind"]=(int)obj.Kind,["x"]=obj.X,["y"]=obj.Y,["width"]=obj.Width,["height"]=obj.Height,
        ["rotation"]=obj.Rotation,["zIndex"]=obj.ZIndex,["locked"]=obj.Locked,["createdBy"]=obj.CreatedBy,
        ["createdAt"]=obj.CreatedAt.ToUniversalTime().ToString("O"),["updatedAt"]=obj.UpdatedAt.ToUniversalTime().ToString("O"),
        ["style"]=JsonSerializer.SerializeToNode(obj.Style),["content"]=JsonSerializer.SerializeToNode(obj.Content)
    });

    public static BoardObject MaterializeBoardObject(CloudEntity entity)
    {
        var d=entity.Data;
        return new BoardObject
        {
            Id=entity.Id,WorkspaceId=entity.WorkspaceId ?? "",Kind=(BoardObjectKind)(d["objectKind"]?.GetValue<int>() ?? 0),
            X=Number(d,"x"),Y=Number(d,"y"),Width=Number(d,"width"),Height=Number(d,"height"),
            Rotation=Number(d,"rotation"),ZIndex=d["zIndex"]?.GetValue<int>() ?? 0,Locked=d["locked"]?.GetValue<bool>() ?? false,
            CreatedBy=Text(d,"createdBy"),CreatedAt=OffsetDate(d,"createdAt"),UpdatedAt=OffsetDate(d,"updatedAt"),
            Style=JsonSerializer.Deserialize<Dictionary<string,string>>(d["style"]?.ToJsonString() ?? "{}") ?? [],
            Content=JsonSerializer.Deserialize<Dictionary<string,string>>(d["content"]?.ToJsonString() ?? "{}") ?? []
        };
    }
    private static IEnumerable<CloudEntity> Items(IEnumerable<ClipboardItem> items,string workspace,string? parent)
    {
        foreach(var item in items)
        {
            yield return new(item.Id,"item",workspace,0,new JsonObject { ["type"]=(int)item.Type,["name"]=item.DisplayName,["text"]=item.Text,["url"]=item.Url,["x"]=item.X,["y"]=item.Y,["width"]=item.Width,["height"]=item.Height,["zIndex"]=item.ZIndex,["parentId"]=parent,["createdAt"]=item.CreatedAt.ToUniversalTime().ToString("O"),["attachments"]=Attachments(item.Attachments) });
            foreach(var child in Items(item.Children,workspace,item.Id)) yield return child;
        }
    }
    private static JsonArray Attachments(IEnumerable<CloudAttachment> attachments)
    {
        var array=new JsonArray();
        foreach(var a in attachments) array.Add(new JsonObject { ["id"]=a.Id,["ownerId"]=a.OwnerId,["name"]=a.Name,["size"]=a.Size,["sha256"]=a.Sha256,["isDrive"]=a.IsDrive,["uploaded"]=a.Uploaded,["driveFileId"]=a.DriveFileId,["storagePath"]=a.StoragePath,["relativePath"]=a.RelativePath });
        return array;
    }
    public static List<WorkspaceBoard> Materialize(IEnumerable<CloudEntity> source,IReadOnlyDictionary<string,string> localPaths,
        IReadOnlyDictionary<string,ClipboardItem>? deviceItems=null)
    {
        var entities=source.Where(e=>!e.Deleted).ToList(); var boards=new List<WorkspaceBoard>();
        foreach(var entity in entities.Where(e=>e.Kind=="workspace"))
        {
            var data=entity.Data;
            var board=new WorkspaceBoard { Id=Guid.Parse(entity.Id),Name=Text(data,"name") ?? "Mesa",OwnerId=Text(data,"ownerId"),SyncMode=Text(data,"mode")=="shared" ? WorkspaceSyncMode.Shared : WorkspaceSyncMode.PersonalCloud,WorldWidth=Number(data,"worldWidth"),WorldHeight=Number(data,"worldHeight"),SchemaVersion=data["schemaVersion"]?.GetValue<int>() ?? 0 };
            BoardMigration.Normalize(board);
            board.CreatedAt=Date(data,"createdAt"); board.UpdatedAt=board.CreatedAt;
            var items=entities.Where(e=>e.Kind=="item" && e.WorkspaceId==entity.Id).ToList();
            var visited=new HashSet<string>();
            List<ClipboardItem> Build(string? parent,int depth)
            {
                if(depth>32) return [];
                return items.Where(e=>Text(e.Data,"parentId")==parent && visited.Add(e.Id)).Select(e=>
                {
                    var d=e.Data; var a=ReadAttachments(d,localPaths);
                    var item=new ClipboardItem { Id=e.Id,Type=(ClipboardItemType)(d["type"]?.GetValue<int>() ?? 0),DisplayName=Text(d,"name") ?? "Item",Text=Text(d,"text"),Url=Text(d,"url"),X=Number(d,"x"),Y=Number(d,"y"),Width=Number(d,"width"),Height=Number(d,"height"),ZIndex=d["zIndex"]?.GetValue<int>() ?? 0,Attachments=a,Children=Build(e.Id,depth+1) };
                    item.CreatedAt=Date(d,"createdAt"); RestorePaths(item,a);
                    // Paths belong to this device and must never be projected to Supabase.
                    // Keep them when a remote snapshot refreshes an existing card.
                    if(deviceItems?.TryGetValue(e.Id,out var saved)==true && saved.Type==item.Type)
                    {
                        var savedAttachments=saved.Attachments.ToDictionary(attachment=>attachment.Id,StringComparer.Ordinal);
                        foreach(var attachment in item.Attachments)
                            if(savedAttachments.TryGetValue(attachment.Id,out var local) && File.Exists(local.LocalPath))
                                attachment.LocalPath=local.LocalPath;
                        if(item.Type==ClipboardItemType.Image && File.Exists(saved.StoredFilePath))
                            item.StoredFilePath=saved.StoredFilePath;
                        else if(item.IsFileBacked && item.FilePaths.Count==0)
                            item.FilePaths=saved.FilePaths.Where(path=>File.Exists(path)||Directory.Exists(path)).ToList();
                    }
                    return item;
                }).ToList();
            }
            board.Items=Build(null,0); boards.Add(board);
            board.Objects=entities.Where(e=>e.Kind=="boardObject" && e.WorkspaceId==entity.Id).Select(MaterializeBoardObject).ToList();
        }
        return boards;
    }
    public static List<ClipboardHistoryEntry> History(IEnumerable<CloudEntity> source,IReadOnlyDictionary<string,string> paths)=>source.Where(e=>e.Kind=="history" && !e.Deleted).Select(e=>
    {
        var d=e.Data; var a=ReadAttachments(d,paths);
        var entry=new ClipboardHistoryEntry { Id=e.Id,Type=(ClipboardItemType)(d["type"]?.GetValue<int>() ?? 0),Title=Text(d,"title") ?? "Item",Preview=Text(d,"preview") ?? "",Text=Text(d,"text"),Url=Text(d,"url"),CapturedAt=DateTime.TryParse(Text(d,"capturedAt"),out var time)?time.ToLocalTime():DateTime.Now,Attachments=a };
        if(entry.Type==ClipboardItemType.Image) entry.StoredFilePath=a.FirstOrDefault()?.LocalPath;
        else entry.FilePaths=a.Where(x=>x.LocalPath is not null).Select(x=>x.LocalPath!).ToList();
        return entry;
    }).OrderByDescending(e=>e.CapturedAt).ThenBy(e=>e.Id).Take(80).ToList();
    private static List<CloudAttachment> ReadAttachments(JsonObject data,IReadOnlyDictionary<string,string> localPaths)
    {
        var attachments=data["attachments"] is JsonArray array ? CloudRules.Deserialize<List<CloudAttachment>>(array.ToJsonString()) : [];
        foreach(var a in attachments) a.LocalPath=localPaths.GetValueOrDefault(a.Id);
        return attachments;
    }
    private static void RestorePaths(ClipboardItem item,List<CloudAttachment> attachments)
    {
        if(item.Type==ClipboardItemType.Image) item.StoredFilePath=attachments.FirstOrDefault()?.LocalPath;
        else item.FilePaths=attachments.Where(a=>a.LocalPath is not null).Select(a=>a.LocalPath!).ToList();
    }
    private static string? Text(JsonObject obj,string field)=>obj[field]?.GetValue<string>();
    private static DateTime Date(JsonObject obj,string field)=>DateTime.TryParse(Text(obj,field),out var date)?date.ToLocalTime():DateTime.UnixEpoch;
    private static DateTimeOffset OffsetDate(JsonObject obj,string field)=>DateTimeOffset.TryParse(Text(obj,field),out var date)?date:DateTimeOffset.UtcNow;
    private static double Number(JsonObject obj,string field) { var value=obj[field]?.GetValue<double>() ?? 0; return double.IsFinite(value)?Math.Clamp(value,0,10_000_000):0; }
    public static IEnumerable<ClipboardItem> Flatten(IEnumerable<ClipboardItem> items)=>items.SelectMany(item=>new[]{item}.Concat(Flatten(item.Children)));
}
