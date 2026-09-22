using System.Net;
using System.Net.Http;
using System.IO;
using ClipDesk.PluginSdk;

namespace ClipDesk.Services;

/// <summary>Host-owned HTTP boundary with an explicit per-plugin allowlist.</summary>
public sealed class PluginNetworkClient : IPluginNetworkClient
{
    private const int MaximumResponseBytes = 1_048_576;
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };
    private readonly HashSet<string> _allowedHosts;

    public PluginNetworkClient(IEnumerable<string> allowedHosts) =>
        _allowedHosts = new HashSet<string>(allowedHosts, StringComparer.OrdinalIgnoreCase);

    public async Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !_allowedHosts.Contains(uri.IdnHost))
            throw new UnauthorizedAccessException("O endereço não está autorizado pelo manifesto do plugin.");
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if ((int)response.StatusCode is >= 300 and < 400)
            throw new HttpRequestException("Redirecionamentos não são permitidos para plugins.", null, response.StatusCode);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            throw new InvalidDataException("A resposta excede o limite permitido.");
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var limited = new MemoryStream();
        var buffer = new byte[81920]; var total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken); if (read == 0) break;
            total += read; if (total > MaximumResponseBytes) throw new InvalidDataException("A resposta excede o limite permitido.");
            await limited.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return response.Content.Headers.ContentType?.CharSet is { Length: > 0 } charset
            ? System.Text.Encoding.GetEncoding(charset).GetString(limited.ToArray())
            : System.Text.Encoding.UTF8.GetString(limited.ToArray());
    }
}
