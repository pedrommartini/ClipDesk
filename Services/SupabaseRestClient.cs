using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClipDesk.Core;

namespace ClipDesk.Services;

/// <summary>
/// Small, typed REST boundary for the Supabase Data API. All requests carry a
/// user session plus the publishable project key; Row Level Security remains
/// the authorization authority in Postgres.
/// </summary>
public sealed class SupabaseRestClient : IDisposable
{
    private readonly SupabaseConfiguration _configuration;
    private readonly HttpClient _http;
    private string? _accessToken;

    public SupabaseRestClient(SupabaseConfiguration configuration, HttpMessageHandler? handler = null)
    {
        _configuration = configuration;
        var project = configuration.ProjectUri();
        _http = handler is null ? new HttpClient(new RequestCooldownHandler()) : new HttpClient(handler, false);
        _http.BaseAddress = new Uri(project, "rest/v1/");
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("apikey", configuration.PublishableKey);
    }

    public void SetSession(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) throw new ArgumentException("Sessão inválida.", nameof(accessToken));
        _accessToken = accessToken;
        _http.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);
    }

    public void ClearSession()
    {
        _accessToken = null;
        _http.DefaultRequestHeaders.Authorization = null;
    }

    public async Task<List<T>> GetAsync<T>(string path, CancellationToken cancellation)
    {
        EnsureSession();
        using var response = await _http.GetAsync(path, cancellation);
        await EnsureAsync(response);
        return await response.Content.ReadFromJsonAsync<List<T>>(CloudRules.Json, cancellation) ?? [];
    }

    public async Task<List<T>> PostAsync<T>(string path, object body, CancellationToken cancellation, bool returnRows = true)
    {
        EnsureSession();
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, options: CloudRules.Json) };
        if (returnRows) request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        using var response = await _http.SendAsync(request, cancellation);
        await EnsureAsync(response);
        return returnRows ? await response.Content.ReadFromJsonAsync<List<T>>(CloudRules.Json, cancellation) ?? [] : [];
    }

    public async Task<List<T>> PatchAsync<T>(string path, object body, CancellationToken cancellation)
    {
        EnsureSession();
        using var request = new HttpRequestMessage(HttpMethod.Patch, path) { Content = JsonContent.Create(body, options: CloudRules.Json) };
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        using var response = await _http.SendAsync(request, cancellation);
        await EnsureAsync(response);
        return await response.Content.ReadFromJsonAsync<List<T>>(CloudRules.Json, cancellation) ?? [];
    }

    public async Task UpsertAsync(string path, object body, CancellationToken cancellation)
    {
        EnsureSession();
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, options: CloudRules.Json) };
        request.Headers.TryAddWithoutValidation("Prefer", "resolution=merge-duplicates,return=minimal");
        using var response = await _http.SendAsync(request, cancellation);
        await EnsureAsync(response);
    }

    public async Task<List<T>> UpsertAsync<T>(string path, object body, CancellationToken cancellation)
    {
        EnsureSession();
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, options: CloudRules.Json) };
        request.Headers.TryAddWithoutValidation("Prefer", "resolution=merge-duplicates,return=representation");
        using var response = await _http.SendAsync(request, cancellation);
        await EnsureAsync(response);
        return await response.Content.ReadFromJsonAsync<List<T>>(CloudRules.Json, cancellation) ?? [];
    }

    public async Task DeleteAsync(string path, CancellationToken cancellation)
    {
        EnsureSession();
        using var response = await _http.DeleteAsync(path, cancellation);
        await EnsureAsync(response);
    }

    public async Task<T?> RpcAsync<T>(string function, object parameters, CancellationToken cancellation)
    {
        EnsureSession();
        using var request = new HttpRequestMessage(HttpMethod.Post, "rpc/" + Uri.EscapeDataString(function)) { Content = JsonContent.Create(parameters, options: CloudRules.Json) };
        using var response = await _http.SendAsync(request, cancellation);
        await EnsureAsync(response);
        return await response.Content.ReadFromJsonAsync<T>(CloudRules.Json, cancellation);
    }

    private void EnsureSession()
    {
        if (string.IsNullOrWhiteSpace(_accessToken)) throw new InvalidOperationException("Entre com Google para usar a nuvem.");
    }

    private static async Task EnsureAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retry = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(15);
            throw new HttpRequestException("A nuvem limitou temporariamente as requisições.", null, response.StatusCode) { Data = { ["RetryAfter"] = retry } };
        }
        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.Conflict)
        {
            try
            {
                var json = JsonNode.Parse(body)?.AsObject();
                throw new InvalidOperationException(json?["message"]?.GetValue<string>() ?? json?["hint"]?.GetValue<string>() ?? "Operação não autorizada.");
            }
            catch (JsonException) { throw new InvalidOperationException("Operação não autorizada."); }
        }
        response.EnsureSuccessStatusCode();
    }

    public void Dispose() => _http.Dispose();
}
