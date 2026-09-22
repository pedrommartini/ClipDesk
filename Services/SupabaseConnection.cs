using System.IO;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using ClipDesk.Core;

namespace ClipDesk.Services;

/// <summary>
/// Public connection settings for the ClipDesk Supabase project. The publishable
/// key identifies the project but cannot bypass Row Level Security; privileged
/// service-role credentials are deliberately never present in the desktop app.
/// </summary>
public sealed class SupabaseConfiguration
{
    private const string ProductionUrl = "https://bnrwsndtxaizhmxsgknu.supabase.co";
    private const string ProductionPublishableKey = "sb_publishable_92fm1ztj9uZWBvVgnoD7mw_R8ws4zFU";
    // supabase-csharp 1.1.x / Realtime 8.x still authenticates the websocket
    // handshake with the project's JWT anon key. The newer sb_publishable key
    // remains the least-privileged key for REST, while this public anon key is
    // used only by the SDK client that owns Auth and Realtime. RLS applies to
    // both keys and no service-role credential is shipped in the desktop app.
    private const string ProductionRealtimeAnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImJucndzbmR0eGFpemhteHNna251Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODk0OTY0MjEsImV4cCI6MjEwNTA3MjQyMX0.7tpiEiLA26nFdmj-oxZwfuTcEvK_ILqd4m1TgcNTArs";

    // The same isolated cloud project is deliberately used by DEV for live
    // compatibility checks. Production only accepts its HTTPS Supabase origin.
    public string Url { get; set; } = ProductionUrl;
    public string PublishableKey { get; set; } = ProductionPublishableKey;
    public static string FilePath => Path.Combine(AppEnvironment.DataRoot, "supabase-config.local.json");

    public static SupabaseConfiguration Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var saved = CloudRules.Deserialize<SupabaseConfiguration>(File.ReadAllText(FilePath));
                if (!string.IsNullOrWhiteSpace(saved.Url) && !string.IsNullOrWhiteSpace(saved.PublishableKey)) return saved;
            }
        }
        catch (IOException) { }
        catch (System.Text.Json.JsonException) { }
        return new SupabaseConfiguration();
    }

    public Uri ProjectUri()
    {
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri)
            || uri.Scheme != "https"
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || !uri.Host.EndsWith(".supabase.co", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(PublishableKey)
            || !PublishableKey.StartsWith("sb_publishable_", StringComparison.Ordinal))
            throw new InvalidOperationException("A conexão segura com a nuvem ainda não está configurada.");
        return new Uri(uri.AbsoluteUri.TrimEnd('/'));
    }

    internal string RealtimeAnonKey() => ProductionRealtimeAnonKey;
}

public sealed record SupabaseCloudSession(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt,
    string UserId, string Email, string DisplayName, string? PictureUrl,
    string? ProviderAccessToken = null, string? ProviderRefreshToken = null, DateTimeOffset? ProviderExpiresAt = null);

public static class SecureSupabaseSessionFile
{
    private static string PathName => Path.Combine(AppEnvironment.DataRoot, "supabase-account.protected");

    public static SupabaseCloudSession? Load()
    {
        try
        {
            if (!File.Exists(PathName)) return null;
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(PathName), null, DataProtectionScope.CurrentUser);
            return CloudRules.Deserialize<SupabaseCloudSession>(Encoding.UTF8.GetString(bytes));
        }
        catch (IOException) { return null; }
        catch (CryptographicException) { return null; }
        catch (System.Text.Json.JsonException) { return null; }
    }

    public static void Save(SupabaseCloudSession session)
    {
        Directory.CreateDirectory(AppEnvironment.DataRoot);
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(CloudRules.Serialize(session)), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(PathName + ".tmp", bytes);
        File.Move(PathName + ".tmp", PathName, true);
    }

    public static void Clear()
    {
        if (File.Exists(PathName)) File.Delete(PathName);
    }
}

/// <summary>Owns the Supabase SDK client and converts a Google native sign-in into a Supabase session.</summary>
public sealed class SupabaseConnection : IAsyncDisposable
{
    private readonly SupabaseConfiguration _configuration;
    private Supabase.Client? _client;

    public SupabaseConnection(SupabaseConfiguration? configuration = null)
    {
        _configuration = configuration ?? SupabaseConfiguration.Load();
    }

    public bool Configured
    {
        get
        {
            try { _ = _configuration.ProjectUri(); return true; }
            catch (InvalidOperationException) { return false; }
        }
    }

    public async Task<Supabase.Client> ClientAsync()
    {
        if (_client is not null) return _client;
        var uri = _configuration.ProjectUri();
        _client = new Supabase.Client(uri.AbsoluteUri, _configuration.RealtimeAnonKey(), new Supabase.SupabaseOptions
        {
            AutoConnectRealtime = true,
            // The desktop service refreshes under one lock and persists every
            // rotated refresh token. SDK background refresh left the durable
            // session with an already-spent token on the next launch.
            AutoRefreshToken = false
        });
        await _client.InitializeAsync();
        return _client;
    }

    /// <summary>
    /// Uses Supabase as the OAuth confidential client. The desktop app never
    /// receives or ships the Google OAuth client secret.
    /// </summary>
    public async Task<SupabaseCloudSession> SignInWithGoogleAsync(CancellationToken cancellation, bool requestDrive = false)
    {
        var client = await ClientAsync();
        const string redirect = "http://127.0.0.1:48173/callback";
        var listener = new TcpListener(IPAddress.Loopback, 48173);
        try
        {
            listener.Start();
        }
        catch (SocketException e)
        {
            throw new InvalidOperationException("Não foi possível abrir o retorno seguro do login. Feche outra janela do ClipDesk e tente novamente.", e);
        }
        try
        {
            var auth = await client.Auth.SignIn(Supabase.Gotrue.Constants.Provider.Google, new Supabase.Gotrue.SignInOptions
            {
                FlowType = Supabase.Gotrue.Constants.OAuthFlowType.PKCE,
                RedirectTo = redirect,
                Scopes = requestDrive
                    ? "openid email profile https://www.googleapis.com/auth/drive.file"
                    : "openid email profile",
                QueryParams = requestDrive ? new Dictionary<string, string>
                {
                    ["access_type"] = "offline",
                    ["prompt"] = "consent select_account"
                } : null
            });
            Process.Start(new ProcessStartInfo(auth.Uri.AbsoluteUri) { UseShellExecute = true });
            string? code = null;
            while (code is null)
            {
                using var socket = await listener.AcceptTcpClientAsync(cancellation);
                await using var stream = socket.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var first = await reader.ReadLineAsync(cancellation) ?? "";
                var parts = first.Split(' '); var target = parts.Length > 1 ? parts[1] : "/";
                string? line; do { line = await reader.ReadLineAsync(cancellation); } while (!string.IsNullOrEmpty(line));
                var callback = new Uri(new Uri(redirect), target);
                var query = HttpUtility.ParseQueryString(callback.Query);
                // The authorization code is bound to the PKCE verifier created
                // above, so a separate browser-visible state value is not needed.
                var valid = parts.FirstOrDefault() == "GET" && callback.AbsolutePath == "/callback";
                if (valid && query["error"] is null) code = query["code"] ?? throw new InvalidOperationException("A nuvem não retornou o código de acesso.");
                var providerError = query["error"];
                var success = valid && providerError is null;
                var page = success
                    ? "<!doctype html><meta charset=utf-8><title>ClipDesk</title><style>body{margin:0;display:grid;place-items:center;min-height:100vh;background:#0e1625;color:#eef2ff;font:600 18px Segoe UI,sans-serif}main{padding:36px;border:1px solid #394964;border-radius:22px;background:#172235;text-align:center}b{color:#b99aff}</style><main><b>✓ ClipDesk conectado</b><p>Você já pode fechar esta guia e voltar ao aplicativo.</p></main>"
                    : "<!doctype html><meta charset=utf-8><title>ClipDesk</title><style>body{margin:0;display:grid;place-items:center;min-height:100vh;background:#0e1625;color:#eef2ff;font:600 18px Segoe UI,sans-serif}main{padding:36px;border:1px solid #394964;border-radius:22px;background:#172235;text-align:center}b{color:#fb7185}</style><main><b>Não foi possível concluir o login</b><p>Volte ao ClipDesk para ver a orientação e tente novamente.</p></main>";
                var bytes = Encoding.UTF8.GetBytes(page);
                var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {(success ? 200 : 400)} OK\r\nContent-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                try { await stream.WriteAsync(header, cancellation); await stream.WriteAsync(bytes, cancellation); }
                catch (IOException) when (code is not null) { }
                if (!valid) continue;
                if (providerError is not null)
                {
                    if (string.Equals(providerError, "access_denied", StringComparison.OrdinalIgnoreCase))
                        throw new OperationCanceledException("Login cancelado.");
                    throw new InvalidOperationException("O Google não concluiu a autenticação com a nuvem. Tente entrar novamente.");
                }
            }
            var verifier = auth.PKCEVerifier ?? throw new InvalidOperationException("A nuvem não preparou a verificação segura do login.");
            var session = await client.Auth.ExchangeCodeForSession(verifier, code!);
            return SaveSession(session);
        }
        finally { listener.Stop(); }
    }

    private static SupabaseCloudSession SaveSession(Supabase.Gotrue.Session? session, SupabaseCloudSession? previous = null)
    {
        var user = session?.User ?? throw new InvalidOperationException("A nuvem não concluiu a autenticação.");
        var accessToken = session.AccessToken ?? throw new InvalidOperationException("A nuvem não retornou a sessão de acesso.");
        var result = new SupabaseCloudSession(
            accessToken,
            string.IsNullOrWhiteSpace(session.RefreshToken) ? previous?.RefreshToken ?? "" : session.RefreshToken,
            previous is not null && previous.AccessToken == accessToken
                ? previous.ExpiresAt
                : DateTimeOffset.UtcNow.AddSeconds(session.ExpiresIn),
            user.Id ?? throw new InvalidOperationException("A nuvem não retornou a conta."),
            user.Email ?? "",
            user.UserMetadata? ["full_name"]?.ToString() ?? user.UserMetadata?["name"]?.ToString() ?? user.Email ?? "Usuário",
            user.UserMetadata?["avatar_url"]?.ToString() ?? user.UserMetadata?["picture"]?.ToString(),
            string.IsNullOrWhiteSpace(session.ProviderToken) ? previous?.ProviderAccessToken : session.ProviderToken,
            string.IsNullOrWhiteSpace(session.ProviderRefreshToken) ? previous?.ProviderRefreshToken : session.ProviderRefreshToken,
            string.IsNullOrWhiteSpace(session.ProviderToken) ? previous?.ProviderExpiresAt : DateTimeOffset.UtcNow.AddSeconds(session.ExpiresIn));
        SecureSupabaseSessionFile.Save(result);
        return result;
    }

    /// <summary>
    /// Restores the durable session and returns the SDK's current session.
    /// SetSession refreshes expired access tokens, so callers must replace
    /// their cached REST bearer token with this returned value.
    /// </summary>
    public async Task<SupabaseCloudSession> RestoreAsync(SupabaseCloudSession session)
    {
        var client = await ClientAsync();
        await client.Auth.SetSession(session.AccessToken, session.RefreshToken);
        return SaveSession(client.Auth.CurrentSession, session);
    }

    public async Task<SupabaseCloudSession> RefreshSessionAsync(SupabaseCloudSession session)
    {
        var client = await ClientAsync();
        // The SDK may already have refreshed the token in the background. Do
        // not spend the single-use refresh token a second time in that case.
        if (client.Auth.CurrentSession?.AccessToken is { Length: > 0 } access
            && !string.Equals(access, session.AccessToken, StringComparison.Ordinal))
            return SaveSession(client.Auth.CurrentSession, session);

        await client.Auth.SetSession(session.AccessToken, session.RefreshToken, forceAccessTokenRefresh: true);
        return SaveSession(client.Auth.CurrentSession, session);
    }

    public async Task SignOutAsync()
    {
        if (_client is not null) await _client.Auth.SignOut();
        SecureSupabaseSessionFile.Clear();
    }

    public ValueTask DisposeAsync()
    {
        // Closing ClipDesk is not the same as explicitly signing out.  The
        // refresh token is kept in the DPAPI-protected session file so the
        // next launch can restore the account without another browser flow.
        return ValueTask.CompletedTask;
    }
}
