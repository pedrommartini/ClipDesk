using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClipDesk.Core;

namespace ClipDesk.Services;

public sealed class CloudConfiguration
{
    public string ServerUrl { get; set; } = AppEnvironment.IsDevelopment ? "http://127.0.0.1:5278" : "";
    public string GoogleClientId { get; set; } = "";
    public string GoogleDesktopClientSecret { get; set; } = "";
    // Only test installers set this. Production continues to require HTTPS.
    public bool AllowInsecureLanServer { get; set; }
    public static string FilePath => Path.Combine(AppEnvironment.DataRoot,"cloud-config.local.json");
    private static string BootstrapPath => Path.Combine(AppContext.BaseDirectory,"clipdesk-dev-cloud-bootstrap.json");
    public static CloudConfiguration Load()
    {
        try
        {
            CloudConfiguration? installedBootstrap=null;
            if(AppEnvironment.IsDevelopment && File.Exists(BootstrapPath))
            {
                var initial=CloudRules.Deserialize<CloudConfiguration>(File.ReadAllText(BootstrapPath));
                if(!string.IsNullOrWhiteSpace(initial.ServerUrl) && !string.IsNullOrWhiteSpace(initial.GoogleClientId))
                {
                    installedBootstrap=initial;
                }
            }
            if(File.Exists(FilePath))
            {
                var saved=CloudRules.Deserialize<CloudConfiguration>(File.ReadAllText(FilePath));
                if(installedBootstrap is null) return saved;
                // DEV installers own their connection endpoint. This upgrades a prior local/LAN
                // configuration when the same tester installs the current public collaboration build.
                saved.ServerUrl=installedBootstrap.ServerUrl;
                saved.GoogleClientId=installedBootstrap.GoogleClientId;
                saved.GoogleDesktopClientSecret=installedBootstrap.GoogleDesktopClientSecret;
                saved.AllowInsecureLanServer=false;
                File.WriteAllText(FilePath,CloudRules.Serialize(saved));
                return saved;
            }
            if(installedBootstrap is not null)
            {
                Directory.CreateDirectory(AppEnvironment.DataRoot);
                File.WriteAllText(FilePath,CloudRules.Serialize(installedBootstrap));
                return installedBootstrap;
            }
            return new();
        }
        catch(Exception e) when(e is IOException or JsonException) {return new CloudConfiguration {ServerUrl=""};}
    }
    public Uri ServerUri()
    {
        if(!Uri.TryCreate(ServerUrl,UriKind.Absolute,out var uri) || !string.IsNullOrEmpty(uri.UserInfo) || uri.Query.Length>0 || uri.Fragment.Length>0
            || (uri.Scheme!="https" && !(AppEnvironment.IsDevelopment && uri.Scheme=="http" && (uri.IsLoopback || AllowInsecureLanServer && IsPrivateLanAddress(uri)))))
            throw new InvalidOperationException("Configure uma URL HTTPS do servidor ClipDesk. No DEV, HTTP é permitido somente no próprio computador.");
        return new Uri(uri.AbsoluteUri.TrimEnd('/')+"/");
    }
    private static bool IsPrivateLanAddress(Uri uri)
    {
        if(!IPAddress.TryParse(uri.Host,out var address) || address.AddressFamily!=AddressFamily.InterNetwork) return false;
        var bytes=address.GetAddressBytes();
        return bytes[0]==10 || bytes[0]==192 && bytes[1]==168 || bytes[0]==172 && bytes[1] is >=16 and <=31;
    }
}
public sealed record GoogleTokens(string AccessToken,string RefreshToken,DateTimeOffset Expires,string IdToken);
public sealed record SavedCloudAccount(CloudSession Session,GoogleTokens Google,string ServerUrl,string RootFolderId,string GoogleClientId="");
public sealed record SavedDriveAccount(string UserId,GoogleTokens Google,string RootFolderId,string GoogleClientId);

public static class SecureDriveAccountFile
{
    private static string PathName => Path.Combine(AppEnvironment.DataRoot,"drive-account.protected");
    public static SavedDriveAccount? Load()
    {
        try
        {
            if(!File.Exists(PathName)) return null;
            var bytes=ProtectedData.Unprotect(File.ReadAllBytes(PathName),null,DataProtectionScope.CurrentUser);
            return CloudRules.Deserialize<SavedDriveAccount>(Encoding.UTF8.GetString(bytes));
        }
        catch(Exception e) when(e is IOException or CryptographicException or JsonException) { return null; }
    }
    public static void Save(SavedDriveAccount account)
    {
        Directory.CreateDirectory(AppEnvironment.DataRoot);
        var bytes=ProtectedData.Protect(Encoding.UTF8.GetBytes(CloudRules.Serialize(account)),null,DataProtectionScope.CurrentUser);
        File.WriteAllBytes(PathName+".tmp",bytes); File.Move(PathName+".tmp",PathName,true);
    }
    public static void Clear() { if(File.Exists(PathName)) File.Delete(PathName); }
}

public static class SecureAccountFile
{
    private static string PathName => Path.Combine(AppEnvironment.DataRoot,"account.protected");
    public static SavedCloudAccount? Load()
    {
        try { return File.Exists(PathName) ? CloudRules.Deserialize<SavedCloudAccount>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(PathName),null,DataProtectionScope.CurrentUser))) : null; }
        catch(Exception e) when(e is IOException or CryptographicException or JsonException) { return null; }
    }
    public static void Save(SavedCloudAccount account)
    {
        Directory.CreateDirectory(AppEnvironment.DataRoot);
        File.WriteAllBytes(PathName+".tmp",ProtectedData.Protect(Encoding.UTF8.GetBytes(CloudRules.Serialize(account)),null,DataProtectionScope.CurrentUser));
        File.Move(PathName+".tmp",PathName,true);
    }
    public static void Clear() { if(File.Exists(PathName)) File.Delete(PathName); }
}

public sealed class GoogleConnection(CloudConfiguration configuration, bool requestDrive = true)
{
    private readonly HttpClient _http=new(new RequestCooldownHandler()) { Timeout=TimeSpan.FromMinutes(5) };
    public event Action<GoogleTokens>? TokensRefreshed;
    private static string Base64(byte[] value)=>Convert.ToBase64String(value).TrimEnd('=').Replace('+','-').Replace('/','_');
    public async Task<GoogleTokens> SignInAsync(string nonce,CancellationToken cancellation)
    {
        if(string.IsNullOrWhiteSpace(configuration.GoogleClientId)) throw new InvalidOperationException("Configure o cliente OAuth Google Desktop antes de entrar.");
        var verifier=Base64(RandomNumberGenerator.GetBytes(48)); var challenge=Base64(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state=Base64(RandomNumberGenerator.GetBytes(32));
        var listener=new TcpListener(IPAddress.Loopback,0); listener.Start();
        try
        {
            var redirect=$"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/callback";
            var url="https://accounts.google.com/o/oauth2/v2/auth?"+Encode(new()
            {
                ["client_id"]=configuration.GoogleClientId,["redirect_uri"]=redirect,["response_type"]="code",
                ["scope"]=requestDrive ? "openid email profile https://www.googleapis.com/auth/drive.file" : "openid email profile",["state"]=state,["nonce"]=nonce,
                ["code_challenge"]=challenge,["code_challenge_method"]="S256",["access_type"]="offline",["prompt"]="consent select_account"
            });
            Process.Start(new ProcessStartInfo(url) { UseShellExecute=true });
            string? code=null;
            while(code is null)
            {
                using var socket=await listener.AcceptTcpClientAsync(cancellation); await using var stream=socket.GetStream();
                using var reader=new StreamReader(stream,Encoding.ASCII,leaveOpen:true);
                var first=await reader.ReadLineAsync(cancellation) ?? "";
                var parts=first.Split(' '); var target=parts.Length>1 ? parts[1] : "/";
                string? line; do { line=await reader.ReadLineAsync(cancellation); } while(!string.IsNullOrEmpty(line));
                var callback=new Uri(new Uri(redirect),target); var query=System.Web.HttpUtility.ParseQueryString(callback.Query);
                var valid=parts.FirstOrDefault()=="GET" && callback.AbsolutePath=="/callback" && query["state"]==state;
                if(valid && query["error"] is null)
                    code=query["code"] ?? throw new InvalidOperationException("O Google não retornou o código de acesso.");
                var body=CallbackPage(valid,query["error"] is null);
                var bytes=Encoding.UTF8.GetBytes(body);
                var header=Encoding.ASCII.GetBytes($"HTTP/1.1 {(valid?200:400)} OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; img-src data:; base-uri 'none'; frame-ancestors 'none'\r\nReferrer-Policy: no-referrer\r\nCache-Control: no-store\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                try { await stream.WriteAsync(header,cancellation); await stream.WriteAsync(bytes,cancellation); }
                // Some endpoint protection closes the loopback browser connection immediately
                // after delivering the OAuth code. The validated code is already safe to use.
                catch(IOException) when(code is not null) { }
                if(!valid) continue;
                if(query["error"] is not null) throw new OperationCanceledException("Login cancelado.");
            }
            var form=new Dictionary<string,string> { ["client_id"]=configuration.GoogleClientId,["code"]=code,["code_verifier"]=verifier,["redirect_uri"]=redirect,["grant_type"]="authorization_code" };
            if(configuration.GoogleDesktopClientSecret.Length>0) form["client_secret"]=configuration.GoogleDesktopClientSecret;
            using var response=await _http.PostAsync("https://oauth2.googleapis.com/token",new FormUrlEncodedContent(form),cancellation);
            if(!response.IsSuccessStatusCode) throw new InvalidOperationException("O Google não concluiu o login. Confira o cliente OAuth Desktop configurado.");
            var json=await response.Content.ReadFromJsonAsync<JsonElement>(cancellation);
            if(requestDrive && (!json.TryGetProperty("scope",out var scope) || !(scope.GetString() ?? "").Split(' ').Contains("https://www.googleapis.com/auth/drive.file")))
                throw new InvalidOperationException("Autorize o acesso aos arquivos do ClipDesk no Drive para conectar a conta.");
            return new(json.GetProperty("access_token").GetString()!,json.TryGetProperty("refresh_token",out var refresh)?refresh.GetString()!:"",DateTimeOffset.UtcNow.AddSeconds(json.GetProperty("expires_in").GetInt32()),json.GetProperty("id_token").GetString()!);
        }
        finally { listener.Stop(); }
    }
    private static string CallbackPage(bool valid,bool success)
    {
        var title=valid && success ? "Tudo certo por aqui." : valid ? "Conexão cancelada" : "Solicitação não reconhecida";
        var message=valid && success ? "Sua conta foi conectada com segurança. Você já pode fechar esta guia e continuar no ClipDesk." : valid ? "Nenhuma alteração foi feita. Você pode fechar esta guia e tentar novamente no ClipDesk." : "Volte ao ClipDesk e inicie a conexão novamente.";
        var symbol=valid && success ? "✓" : "!";
        const string page="""
        <!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><link rel="icon" href="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 32 32'%3E%3Crect width='32' height='32' rx='9' fill='%237146eb'/%3E%3Cpath d='M10 16.5l4 4 8-9' fill='none' stroke='white' stroke-width='3' stroke-linecap='round'/%3E%3C/svg%3E"><title>__TITLE__ — ClipDesk</title><style>
        *{box-sizing:border-box}html,body{height:100%;margin:0}body{display:grid;place-items:center;padding:24px;color:#17131f;font-family:Inter,"Segoe UI",system-ui,sans-serif;background:radial-gradient(circle at 86% 5%,#e5d9ff 0,transparent 35%),#fcfbff}main{width:min(100%,560px);padding:44px;border:1px solid #ded2ff;border-radius:30px;background:#fffffff0;box-shadow:0 34px 90px #321f581f;text-align:center}.brand{margin-bottom:32px;font-size:19px;font-weight:800}.symbol{display:grid;width:76px;height:76px;margin:0 auto 24px;place-items:center;border-radius:50%;color:#fff;background:linear-gradient(145deg,#a77bff,#4f28bd);box-shadow:0 16px 36px #5a31c94d;font-size:38px;font-weight:700}h1{margin:0 0 12px;font-size:clamp(28px,6vw,40px);letter-spacing:-.04em}p{margin:0 auto;color:#5b5468;font-size:16px;line-height:1.65}.status{display:inline-block;margin-top:27px;padding:9px 13px;border:1px solid #e7e1f0;border-radius:999px;color:#4f28bd;background:#f8f5ff;font-size:13px;font-weight:750}@media(max-width:520px){main{padding:34px 24px;border-radius:24px}}
        </style></head><body><main><div class="brand">ClipDesk</div><div class="symbol">__SYMBOL__</div><h1>__TITLE__</h1><p>__MESSAGE__</p><div class="status">Volte para o aplicativo</div></main></body></html>
        """;
        return page.Replace("__TITLE__",title,StringComparison.Ordinal).Replace("__SYMBOL__",symbol,StringComparison.Ordinal).Replace("__MESSAGE__",message,StringComparison.Ordinal);
    }
    private static string Encode(Dictionary<string,string> fields)=>string.Join("&",fields.Select(p=>$"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
    public async Task<GoogleTokens> RefreshAsync(GoogleTokens tokens,CancellationToken cancellation)
    {
        if(tokens.Expires>DateTimeOffset.UtcNow.AddMinutes(1)) return tokens;
        if(tokens.RefreshToken.Length==0) throw new InvalidOperationException("Entre novamente com Google para renovar o acesso ao Drive.");
        var form=new Dictionary<string,string> { ["client_id"]=configuration.GoogleClientId,["refresh_token"]=tokens.RefreshToken,["grant_type"]="refresh_token" };
        if(configuration.GoogleDesktopClientSecret.Length>0) form["client_secret"]=configuration.GoogleDesktopClientSecret;
        using var response=await _http.PostAsync("https://oauth2.googleapis.com/token",new FormUrlEncodedContent(form),cancellation);
        if(!response.IsSuccessStatusCode) throw new InvalidOperationException("O acesso ao Drive expirou. Reconecte o Google.");
        var json=await response.Content.ReadFromJsonAsync<JsonElement>(cancellation);
        var updated=tokens with {AccessToken=json.GetProperty("access_token").GetString()!,Expires=DateTimeOffset.UtcNow.AddSeconds(json.GetProperty("expires_in").GetInt32()),IdToken=json.TryGetProperty("id_token",out var idToken)?idToken.GetString()!:tokens.IdToken};
        TokensRefreshed?.Invoke(updated); return updated;
    }
    public async Task<string> EnsureFolderAsync(string accessToken,string name,string key,string? parent,CancellationToken cancellation)
    {
        // appProperties are private to the OAuth app and avoid relying on a mutable folder name.
        using var query=new HttpRequestMessage(HttpMethod.Get,"https://www.googleapis.com/drive/v3/files?q="+Uri.EscapeDataString($"trashed = false and mimeType = 'application/vnd.google-apps.folder' and appProperties has {{ key='clipdeskFolder' and value='{key}' }}")+"&fields=files(id)");
        query.Headers.Authorization=new("Bearer",accessToken);
        using var found=await _http.SendAsync(query,cancellation); found.EnsureSuccessStatusCode();
        var existing=await found.Content.ReadFromJsonAsync<JsonElement>(cancellation);
        if(existing.GetProperty("files").EnumerateArray().FirstOrDefault() is var item && item.ValueKind==JsonValueKind.Object) return item.GetProperty("id").GetString()!;
        var metadata=new Dictionary<string,object> { ["name"]=name,["mimeType"]="application/vnd.google-apps.folder",["appProperties"]=new {clipdeskFolder=key} };
        if(parent is not null) metadata["parents"]=new[]{parent};
        using var create=new HttpRequestMessage(HttpMethod.Post,"https://www.googleapis.com/drive/v3/files?fields=id") { Content=JsonContent.Create(metadata) };
        create.Headers.Authorization=new("Bearer",accessToken);
        using var response=await _http.SendAsync(create,cancellation); response.EnsureSuccessStatusCode();
        var json=await response.Content.ReadFromJsonAsync<JsonElement>(cancellation); return json.GetProperty("id").GetString()!;
    }
    public async Task<string> UploadAsync(string accessToken,CloudAttachment attachment,string parent,LocalDatabase database,IProgress<double>? progress,CancellationToken cancellation)
    {
        if(attachment.LocalPath is null || !File.Exists(attachment.LocalPath)) throw new FileNotFoundException("Arquivo local não encontrado.");
        // A generated Drive ID is persisted before transfer. Repeated attempts update the same file.
        var key="drive-upload:"+attachment.Id;
        var saved=database.Read(key);
        var upload=saved is null ? null : CloudRules.Deserialize<DriveUpload>(saved);
        if(upload is null)
        {
            using var generate=new HttpRequestMessage(HttpMethod.Get,"https://www.googleapis.com/drive/v3/files/generateIds?count=1&space=drive&type=files"); generate.Headers.Authorization=new("Bearer",accessToken);
            using var generated=await _http.SendAsync(generate,cancellation); generated.EnsureSuccessStatusCode();
            var ids=await generated.Content.ReadFromJsonAsync<JsonElement>(cancellation);
            upload=new(ids.GetProperty("ids")[0].GetString()!,"",0);
            database.Write(key,CloudRules.Serialize(upload));
        }
        // Probe the stable ID first: the previous final response may have been lost.
        using(var probe=new HttpRequestMessage(HttpMethod.Get,$"https://www.googleapis.com/drive/v3/files/{upload.FileId}?fields=id,size,sha256Checksum"))
        {
            probe.Headers.Authorization=new("Bearer",accessToken); using var existing=await _http.SendAsync(probe,cancellation);
            if(existing.IsSuccessStatusCode) { var file=await existing.Content.ReadFromJsonAsync<JsonElement>(cancellation); if(file.TryGetProperty("size",out var size) && size.GetString()==attachment.Size.ToString() && file.TryGetProperty("sha256Checksum",out var checksum) && string.Equals(checksum.GetString(),attachment.Sha256,StringComparison.OrdinalIgnoreCase)) return upload.FileId; }
        }
        if(upload.Session.Length>0)
        {
            using var status=new HttpRequestMessage(HttpMethod.Put,ValidateSession(upload.Session)) { Content=new ByteArrayContent([]) };
            status.Headers.Authorization=new("Bearer",accessToken); status.Content.Headers.TryAddWithoutValidation("Content-Range",$"bytes */{attachment.Size}");
            using var result=await _http.SendAsync(status,cancellation);
            if(result.IsSuccessStatusCode) return upload.FileId;
            upload=result.StatusCode==(HttpStatusCode)308 ? upload with {Offset=NextOffset(result)} : upload with {Session="",Offset=0};
        }
        if(upload.Session.Length==0)
        {
            using var begin=new HttpRequestMessage(HttpMethod.Post,"https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&fields=id")
            { Content=JsonContent.Create(new { id=upload.FileId,name=attachment.Name,parents=new[]{parent},appProperties=new {clipdeskAttachment=attachment.Id} }) };
            begin.Headers.Authorization=new("Bearer",accessToken); begin.Headers.TryAddWithoutValidation("X-Upload-Content-Length",attachment.Size.ToString());
            begin.Headers.TryAddWithoutValidation("X-Upload-Content-Type","application/octet-stream");
            using var response=await _http.SendAsync(begin,cancellation); response.EnsureSuccessStatusCode();
            upload=upload with {Session=response.Headers.Location?.AbsoluteUri ?? throw new IOException("O Drive não iniciou a transferência."),Offset=0};
            ValidateSession(upload.Session); database.Write(key,CloudRules.Serialize(upload));
        }
        await using var source=File.OpenRead(attachment.LocalPath); source.Position=upload.Offset;
        var bytes=new byte[4*1024*1024];
        while(source.Position<source.Length)
        {
            var count=await source.ReadAsync(bytes,cancellation); if(count==0) break;
            using var request=new HttpRequestMessage(HttpMethod.Put,ValidateSession(upload.Session)) {Content=new ByteArrayContent(bytes,0,count)};
            request.Headers.Authorization=new("Bearer",accessToken); request.Content.Headers.ContentRange=new ContentRangeHeaderValue(upload.Offset,upload.Offset+count-1,attachment.Size);
            using var response=await _http.SendAsync(request,cancellation);
            if(response.IsSuccessStatusCode) { progress?.Report(1); return upload.FileId; }
            if(response.StatusCode!=(HttpStatusCode)308) response.EnsureSuccessStatusCode();
            var next=NextOffset(response); if(next<=upload.Offset) throw new IOException("O Drive não confirmou o bloco. Tente novamente.");
            upload=upload with {Offset=next}; database.Write(key,CloudRules.Serialize(upload)); source.Position=next; progress?.Report((double)next/attachment.Size);
        }
        throw new IOException("Transferência incompleta. Tente novamente para retomar.");
    }
    private static Uri ValidateSession(string session)
    {
        var uri=new Uri(session); if(uri.Scheme!="https" || uri.Host!="www.googleapis.com" || !uri.AbsolutePath.StartsWith("/upload/drive/",StringComparison.Ordinal)) throw new IOException("Destino de upload inválido."); return uri;
    }
    private static long NextOffset(HttpResponseMessage response)=>response.Headers.TryGetValues("Range",out var values) && long.TryParse(values.Single().Split('-').Last(),out var last) ? last+1 : 0;
    private sealed record DriveUpload(string FileId,string Session,long Offset);
    public async Task GrantAsync(string accessToken,DriveGrant grant,CancellationToken cancellation)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,$"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(grant.FileId)}/permissions?sendNotificationEmail=false")
        { Content=JsonContent.Create(new {type="user",role="reader",emailAddress=grant.Email}) };
        request.Headers.Authorization=new("Bearer",accessToken); using var response=await _http.SendAsync(request,cancellation); response.EnsureSuccessStatusCode();
    }
    public async Task GrantLinkAsync(string accessToken,string fileId,CancellationToken cancellation)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,$"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(fileId)}/permissions?sendNotificationEmail=false")
        { Content=JsonContent.Create(new {type="anyone",role="reader",allowFileDiscovery=false}) };
        request.Headers.Authorization=new("Bearer",accessToken); using var response=await _http.SendAsync(request,cancellation);
        if(response.StatusCode!=HttpStatusCode.Conflict) response.EnsureSuccessStatusCode();
    }
    public async Task DownloadAsync(string accessToken,string fileId,string destination,CancellationToken cancellation)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,$"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(fileId)}?alt=media"); request.Headers.Authorization=new("Bearer",accessToken);
        using var response=await _http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation); response.EnsureSuccessStatusCode();
        await using var output=File.Create(destination); await response.Content.CopyToAsync(output,cancellation);
    }
    public async Task DownloadPublicAsync(string fileId,string destination,CancellationToken cancellation)
    {
        var url=$"https://drive.usercontent.google.com/download?id={Uri.EscapeDataString(fileId)}&export=download&confirm=t";
        using var response=await _http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,cancellation); response.EnsureSuccessStatusCode();
        await using var output=File.Create(destination); await response.Content.CopyToAsync(output,cancellation);
    }
    public async Task DeleteAsync(string accessToken,string fileId,CancellationToken cancellation)
    {
        using var request=new HttpRequestMessage(HttpMethod.Delete,$"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(fileId)}");
        request.Headers.Authorization=new("Bearer",accessToken);
        using var response=await _http.SendAsync(request,cancellation);
        if(response.StatusCode!=HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
    }
    public async Task RevokeAsync(string accessToken,DriveGrant grant,CancellationToken cancellation)
    {
        string? page=null;
        do
        {
            using var list=new HttpRequestMessage(HttpMethod.Get,$"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(grant.FileId)}/permissions?fields=nextPageToken,permissions(id,emailAddress,type,role)&pageSize=100"+(page is null?"":"&pageToken="+Uri.EscapeDataString(page)));
            list.Headers.Authorization=new("Bearer",accessToken);using var response=await _http.SendAsync(list,cancellation);response.EnsureSuccessStatusCode();
            var json=await response.Content.ReadFromJsonAsync<JsonElement>(cancellation);
            foreach(var permission in json.GetProperty("permissions").EnumerateArray())
            {
                if(!permission.TryGetProperty("emailAddress",out var email) || !string.Equals(email.GetString(),grant.Email,StringComparison.OrdinalIgnoreCase) || permission.GetProperty("role").GetString()=="owner") continue;
                using var remove=new HttpRequestMessage(HttpMethod.Delete,$"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(grant.FileId)}/permissions/{Uri.EscapeDataString(permission.GetProperty("id").GetString()!)}");
                remove.Headers.Authorization=new("Bearer",accessToken);using var removed=await _http.SendAsync(remove,cancellation);removed.EnsureSuccessStatusCode();
            }
            page=json.TryGetProperty("nextPageToken",out var next)?next.GetString():null;
        }while(page is not null);
    }
}
