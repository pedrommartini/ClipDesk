using System.Collections.Concurrent;

namespace ClipDesk.Server;

public sealed class LiveConnections
{
    private sealed record Connection(string UserId,string SessionHash,Action Abort);
    private readonly ConcurrentDictionary<string,Connection> _connections=new();
    public void Add(string connectionId,string userId,string sessionHash,Action abort)=>_connections[connectionId]=new(userId,sessionHash,abort);
    public void Remove(string connectionId)=>_connections.TryRemove(connectionId,out _);
    public string[] ActiveFor(IEnumerable<string> users,CloudStore store)
    {
        var targets=users.ToHashSet();var result=new List<string>();
        foreach(var pair in _connections.Where(p=>targets.Contains(p.Value.UserId)))
        {
            if(store.SessionActive(pair.Value.UserId,pair.Value.SessionHash)) result.Add(pair.Key);
            else {pair.Value.Abort();Remove(pair.Key);}
        }
        return result.ToArray();
    }
    public void Revoke(string hash)
    {
        foreach(var pair in _connections.Where(p=>p.Value.SessionHash==hash)) {pair.Value.Abort();Remove(pair.Key);}
    }
}
