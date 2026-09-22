using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.RateLimiting;
using ClipDesk.Core;
using ClipDesk.Server;
using Google.Apis.Auth;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;

var builder=WebApplication.CreateBuilder(args);
if(builder.Environment.IsDevelopment()) builder.Configuration.AddJsonFile("appsettings.Development.local.json",optional:true,reloadOnChange:false);
builder.Services.AddSingleton<CloudStore>();
builder.Services.AddSingleton<LiveConnections>();
builder.Services.AddSignalR(o=>o.MaximumReceiveMessageSize=16_384);
builder.Services.AddHttpClient();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode=429;
    options.OnRejected=async (context,cancellation)=>
    {
        var seconds=context.Lease.TryGetMetadata(MetadataName.RetryAfter,out var retry) ? Math.Max(1,(int)Math.Ceiling(retry.TotalSeconds)) : 60;
        context.HttpContext.Response.Headers.RetryAfter=seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await context.HttpContext.Response.WriteAsJsonAsync(new {message="Limite temporário de requisições. Aguarde antes de tentar novamente."},cancellation);
    };
    options.GlobalLimiter=PartitionedRateLimiter.Create<HttpContext,string>(context=>
        RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId ? "user:"+userId : "ip:"+(context.Connection.RemoteIpAddress?.ToString() ?? "local"),_=>new FixedWindowRateLimiterOptions { PermitLimit=600,Window=TimeSpan.FromMinutes(1),QueueLimit=0 }));
});
builder.WebHost.ConfigureKestrel(o=>o.Limits.MaxRequestBodySize=CloudRules.DatabaseFileLimit+4096);
var app=builder.Build();
var store=app.Services.GetRequiredService<CloudStore>();
var challenges=new ConcurrentDictionary<string,DateTimeOffset>();
app.Use(async (context,next)=>
{
    try
    {
        if(!app.Environment.IsDevelopment() && !context.Request.IsHttps) { context.Response.StatusCode=400; return; }
        var path=context.Request.Path;
        if(path.StartsWithSegments("/api") || path.StartsWithSegments("/live"))
        {
            var bearer=context.Request.Headers.Authorization.ToString();
            var token=bearer.StartsWith("Bearer ",StringComparison.Ordinal) ? bearer[7..] : "";
            if(path.StartsWithSegments("/live") && token.Length==0) token=context.Request.Query["access_token"].ToString();
            var user=store.Authenticate(token);
            if(user is null) context.Items["unauthorized"]=true;
            else
            {
                context.Items["user"]=user;
                context.User=new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier,user.Id),new(ClaimTypes.Name,user.Username ?? user.Name),new("sid",CloudStore.Hash(token))],"ClipDesk"));
            }
        }
        await next(context);
    }
    catch(UnauthorizedAccessException) { context.Response.StatusCode=403; await context.Response.WriteAsJsonAsync(new {message="Você não tem acesso a este conteúdo."}); }
    catch(SyncConflictException e) { context.Response.StatusCode=409; await context.Response.WriteAsJsonAsync(new {message=e.Message}); }
    catch(ArgumentException e) { context.Response.StatusCode=400; await context.Response.WriteAsJsonAsync(new {message=e.Message}); }
    catch(Exception e)
    {
        app.Logger.LogError("Cloud request failed: {Type}",e.GetType().Name);
        if(!context.Response.HasStarted) { context.Response.StatusCode=500; await context.Response.WriteAsJsonAsync(new {message="Não foi possível concluir. Tente novamente."}); }
    }
});
app.UseRateLimiter();
app.Use(async (context,next)=>
{
    if(context.Items.ContainsKey("unauthorized")) { context.Response.StatusCode=401; return; }
    await next(context);
});
CloudUser User(HttpContext c)=>(CloudUser)c.Items["user"]!;
async Task Notify(string userId,string? workspace)
{
    var hub=app.Services.GetRequiredService<IHubContext<SyncHub>>();
    var ids=workspace is not null && store.CanAccess(userId,workspace) ? store.Members(userId,workspace).Select(m=>m.UserId).ToArray() : [userId];
    await hub.Clients.Clients(app.Services.GetRequiredService<LiveConnections>().ActiveFor(ids,store)).SendAsync("Changed");
}
app.MapGet("/health",()=>new {application="ClipDesk",status="ok",environment=app.Environment.EnvironmentName,googleConfigured=!string.IsNullOrWhiteSpace(app.Configuration["Google:ClientId"])});
app.MapGet("/config",()=>new {googleClientId=app.Configuration["Google:ClientId"] ?? ""});
app.MapPost("/auth/challenge",()=>
{
    foreach(var expired in challenges.Where(p=>p.Value<DateTimeOffset.UtcNow)) challenges.TryRemove(expired.Key,out _);
    if(challenges.Count>1000) return Results.StatusCode(429);
    var nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); challenges[nonce]=DateTimeOffset.UtcNow.AddMinutes(5);
    return Results.Ok(new LoginChallenge(nonce));
});
app.MapPost("/auth/google",async (GoogleLogin login)=>
{
    if(!challenges.TryRemove(login.Nonce,out var expires) || expires<DateTimeOffset.UtcNow) return Results.Unauthorized();
    var clientId=app.Configuration["Google:ClientId"];
    if(string.IsNullOrWhiteSpace(clientId)) return Results.Problem("Configure o cliente OAuth Google no servidor.",statusCode:503);
    GoogleJsonWebSignature.Payload payload;
    try { payload=await GoogleJsonWebSignature.ValidateAsync(login.IdToken,new GoogleJsonWebSignature.ValidationSettings { Audience=[clientId] }); }
    catch(InvalidJwtException) { return Results.Unauthorized(); }
    var encoded=login.IdToken.Split('.')[1].Replace('-','+').Replace('_','/');
    using var json=JsonDocument.Parse(Convert.FromBase64String(encoded.PadRight((encoded.Length+3)/4*4,'=')));
    if(!json.RootElement.TryGetProperty("nonce",out var nonce) || nonce.GetString()!=login.Nonce || !payload.EmailVerified) return Results.Unauthorized();
    return Results.Ok(store.Login(payload.Subject,payload.Email,payload.Name ?? "Usuário",payload.Picture));
});
app.MapGet("/api/me",(HttpContext c)=>User(c));
app.MapPost("/api/username",(HttpContext c,UsernameRequest request)=>store.SetUsername(User(c).Id,request.Username));
app.MapPost("/api/logout",(HttpContext c)=> {var token=c.Request.Headers.Authorization.ToString()[7..];store.Logout(token);app.Services.GetRequiredService<LiveConnections>().Revoke(CloudStore.Hash(token));return Results.NoContent();});
app.MapGet("/api/entities",(HttpContext c)=>store.GetEntities(User(c).Id));
app.MapPost("/api/sync",async (HttpContext c,SyncOperation operation)=>
{
    var previousMembers=operation.Kind=="workspace" && operation.Deleted && store.CanAccess(User(c).Id,operation.EntityId)
        ? store.Members(User(c).Id,operation.EntityId).Select(m=>m.UserId).ToArray() : null;
    var result=store.Apply(User(c).Id,operation);
    if(previousMembers is not null)
        await app.Services.GetRequiredService<IHubContext<SyncHub>>().Clients.Clients(app.Services.GetRequiredService<LiveConnections>().ActiveFor(previousMembers,store)).SendAsync("Changed");
    else if(operation.Kind=="boardObject")
    {
        var workspace=operation.WorkspaceId!;
        var memberIds=store.Members(User(c).Id,workspace).Select(member=>member.UserId).ToArray();
        await app.Services.GetRequiredService<IHubContext<SyncHub>>().Clients
            .Clients(app.Services.GetRequiredService<LiveConnections>().ActiveFor(memberIds,store))
            .SendAsync("EntityChanged",result);
    }
    else await Notify(User(c).Id,operation.Kind=="workspace" ? operation.EntityId : operation.WorkspaceId);
    return new SyncResult(result);
});
app.MapGet("/api/workspaces/{id}/members",(HttpContext c,string id)=>store.Members(User(c).Id,id));
app.MapGet("/api/workspaces/{id}/private-readiness",(HttpContext c,string id)=> {store.CheckPrivateTransition(User(c).Id,id);return Results.NoContent();});
app.MapPost("/api/workspaces/{id}/personal",async (HttpContext c,string id)=>
{
    var members=store.Members(User(c).Id,id);store.MakePersonal(User(c).Id,id);
    await app.Services.GetRequiredService<IHubContext<SyncHub>>().Clients.Clients(app.Services.GetRequiredService<LiveConnections>().ActiveFor(members.Select(m=>m.UserId),store)).SendAsync("Changed");return Results.NoContent();
});
app.MapPost("/api/workspaces/{id}/invites",async (HttpContext c,string id,InviteRequest invite)=>
{
    store.Invite(User(c).Id,id,invite.Username);
    // The invitation is delivered in the app only; no emails or public links.
    return Results.NoContent();
});
app.MapGet("/api/workspaces/{id}/members/{memberId}/drive-grants",(HttpContext c,string id,string memberId)=>store.MemberDriveGrants(User(c).Id,id,memberId));
app.MapDelete("/api/workspaces/{id}/members/{memberId}",async (HttpContext c,string id,string memberId)=>
{
    var members=store.Members(User(c).Id,id).Select(m=>m.UserId).ToArray();
    store.RemoveMember(User(c).Id,id,memberId);
    await app.Services.GetRequiredService<IHubContext<SyncHub>>().Clients.Clients(app.Services.GetRequiredService<LiveConnections>().ActiveFor(members,store)).SendAsync("Changed");
    return Results.NoContent();
});
app.MapGet("/api/invites",(HttpContext c)=>store.Invitations(User(c).Id));
app.MapPost("/api/invites/{id}/accept",async (HttpContext c,string id)=>
{
    var workspace=store.AcceptInvitation(User(c).Id,id); await Notify(User(c).Id,workspace); return Results.NoContent();
});
app.MapPost("/api/invites/{id}/decline",(HttpContext c,string id)=> {store.DeclineInvitation(User(c).Id,id);return Results.NoContent();});
app.MapPost("/api/attachments",(HttpContext c,AttachmentRegistration a)=> { store.RegisterAttachment(User(c).Id,a); return Results.NoContent(); });
app.MapGet("/api/attachments/{id}",(HttpContext c,string id)=>store.Attachment(User(c).Id,id));
app.MapPut("/api/attachments/{id}/content",async (HttpContext c,string id)=>
{
    var a=store.Attachment(User(c).Id,id,true);
    if(Convert.ToInt64(a["size"])>CloudRules.DatabaseFileLimit || c.Request.ContentLength>CloudRules.DatabaseFileLimit) return Results.StatusCode(413);
    using var memory=new MemoryStream(); var buffer=new byte[81920];
    while(true) { var read=await c.Request.Body.ReadAsync(buffer,c.RequestAborted); if(read==0) break; if(memory.Length+read>CloudRules.DatabaseFileLimit) return Results.StatusCode(413); await memory.WriteAsync(buffer.AsMemory(0,read),c.RequestAborted); }
    store.StoreBytes(User(c).Id,id,memory.ToArray()); await Notify(User(c).Id,a["workspace"] as string); return Results.NoContent();
});
app.MapGet("/api/attachments/{id}/content",(HttpContext c,string id)=>
{
    var a=store.Attachment(User(c).Id,id); return Results.File(store.Download(User(c).Id,id),"application/octet-stream",(string)a["name"]!);
});
app.MapPost("/api/attachments/{id}/drive",async (HttpContext c,string id,DriveCompletion completion,IHttpClientFactory factory)=>
{
    var a=store.Attachment(User(c).Id,id,true); var token=c.Request.Headers["X-Google-Access-Token"].ToString();
    if(token.Length==0 || !System.Text.RegularExpressions.Regex.IsMatch(completion.FileId,"^[a-zA-Z0-9_-]{1,200}$")) return Results.BadRequest();
    using var http=factory.CreateClient(); http.DefaultRequestHeaders.Authorization=new("Bearer",token);
    using var identity=await http.GetAsync("https://openidconnect.googleapis.com/v1/userinfo",c.RequestAborted);
    if(!identity.IsSuccessStatusCode) return Results.Unauthorized();
    var userinfo=await identity.Content.ReadFromJsonAsync<JsonElement>(c.RequestAborted);
    if(userinfo.GetProperty("sub").GetString()!=store.GoogleSubject(User(c).Id)) return Results.StatusCode(403);
    using var response=await http.GetAsync($"https://www.googleapis.com/drive/v3/files/{completion.FileId}?fields=id,size,sha256Checksum,ownedByMe,appProperties,trashed",c.RequestAborted);
    if(!response.IsSuccessStatusCode) return Results.BadRequest();
    var metadata=await response.Content.ReadFromJsonAsync<JsonElement>(c.RequestAborted);
    if(!metadata.GetProperty("ownedByMe").GetBoolean() || metadata.GetProperty("trashed").GetBoolean()
        || metadata.GetProperty("size").GetString()!=a["size"]!.ToString()
        || !metadata.TryGetProperty("sha256Checksum",out var checksum) || !string.Equals(checksum.GetString(),(string)a["sha256"]!,StringComparison.OrdinalIgnoreCase)
        || !metadata.TryGetProperty("appProperties",out var properties) || !properties.TryGetProperty("clipdeskAttachment",out var aid) || aid.GetString()!=id) return Results.BadRequest();
    store.CompleteDrive(User(c).Id,id,completion.FileId); await Notify(User(c).Id,a["workspace"] as string); return Results.NoContent();
});
app.MapGet("/api/drive/grants",(HttpContext c)=>store.DriveGrants(User(c).Id));
app.MapHub<SyncHub>("/live");
app.Run();
public partial class Program { }
