using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ClipDesk.Core;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace ClipDesk.Server;

public sealed class CloudStore
{
    private readonly string _connectionString;
    private readonly bool _postgres;
    public object Gate { get; } = new();
    public CloudStore(IConfiguration config, IHostEnvironment environment)
    {
        _connectionString = config.GetConnectionString("ClipDesk") ?? "";
        _postgres = !string.IsNullOrWhiteSpace(_connectionString);
        if (!_postgres)
        {
            if (!environment.IsDevelopment()) throw new InvalidOperationException("Configure ConnectionStrings__ClipDesk para PostgreSQL em produção.");
            var path = Path.GetFullPath(config["LocalDatabase"] ?? Path.Combine(environment.ContentRootPath, "data", "development.db"));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        }
        using var db = Open();
        Execute(db, """
            CREATE TABLE IF NOT EXISTS users(id TEXT PRIMARY KEY,google_sub TEXT NOT NULL UNIQUE,email TEXT NOT NULL,name TEXT NOT NULL,picture TEXT,username TEXT UNIQUE);
            CREATE TABLE IF NOT EXISTS sessions(hash TEXT PRIMARY KEY,user_id TEXT NOT NULL REFERENCES users(id),expires TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS entities(id TEXT PRIMARY KEY,kind TEXT NOT NULL,workspace TEXT,owner TEXT NOT NULL REFERENCES users(id),version BIGINT NOT NULL,data TEXT NOT NULL,deleted INTEGER NOT NULL DEFAULT 0);
            CREATE INDEX IF NOT EXISTS entities_workspace ON entities(workspace);
            CREATE INDEX IF NOT EXISTS entities_owner ON entities(owner);
            CREATE TABLE IF NOT EXISTS members(workspace TEXT NOT NULL REFERENCES entities(id),user_id TEXT NOT NULL REFERENCES users(id),role TEXT NOT NULL,PRIMARY KEY(workspace,user_id));
            CREATE TABLE IF NOT EXISTS operations(id TEXT PRIMARY KEY,user_id TEXT NOT NULL,result TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS invitations(id TEXT PRIMARY KEY,workspace TEXT NOT NULL REFERENCES entities(id),from_user TEXT NOT NULL REFERENCES users(id),to_user TEXT NOT NULL REFERENCES users(id),status TEXT NOT NULL,UNIQUE(workspace,to_user));
            CREATE TABLE IF NOT EXISTS attachments(id TEXT PRIMARY KEY,owner TEXT NOT NULL REFERENCES users(id),workspace TEXT,name TEXT NOT NULL,size BIGINT NOT NULL,sha256 TEXT NOT NULL,drive INTEGER NOT NULL,ready INTEGER NOT NULL DEFAULT 0,file_id TEXT,content BYTEA);
            CREATE INDEX IF NOT EXISTS attachments_workspace ON attachments(workspace);
            """);
    }
    public DbConnection Open()
    {
        DbConnection db = _postgres ? new NpgsqlConnection(_connectionString) : new SqliteConnection(_connectionString);
        db.Open(); return db;
    }
    public static DbCommand Command(DbConnection db, string sql, params (string, object?)[] args)
    {
        var cmd = db.CreateCommand(); cmd.CommandText = sql;
        if (_activeTransactions.Value?.Connection == db) cmd.Transaction = _activeTransactions.Value;
        foreach (var (name,value) in args) { var p = cmd.CreateParameter(); p.ParameterName = name; p.Value = value ?? DBNull.Value; cmd.Parameters.Add(p); }
        return cmd;
    }
    public static int Execute(DbConnection db, string sql, params (string,object?)[] args)
    { using var cmd = Command(db, sql, args); return cmd.ExecuteNonQuery(); }
    public static object? Scalar(DbConnection db, string sql, params (string,object?)[] args)
    { using var cmd = Command(db, sql, args); var value = cmd.ExecuteScalar(); return value is DBNull ? null : value; }
    public static List<Dictionary<string,object?>> Rows(DbConnection db, string sql, params (string,object?)[] args)
    {
        using var cmd = Command(db,sql,args); using var r = cmd.ExecuteReader(); var rows = new List<Dictionary<string,object?>>();
        while (r.Read()) { var row = new Dictionary<string,object?>(); for (var i=0;i<r.FieldCount;i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i); rows.Add(row); } return rows;
    }
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public CloudUser? Authenticate(string token)
    {
        if (token.Length < 32 || token.Length > 200) return null;
        using var db=Open(); var row=Rows(db,"SELECT u.* FROM users u JOIN sessions s ON s.user_id=u.id WHERE s.hash=@hash AND s.expires>@now",("hash",Hash(token)),("now",DateTimeOffset.UtcNow.ToString("O"))).FirstOrDefault();
        return row is null ? null : User(row);
    }
    public static CloudUser User(Dictionary<string,object?> r) => new((string)r["id"]!,r["username"] as string,(string)r["name"]!,r["picture"] as string);
    public CloudSession Login(string sub,string email,string name,string? picture)
    {
        lock(Gate)
        {
            using var db=Open(); var id=Guid.NewGuid().ToString("N");
            Execute(db,"INSERT INTO users(id,google_sub,email,name,picture) VALUES(@id,@sub,@email,@name,@picture) ON CONFLICT(google_sub) DO UPDATE SET email=@email,name=@name,picture=@picture",("id",id),("sub",sub),("email",email),("name",name),("picture",picture));
            var user=User(Rows(db,"SELECT * FROM users WHERE google_sub=@sub",("sub",sub)).Single());
            var token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            Execute(db,"INSERT INTO sessions VALUES(@hash,@id,@expires)",("hash",Hash(token)),("id",user.Id),("expires",DateTimeOffset.UtcNow.AddDays(7).ToString("O")));
            return new(token,user);
        }
    }
    public CloudUser SetUsername(string userId,string name)
    {
        name=name.Trim().ToLowerInvariant(); if (!CloudRules.IsUsernameValid(name)) throw new ArgumentException("Use 3–24 letras, números ou sublinhado.");
        lock(Gate)
        {
            using var db=Open();
            if (Scalar(db,"SELECT id FROM users WHERE username=@name AND id<>@id",("name",name),("id",userId)) is not null) throw new SyncConflictException("username");
            try { Execute(db,"UPDATE users SET username=@name WHERE id=@id",("name",name),("id",userId)); }
            catch(DbException) { throw new SyncConflictException("username"); }
            return User(Rows(db,"SELECT * FROM users WHERE id=@id",("id",userId)).Single());
        }
    }
    public static bool CanAccess(DbConnection db,string userId,string workspace) =>
        Scalar(db,"SELECT e.id FROM entities e JOIN members m ON m.workspace=e.id WHERE e.id=@w AND m.user_id=@u AND e.deleted=0",("w",workspace),("u",userId)) is not null;
    public static bool IsOwner(DbConnection db,string userId,string workspace) =>
        Scalar(db,"SELECT id FROM entities WHERE id=@w AND owner=@u AND kind='workspace' AND deleted=0",("w",workspace),("u",userId)) is not null;
    public bool CanAccess(string userId,string workspace) { using var db=Open(); return CanAccess(db,userId,workspace); }
    private static CloudEntity Entity(Dictionary<string,object?> r) => new((string)r["id"]!, (string)r["kind"]!,r["workspace"] as string,Convert.ToInt64(r["version"]),JsonNode.Parse((string)r["data"]!)!.AsObject(),Convert.ToInt32(r["deleted"])!=0);
    public List<CloudEntity> GetEntities(string userId)
    {
        using var db=Open();
        return Rows(db,"""
            SELECT e.* FROM entities e WHERE (e.kind='history' AND e.owner=@u)
            OR (e.kind='workspace' AND EXISTS(SELECT 1 FROM members m WHERE m.workspace=e.id AND m.user_id=@u))
            OR (e.kind IN ('item','boardObject') AND EXISTS(SELECT 1 FROM members m JOIN entities w ON w.id=m.workspace WHERE m.workspace=e.workspace AND m.user_id=@u AND w.deleted=0))
            """,("u",userId)).Select(Entity).ToList();
    }
    public CloudEntity Apply(string userId,SyncOperation op)
    {
        ValidateData(op);
        if (op.Kind is not ("workspace" or "item" or "boardObject" or "history") || !Guid.TryParse(op.EntityId,out _) || !Guid.TryParse(op.OperationId,out _)) throw new ArgumentException("Operação inválida.");
        if (op.Data.ToJsonString().Length>1_000_000 || op.BaseData.ToJsonString().Length>1_000_000) throw new ArgumentException("Conteúdo muito grande.");
        if (op.Data.ContainsKey("filePaths") || op.Data.ContainsKey("storedFilePath")) throw new ArgumentException("Caminhos locais não são dados de nuvem.");
        lock(Gate)
        {
            using var db=Open(); using var tx=db.BeginTransaction();
            // Commands enlist in the connection transaction (explicit for SQLite).
            CloudEntity Work()
            {
                var prior=Scalar(db,"SELECT result FROM operations WHERE id=@id AND user_id=@u",("id",op.OperationId),("u",userId)) as string;
                var row=Rows(db,"SELECT * FROM entities WHERE id=@id",("id",op.EntityId)).FirstOrDefault();
                if(row is not null && ((string)row["kind"]! != op.Kind || (row["workspace"] as string)!=op.WorkspaceId)) throw new UnauthorizedAccessException();
                if(op.Kind is "item" or "boardObject" && (op.WorkspaceId is null || !CanAccess(db,userId,op.WorkspaceId))) throw new UnauthorizedAccessException();
                if(op.Kind=="history" && (op.WorkspaceId is not null || (row is not null && (string)row["owner"]! != userId))) throw new UnauthorizedAccessException();
                if(op.Kind=="workspace")
                {
                    if(op.WorkspaceId is not null) throw new ArgumentException("Mesa inválida.");
                    if(row is not null && (string)row["owner"]! != userId) throw new UnauthorizedAccessException();
                }
                if(prior is not null)
                {
                    var previous=CloudRules.Deserialize<CloudEntity>(prior);
                    if(previous.Id!=op.EntityId || previous.Kind!=op.Kind || previous.WorkspaceId!=op.WorkspaceId) throw new ArgumentException("Identificador de operação reutilizado.");
                    return previous;
                }
                var current=row is null ? new JsonObject() : JsonNode.Parse((string)row["data"]!)!.AsObject();
                if(row is not null && Convert.ToInt32(row["deleted"])!=0 && !op.Deleted) throw new SyncConflictException("exclusão");
                var merged=CloudRules.Merge(current,op.BaseData,op.Data);
                if(op.Kind=="workspace") { merged["ownerId"]=userId; merged["mode"]=Rows(db,"SELECT user_id FROM members WHERE workspace=@w",("w",op.EntityId)).Count>1 ? "shared" : "personal"; }
                if(merged["attachments"] is JsonArray attachments)
                    foreach(var a in attachments)
                    {
                        var id=a?["id"]?.GetValue<string>() ?? "";
                        var ar=Rows(db,"SELECT owner,workspace,name,size,sha256,drive,ready,file_id FROM attachments WHERE id=@id",("id",id)).FirstOrDefault();
                        if(ar is null || (ar["workspace"] as string)!=op.WorkspaceId || (op.Kind=="history" && (string)ar["owner"]! != userId)) throw new UnauthorizedAccessException();
                        if(a is JsonObject obj)
                        {
                            obj.Remove("localPath"); obj["ownerId"]=(string)ar["owner"]!; obj["name"]=(string)ar["name"]!;
                            obj["size"]=Convert.ToInt64(ar["size"]); obj["sha256"]=(string)ar["sha256"]!;
                            obj["isDrive"]=Convert.ToInt32(ar["drive"])!=0; obj["uploaded"]=Convert.ToInt32(ar["ready"])!=0; obj["driveFileId"]=ar["file_id"] as string;
                        }
                    }
                var version=row is null ? 1 : Convert.ToInt64(row["version"])+1;
                if(row is null) Execute(db,"INSERT INTO entities VALUES(@id,@kind,@w,@u,@v,@data,@deleted)",("id",op.EntityId),("kind",op.Kind),("w",op.WorkspaceId),("u",userId),("v",version),("data",merged.ToJsonString()),("deleted",op.Deleted?1:0));
                else if(Execute(db,"UPDATE entities SET version=@v,data=@data,deleted=@deleted WHERE id=@id AND version=@old",("v",version),("data",merged.ToJsonString()),("deleted",op.Deleted?1:0),("id",op.EntityId),("old",version-1))!=1) throw new SyncConflictException("versão");
                if(op.Kind=="workspace" && row is null) Execute(db,"INSERT INTO members VALUES(@w,@u,'owner')",("w",op.EntityId),("u",userId));
                var result=new CloudEntity(op.EntityId,op.Kind,op.WorkspaceId,version,merged,op.Deleted);
                Execute(db,"INSERT INTO operations VALUES(@id,@u,@result)",("id",op.OperationId),("u",userId),("result",CloudRules.Serialize(result)));
                return result;
            }
            // SQLite requires Transaction on each command; connection wrapper handles this below.
            _activeTransactions.Value=tx;
            try { var result=Work(); tx.Commit(); return result; } finally { _activeTransactions.Value=null; }
        }
    }
    private static readonly AsyncLocal<DbTransaction?> _activeTransactions=new();
    private static void ValidateData(SyncOperation op)
    {
        if(op.Data is null || op.BaseData is null) throw new ArgumentException("Conteúdo inválido.");
        var allowed=op.Kind switch
        {
            "workspace"=>new[]{"name","ownerId","mode","worldWidth","worldHeight","schemaVersion","createdAt"},
            "item"=>new[]{"type","name","text","url","x","y","width","height","zIndex","parentId","createdAt","attachments"},
            "boardObject"=>new[]{"objectKind","pluginId","pluginName","pluginVersion","x","y","width","height","rotation","zIndex","locked","createdBy","createdAt","updatedAt","style","content"},
            "history"=>new[]{"type","title","preview","text","url","capturedAt","attachments"},
            _=>[]
        };
        if(op.Data.Any(p=>!allowed.Contains(p.Key))) throw new ArgumentException("Campo de sincronização inválido.");
        foreach(var key in new[]{"name","ownerId","mode","createdAt","title","preview","text","url","capturedAt","parentId","pluginId","pluginName","pluginVersion"})
            if(op.Data[key] is { } node && (node is not JsonValue value || !value.TryGetValue<string>(out _))) throw new ArgumentException("Texto inválido.");
        if(op.Data["type"] is { } kind && (!int.TryParse(kind.ToJsonString(),out var type) || type is <0 or >5)) throw new ArgumentException("Tipo de card inválido.");
        if(op.Data["objectKind"] is { } objectKind
            && (!int.TryParse(objectKind.ToJsonString(),out var objectType)
                || !Enum.IsDefined(typeof(BoardObjectKind),objectType)))
            throw new ArgumentException("Tipo de objeto inválido.");
        if(op.Data["zIndex"] is { } zIndex && (!int.TryParse(zIndex.ToJsonString(),out var depth) || depth is <0 or >1_000_000)) throw new ArgumentException("Profundidade inválida.");
        foreach(var key in new[]{"x","y","width","height"})
            if(op.Data[key] is { } number && (!double.TryParse(number.ToJsonString(),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value) || !double.IsFinite(value) || value<0 || value>10_000_000)) throw new ArgumentException("Posição ou tamanho inválido.");
        foreach(var key in new[]{"worldWidth","worldHeight"})
            if(op.Data[key] is { } number && (!double.TryParse(number.ToJsonString(),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value) || !double.IsFinite(value) || value<1 || value>100_000)) throw new ArgumentException("Dimensões da mesa inválidas.");
        if(op.Data["schemaVersion"] is { } schema && (!schema.AsValue().TryGetValue<int>(out var version) || version is <1 or >100)) throw new ArgumentException("Versão da mesa inválida.");
        if(op.Data["url"]?.GetValue<string>() is {Length:>0} url && (!Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Scheme is not ("http" or "https"))) throw new ArgumentException("Somente links HTTP ou HTTPS podem ser sincronizados.");
        if(op.Data["parentId"]?.GetValue<string>() is { } parent && (!Guid.TryParse(parent,out _) || parent==op.EntityId)) throw new ArgumentException("Pasta de destino inválida.");
        if(op.Data["attachments"] is { } attachments && (attachments is not JsonArray array || array.Count>1000 || array.Any(a=>a is not JsonObject))) throw new ArgumentException("Lista de anexos inválida.");
        foreach(var key in new[]{"style","content"})
            if(op.Data[key] is { } map && (map is not JsonObject obj || obj.Count>128 || obj.Any(p=>p.Value is not JsonValue value || !value.TryGetValue<string>(out var text) || text.Length>200_000))) throw new ArgumentException("Objeto criativo inválido.");
    }
    public List<CloudMember> Members(string userId,string workspace)
    {
        using var db=Open(); if(!CanAccess(db,userId,workspace)) throw new UnauthorizedAccessException();
        return Rows(db,"SELECT u.id,u.username,u.picture,m.role FROM users u JOIN members m ON m.user_id=u.id WHERE m.workspace=@w",("w",workspace))
            .Select(r=>new CloudMember((string)r["id"]!,r["username"] as string ?? "",r["picture"] as string,(string)r["role"]!)).ToList();
    }
    public void Invite(string userId,string workspace,string username)
    {
        lock(Gate)
        {
            using var db=Open(); if(!IsOwner(db,userId,workspace)) throw new UnauthorizedAccessException();
            var target=Scalar(db,"SELECT id FROM users WHERE username=@name",("name",username.Trim().ToLowerInvariant())) as string ?? throw new ArgumentException("Username não encontrado.");
            if(target==userId) throw new ArgumentException("Essa é sua própria conta.");
            Execute(db,"INSERT INTO invitations VALUES(@id,@w,@from,@to,'pending') ON CONFLICT(workspace,to_user) DO UPDATE SET status='pending'",("id",Guid.NewGuid().ToString("N")),("w",workspace),("from",userId),("to",target));
        }
    }
    public List<DriveGrant> MemberDriveGrants(string userId,string workspace,string memberId)
    {
        using var db=Open();
        if(!IsOwner(db,userId,workspace) || Scalar(db,"SELECT user_id FROM members WHERE workspace=@w AND user_id=@m",("w",workspace),("m",memberId)) is null) throw new UnauthorizedAccessException();
        var email=Scalar(db,"SELECT email FROM users WHERE id=@u",("u",memberId)) as string ?? throw new ArgumentException("Colaborador não encontrado.");
        return Rows(db,"SELECT file_id,workspace,version FROM attachments a JOIN entities w ON w.id=a.workspace WHERE a.workspace=@w AND a.owner=@u AND a.drive=1 AND a.ready=1",("w",workspace),("u",userId))
            .Select(r=>new DriveGrant((string)r["file_id"]!,email,(string)r["workspace"]!,Convert.ToInt64(r["version"]))).ToList();
    }
    public void RemoveMember(string userId,string workspace,string memberId)
    {
        lock(Gate)
        {
            using var db=Open();
            if(!IsOwner(db,userId,workspace)) throw new UnauthorizedAccessException();
            if(memberId==userId) throw new ArgumentException("O proprietário não pode ser removido.");
            if(Scalar(db,"SELECT user_id FROM members WHERE workspace=@w AND user_id=@m",("w",workspace),("m",memberId)) is null) throw new ArgumentException("Colaborador não encontrado.");
            Execute(db,"DELETE FROM members WHERE workspace=@w AND user_id=@m",("w",workspace),("m",memberId));
            Execute(db,"UPDATE invitations SET status='cancelled' WHERE workspace=@w AND to_user=@m",("w",workspace),("m",memberId));
        }
    }
    public List<CloudInvitation> Invitations(string userId)
    {
        using var db=Open(); return Rows(db,"SELECT i.id,i.workspace,e.data,u.username,u.name,u.picture FROM invitations i JOIN entities e ON e.id=i.workspace JOIN users u ON u.id=i.from_user WHERE i.to_user=@u AND i.status='pending' AND e.deleted=0",("u",userId))
            .Select(r=>new CloudInvitation((string)r["id"]!,(string)r["workspace"]!,JsonNode.Parse((string)r["data"]!)?["name"]?.GetValue<string>() ?? "Mesa",r["username"] as string ?? "",r["name"] as string ?? "",r["picture"] as string)).ToList();
    }
    public string AcceptInvitation(string userId,string invitationId)
    {
        lock(Gate)
        {
            using var db=Open(); using var tx=db.BeginTransaction(); _activeTransactions.Value=tx;
            try
            {
                var workspace=Scalar(db,"SELECT i.workspace FROM invitations i JOIN entities e ON e.id=i.workspace WHERE i.id=@id AND i.to_user=@u AND i.status='pending' AND e.deleted=0",("id",invitationId),("u",userId)) as string ?? throw new UnauthorizedAccessException();
                Execute(db,"INSERT INTO members VALUES(@w,@u,'editor') ON CONFLICT(workspace,user_id) DO NOTHING",("w",workspace),("u",userId));
                Execute(db,"UPDATE invitations SET status='accepted' WHERE id=@id",("id",invitationId));
                var json=JsonNode.Parse((string)Scalar(db,"SELECT data FROM entities WHERE id=@w",("w",workspace))!)!.AsObject(); json["mode"]="shared";
                Execute(db,"UPDATE entities SET data=@data,version=version+1 WHERE id=@w",("data",json.ToJsonString()),("w",workspace)); tx.Commit(); return workspace;
            } finally { _activeTransactions.Value=null; }
        }
    }
    public void DeclineInvitation(string userId,string invitationId)
    {
        using var db=Open();
        if(Execute(db,"UPDATE invitations SET status='declined' WHERE id=@id AND to_user=@u AND status='pending'",("id",invitationId),("u",userId))!=1) throw new UnauthorizedAccessException();
    }
    public void RegisterAttachment(string userId,AttachmentRegistration a)
    {
        if(!Guid.TryParse(a.Id,out _) || a.Size<0 || a.Drive!=CloudRules.UsesDrive(a.Size) || a.Name.Length is <1 or >255 || a.Sha256.Length!=64) throw new ArgumentException("Anexo inválido.");
        using var db=Open(); if(a.WorkspaceId is not null && !CanAccess(db,userId,a.WorkspaceId)) throw new UnauthorizedAccessException();
        var existing=Rows(db,"SELECT * FROM attachments WHERE id=@id",("id",a.Id)).FirstOrDefault();
        if(existing is not null)
        {
            if((string)existing["owner"]! != userId || (existing["workspace"] as string)!=a.WorkspaceId || (string)existing["sha256"]! != a.Sha256 || Convert.ToInt64(existing["size"])!=a.Size) throw new UnauthorizedAccessException();
            return;
        }
        Execute(db,"INSERT INTO attachments(id,owner,workspace,name,size,sha256,drive) VALUES(@id,@u,@w,@name,@size,@hash,@drive)",("id",a.Id),("u",userId),("w",a.WorkspaceId),("name",a.Name),("size",a.Size),("hash",a.Sha256),("drive",a.Drive?1:0));
    }
    public Dictionary<string,object?> Attachment(string userId,string id,bool ownerOnly=false)
    {
        using var db=Open(); var row=Rows(db,"SELECT id,owner,workspace,name,size,sha256,drive,ready,file_id FROM attachments WHERE id=@id",("id",id)).FirstOrDefault() ?? throw new UnauthorizedAccessException();
        if((string)row["owner"]! != userId && (ownerOnly || row["workspace"] is not string w || !CanAccess(db,userId,w))) throw new UnauthorizedAccessException();
        return row;
    }
    public void StoreBytes(string userId,string id,byte[] bytes)
    {
        var a=Attachment(userId,id,true);
        if(Convert.ToInt32(a["drive"])!=0 || bytes.LongLength!=Convert.ToInt64(a["size"]) || !Convert.ToHexString(SHA256.HashData(bytes)).Equals((string)a["sha256"]!,StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Integridade do arquivo inválida.");
        using var db=Open(); Execute(db,"UPDATE attachments SET content=@bytes,ready=1 WHERE id=@id AND owner=@u",("bytes",bytes),("id",id),("u",userId));
    }
    public byte[] Download(string userId,string id)
    {
        var a=Attachment(userId,id); if(Convert.ToInt32(a["ready"])==0 || Convert.ToInt32(a["drive"])!=0) throw new ArgumentException("Arquivo indisponível.");
        using var db=Open(); return (byte[])Scalar(db,"SELECT content FROM attachments WHERE id=@id",("id",id))!;
    }
    public void CompleteDrive(string userId,string id,string fileId)
    {
        var a=Attachment(userId,id,true); if(Convert.ToInt32(a["drive"])!=1) throw new ArgumentException("Anexo não pertence ao Drive.");
        using var db=Open(); Execute(db,"UPDATE attachments SET file_id=@file,ready=1 WHERE id=@id AND owner=@u",("file",fileId),("id",id),("u",userId));
    }
    public List<DriveGrant> DriveGrants(string userId)
    {
        using var db=Open(); return Rows(db,"SELECT a.file_id,u.email,a.workspace,w.version FROM attachments a JOIN entities w ON w.id=a.workspace JOIN members m ON m.workspace=a.workspace JOIN users u ON u.id=m.user_id WHERE a.owner=@u AND a.drive=1 AND a.ready=1 AND m.user_id<>@u AND w.deleted=0",("u",userId))
            .Select(r=>new DriveGrant((string)r["file_id"]!,(string)r["email"]!,(string)r["workspace"]!,Convert.ToInt64(r["version"]))).Distinct().ToList();
    }
    public void CheckPrivateTransition(string userId,string workspace)
    {
        using var db=Open(); if(!IsOwner(db,userId,workspace)) throw new UnauthorizedAccessException();
        if(Scalar(db,"SELECT id FROM attachments WHERE workspace=@w AND owner<>@u AND drive=1 AND ready=1",("w",workspace),("u",userId)) is not null)
            throw new ArgumentException("Esta mesa tem arquivos do Drive de outros colaboradores. A remoção coordenada dessas permissões precisa ser concluída antes de torná-la particular.");
    }
    public void MakePersonal(string userId,string workspace)
    {
        lock(Gate)
        {
            CheckPrivateTransition(userId,workspace);
            using var db=Open();using var tx=db.BeginTransaction();_activeTransactions.Value=tx;
            try
            {
                Execute(db,"DELETE FROM members WHERE workspace=@w AND user_id<>@u",("w",workspace),("u",userId));
                Execute(db,"UPDATE invitations SET status='cancelled' WHERE workspace=@w",("w",workspace));
                var data=JsonNode.Parse((string)Scalar(db,"SELECT data FROM entities WHERE id=@w",("w",workspace))!)!.AsObject();data["mode"]="personal";
                Execute(db,"UPDATE entities SET data=@data,version=version+1 WHERE id=@w",("data",data.ToJsonString()),("w",workspace));tx.Commit();
            }finally{_activeTransactions.Value=null;}
        }
    }
    public string GoogleSubject(string userId) { using var db=Open(); return (string)Scalar(db,"SELECT google_sub FROM users WHERE id=@id",("id",userId))!; }
    public bool SessionActive(string userId,string hash)
    {using var db=Open();return Scalar(db,"SELECT hash FROM sessions WHERE hash=@hash AND user_id=@u AND expires>@now",("hash",hash),("u",userId),("now",DateTimeOffset.UtcNow.ToString("O"))) is not null;}
    public void Logout(string token) { using var db=Open(); Execute(db,"DELETE FROM sessions WHERE hash=@hash",("hash",Hash(token))); }
}
