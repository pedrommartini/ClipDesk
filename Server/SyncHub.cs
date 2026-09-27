using ClipDesk.Core;
using Microsoft.AspNetCore.SignalR;

namespace ClipDesk.Server;

public sealed class SyncHub(CloudStore store,LiveConnections connections) : Hub
{
    public override Task OnConnectedAsync()
    {
        var userId=Context.UserIdentifier ?? throw new HubException("Entre com Google.");
        var session=Context.User?.FindFirst("sid")?.Value ?? "";
        if(!store.SessionActive(userId,session)) {Context.Abort();throw new HubException("Sessão encerrada.");}
        var caller=Context;connections.Add(Context.ConnectionId,userId,session,caller.Abort);return base.OnConnectedAsync();
    }
    public override Task OnDisconnectedAsync(Exception? exception) {connections.Remove(Context.ConnectionId);return base.OnDisconnectedAsync(exception);}
    public async Task Presence(PresenceMessage presence)
    {
        var userId=Context.UserIdentifier ?? throw new HubException("Entre com Google.");
        if(!store.SessionActive(userId,Context.User?.FindFirst("sid")?.Value ?? "")) {Context.Abort();throw new HubException("Sessão encerrada.");}
        if(!double.IsFinite(presence.X) || !double.IsFinite(presence.Y) || !double.IsFinite(presence.ViewCenterX) || !double.IsFinite(presence.ViewCenterY) || !double.IsFinite(presence.Zoom)
            || presence.X<0 || presence.Y<0 || presence.X>10_000_000 || presence.Y>10_000_000
            || presence.ViewCenterX<0 || presence.ViewCenterY<0 || presence.ViewCenterX>10_000_000 || presence.ViewCenterY>10_000_000
            || presence.Zoom<BoardViewport.MinimumZoom || presence.Zoom>3 || presence.ItemId?.Length>64) return;
        if(Context.Items.TryGetValue("presenceAt",out var last) && DateTimeOffset.UtcNow-(DateTimeOffset)last!<TimeSpan.FromMilliseconds(40)) return;
        Context.Items["presenceAt"]=DateTimeOffset.UtcNow;
        if(!store.CanAccess(userId,presence.WorkspaceId)) throw new HubException("Mesa indisponível.");
        var draft = presence.Draft;
        if(draft is not null && (!Guid.TryParse(draft.Id,out _) || draft.Kind is not ("pen" or "shape")
            || draft.Points is not {Length: >= 1 and <= 64} points
            || points.Any(p=>!double.IsFinite(p.X)||!double.IsFinite(p.Y)||p.X<0||p.Y<0||p.X>10_000_000||p.Y>10_000_000)
            || draft.Kind=="shape"&&points.Length!=2)) return;
        var members=store.Members(userId,presence.WorkspaceId); var self=members.Single(m=>m.UserId==userId);
        await Clients.Clients(connections.ActiveFor(members.Where(m=>m.UserId!=userId).Select(m=>m.UserId),store)).SendAsync("Presence",
            new CloudPresence(userId,self.Username,self.Picture,presence.WorkspaceId,presence.X,presence.Y,presence.ItemId,
                presence.ViewCenterX,presence.ViewCenterY,presence.Zoom,presence.Drags,presence.Selections,presence.Draft,presence.OriginTicks));
    }
}
