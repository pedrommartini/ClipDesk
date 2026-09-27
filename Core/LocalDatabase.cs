using Microsoft.Data.Sqlite;
using System.Text.Json.Nodes;

namespace ClipDesk.Core;

/// <summary>Profile-scoped durable documents and sync journal. Each write commits before returning.</summary>
public sealed class LocalDatabase
{
    private readonly string _connectionString;
    public LocalDatabase(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;
            CREATE TABLE IF NOT EXISTS documents (id TEXT PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS sync_entities (id TEXT PRIMARY KEY, kind TEXT NOT NULL, workspace TEXT,
              version INTEGER NOT NULL DEFAULT 0, remote_json TEXT NOT NULL, local_json TEXT NOT NULL,
              deleted INTEGER NOT NULL DEFAULT 0, remote_deleted INTEGER NOT NULL DEFAULT 0, operation TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS conflicts (id TEXT PRIMARY KEY, entity_id TEXT NOT NULL, json TEXT NOT NULL, created TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS sync_failures (operation_id TEXT PRIMARY KEY, entity_id TEXT NOT NULL,
              workspace_id TEXT, kind TEXT NOT NULL, base_version INTEGER NOT NULL, deleted INTEGER NOT NULL,
              stage TEXT NOT NULL, status_code INTEGER NOT NULL,
              attempts INTEGER NOT NULL DEFAULT 1, last_seen TEXT NOT NULL);
            """;
        cmd.ExecuteNonQuery();
    }
    private SqliteConnection Open() { var db = new SqliteConnection(_connectionString); db.Open(); return db; }
    public string? Read(string id)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT json FROM documents WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteScalar() as string;
    }
    public void Write(string id, string json)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO documents(id,json) VALUES($id,$json) ON CONFLICT(id) DO UPDATE SET json=excluded.json WHERE documents.json<>excluded.json";
        cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$json", json); cmd.ExecuteNonQuery();
    }
    public void Stage(CloudEntity entity)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sync_entities(id,kind,workspace,remote_json,local_json,deleted,operation)
            VALUES($id,$kind,$workspace,'{}',$json,$deleted,$op)
            ON CONFLICT(id) DO UPDATE SET local_json=$json,deleted=$deleted,
              operation=CASE WHEN local_json<>$json OR deleted<>$deleted THEN $op ELSE operation END
            WHERE local_json<>$json OR deleted<>$deleted
            """;
        cmd.Parameters.AddWithValue("$id", entity.Id); cmd.Parameters.AddWithValue("$kind", entity.Kind);
        cmd.Parameters.AddWithValue("$workspace", (object?)entity.WorkspaceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$json", entity.Data.ToJsonString()); cmd.Parameters.AddWithValue("$deleted", entity.Deleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$op", Guid.NewGuid().ToString("N")); cmd.ExecuteNonQuery();
    }
    public List<SyncOperation> Pending(string? entityId = null)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT operation,id,kind,workspace,version,remote_json,local_json,deleted FROM sync_entities WHERE (remote_json<>local_json OR deleted<>remote_deleted)" + (entityId is null ? "" : " AND id=$id") + " ORDER BY CASE kind WHEN 'workspace' THEN 0 ELSE 1 END,id";
        if (entityId is not null) cmd.Parameters.AddWithValue("$id", entityId);
        using var reader = cmd.ExecuteReader(); var result = new List<SyncOperation>();
        while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetInt64(4), JsonNode.Parse(reader.GetString(5))!.AsObject(), JsonNode.Parse(reader.GetString(6))!.AsObject(), reader.GetInt64(7) == 1));
        return result;
    }
    public List<CloudEntity> Entities(string? entityId = null)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id,kind,workspace,version,local_json,deleted FROM sync_entities" + (entityId is null ? "" : " WHERE id=$id");
        if (entityId is not null) cmd.Parameters.AddWithValue("$id", entityId);
        using var reader = cmd.ExecuteReader(); var result = new List<CloudEntity>();
        while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetInt64(3), JsonNode.Parse(reader.GetString(4))!.AsObject(), reader.GetInt64(5) == 1));
        return result;
    }
    public bool Accept(CloudEntity remote, SyncOperation? sent = null)
    {
        var existing = Entities(remote.Id).FirstOrDefault();
        if (existing is not null && existing.Version > remote.Version) return false;
        var pending = Pending(remote.Id).FirstOrDefault();
        if (pending is null && existing is not null && existing.Version == remote.Version && existing.Deleted == remote.Deleted && CloudRules.Same(existing.Data, remote.Data)) return false;
        var local = remote.Data;
        var deleted = remote.Deleted;
        if (pending is not null)
        {
            var basis = sent?.Data ?? pending.BaseData;
            try { local = CloudRules.MergeEntity(pending.Kind, remote.Data, basis, pending.Data); deleted = pending.Deleted; }
            catch (SyncConflictException)
            {
                PreserveConflict(pending.EntityId, pending.Data);
            }
        }
        if (existing is not null && existing.Version > remote.Version) return false;
        if(remote.Deleted)
        {
            if(pending is {Deleted:false} && !CloudRules.Same(pending.BaseData,pending.Data)) PreserveConflict(pending.EntityId,pending.Data);
            deleted=true; local=remote.Data;
        }
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sync_entities(id,kind,workspace,version,remote_json,local_json,deleted,remote_deleted,operation)
            VALUES($id,$kind,$workspace,$version,$remote,$local,$deleted,$remoteDeleted,$op)
            ON CONFLICT(id) DO UPDATE SET version=$version,remote_json=$remote,local_json=$local,deleted=$deleted,remote_deleted=$remoteDeleted,operation=$op
            """;
        cmd.Parameters.AddWithValue("$id", remote.Id); cmd.Parameters.AddWithValue("$kind", remote.Kind);
        cmd.Parameters.AddWithValue("$workspace", (object?)remote.WorkspaceId ?? DBNull.Value); cmd.Parameters.AddWithValue("$version", remote.Version);
        cmd.Parameters.AddWithValue("$remote", remote.Data.ToJsonString()); cmd.Parameters.AddWithValue("$local", local.ToJsonString());
        cmd.Parameters.AddWithValue("$deleted", deleted ? 1 : 0); cmd.Parameters.AddWithValue("$remoteDeleted", remote.Deleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$op", Guid.NewGuid().ToString("N")); cmd.ExecuteNonQuery();
        return true;
    }
    public void PreserveConflict(string entityId, JsonObject data)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO conflicts VALUES($id,$entity,$json,$created)";
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N")); cmd.Parameters.AddWithValue("$entity", entityId);
        cmd.Parameters.AddWithValue("$json", data.ToJsonString()); cmd.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O")); cmd.ExecuteNonQuery();
    }
    public int ConflictCount
    {
        get { using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM conflicts"; return Convert.ToInt32(cmd.ExecuteScalar()); }
    }
    public List<(string Id,JsonObject Data)> Conflicts()
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT id,json FROM conflicts ORDER BY created DESC LIMIT 50";
        using var reader=cmd.ExecuteReader();var result=new List<(string,JsonObject)>();
        while(reader.Read()) result.Add((reader.GetString(0),JsonNode.Parse(reader.GetString(1))!.AsObject()));return result;
    }
    public void ResolveConflict(string id)
    {
        using var db=Open();using var cmd=db.CreateCommand();cmd.CommandText="DELETE FROM conflicts WHERE id=$id";cmd.Parameters.AddWithValue("$id",id);cmd.ExecuteNonQuery();
    }
    /// <summary>Store failure metadata only; document data and credentials never enter diagnostics.</summary>
    public void RecordSyncFailure(SyncOperation operation, string stage, int statusCode)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sync_failures(operation_id,entity_id,workspace_id,kind,base_version,deleted,stage,status_code,last_seen)
            VALUES($operation,$entity,$workspace,$kind,$baseVersion,$deleted,$stage,$status,$seen)
            ON CONFLICT(operation_id) DO UPDATE SET attempts=attempts+1,status_code=$status,last_seen=$seen
            """;
        cmd.Parameters.AddWithValue("$operation", operation.OperationId);
        cmd.Parameters.AddWithValue("$entity", operation.EntityId);
        cmd.Parameters.AddWithValue("$workspace", (object?)operation.WorkspaceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$kind", operation.Kind);
        cmd.Parameters.AddWithValue("$baseVersion", operation.BaseVersion);
        cmd.Parameters.AddWithValue("$deleted", operation.Deleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$stage", stage);
        cmd.Parameters.AddWithValue("$status", statusCode);
        cmd.Parameters.AddWithValue("$seen", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }
    public List<(string OperationId, string EntityId, string? WorkspaceId, string Kind, long BaseVersion, bool Deleted, string Stage, int StatusCode, int Attempts, string LastSeen)> SyncFailures()
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT operation_id,entity_id,workspace_id,kind,base_version,deleted,stage,status_code,attempts,last_seen FROM sync_failures ORDER BY last_seen DESC LIMIT 100";
        using var reader = cmd.ExecuteReader();
        var result = new List<(string, string, string?, string, long, bool, string, int, int, string)>();
        while (reader.Read()) result.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3), reader.GetInt64(4), reader.GetInt64(5) != 0, reader.GetString(6), reader.GetInt32(7),
            reader.GetInt32(8), reader.GetString(9)));
        return result;
    }
    public void ForgetWorkspace(string workspaceId)
    {
        // Revocation/deletion may arrive while this device still has offline edits.
        // Keep a recoverable local conflict before hiding the former workspace.
        foreach (var operation in Pending().Where(op => op.EntityId == workspaceId || op.WorkspaceId == workspaceId))
            PreserveConflict(operation.EntityId, operation.Data);
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM sync_entities WHERE workspace=$id OR id=$id";
        cmd.Parameters.AddWithValue("$id", workspaceId); cmd.ExecuteNonQuery();
    }
    public void ForgetEntity(string entityId)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM sync_entities WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", entityId); cmd.ExecuteNonQuery();
    }
}
