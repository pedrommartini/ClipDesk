using System.Net;
using System.Net.Http;

namespace ClipDesk.Services;

/// <summary>Share a server's Retry-After across requests without replaying mutations.</summary>
public sealed class RequestCooldownHandler : DelegatingHandler
{
    private readonly object _stateLock = new();
    private readonly Dictionary<string, (DateTimeOffset RetryAt, int Failures)> _servers = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _clock;

    public RequestCooldownHandler(HttpMessageHandler? inner = null, TimeProvider? clock = null) : base(inner ?? new HttpClientHandler()) { _clock = clock ?? TimeProvider.System; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var server = request.RequestUri!.GetLeftPart(UriPartial.Authority);
        lock (_stateLock)
            if (_servers.TryGetValue(server, out var pending) && _clock.GetUtcNow() < pending.RetryAt)
                throw new HttpRequestException("O serviço pediu uma pausa. Seus dados continuam salvos; tentaremos novamente em breve.", null, HttpStatusCode.TooManyRequests);

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        lock (_stateLock)
        {
            var state = _servers.GetValueOrDefault(server);
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            {
                var failures = Math.Min(state.Failures + 1, 6);
                var delay = response.Headers.RetryAfter?.Delta
                    ?? (response.Headers.RetryAfter?.Date is { } date ? date - _clock.GetUtcNow() : TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, failures))));
                if (delay < TimeSpan.FromSeconds(1)) delay = TimeSpan.FromSeconds(1);
                var next = _clock.GetUtcNow() + delay + TimeSpan.FromMilliseconds(Random.Shared.Next(100, 1000));
                _servers[server] = (next > state.RetryAt ? next : state.RetryAt, failures);
            }
            else if (response.IsSuccessStatusCode && _clock.GetUtcNow() >= state.RetryAt) _servers.Remove(server);
        }
        return response;
    }
}
