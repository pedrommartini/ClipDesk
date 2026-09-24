using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ClipDesk.Services;

/// <summary>Publishes a package to a configured ClipDesk plugin-store server.</summary>
public sealed class PluginPublishingService
{
    private const long MaximumZipBytes = 25L * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Uri _storeUri;
    private readonly HttpClient _http;

    public PluginPublishingService(HttpClient? http = null, Uri? storeUri = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        _storeUri = storeUri ?? ResolveStoreUri()
            ?? throw new InvalidOperationException("A publicação da loja ainda não está configurada.");
    }

    public static Uri? ResolveStoreUri()
    {
        var configured = Environment.GetEnvironmentVariable("CLIPDESK_PLUGIN_STORE_URL");
        if (Uri.TryCreate(configured, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || AppEnvironment.IsDevelopment && uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp))
            return uri;
#if CLIPDESK_DEV
        return new Uri("http://127.0.0.1:5278/");
#else
        return null;
#endif
    }

    public async Task<PluginPublishResult> PublishAsync(string archivePath, string username, string deployPassword,
        CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(archivePath) || !File.Exists(archivePath)
            || !archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Selecione um pacote de plugin .zip válido.");
        if (string.IsNullOrWhiteSpace(username)) throw new InvalidOperationException("Entre e defina seu username antes de publicar.");
        if (string.IsNullOrWhiteSpace(deployPassword)) throw new InvalidOperationException("Informe a senha de publicação.");
        if (new FileInfo(archivePath).Length > MaximumZipBytes) throw new InvalidDataException("Pacote de plugin muito grande.");

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(username.Trim().TrimStart('@')), "publisher");
        form.Add(new StringContent(deployPassword), "deployPassword");
        await using var package = File.OpenRead(archivePath);
        using var content = new StreamContent(package);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(content, "package", Path.GetFileName(archivePath));
        using var response = await _http.PostAsync(new Uri(_storeUri, "api/plugin-store/deploy"), form, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            try { throw new InvalidOperationException(JsonSerializer.Deserialize<PluginPublishError>(body, Json)?.Message ?? "Não foi possível publicar o plugin."); }
            catch (JsonException) { throw new InvalidOperationException("Não foi possível publicar o plugin."); }
        }
        return JsonSerializer.Deserialize<PluginPublishResult>(body, Json)
            ?? throw new InvalidDataException("A loja não confirmou a publicação.");
    }

    private sealed class PluginPublishError { public string? Message { get; init; } }
}

public sealed class PluginPublishResult
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public string Publisher { get; init; } = "";
}
