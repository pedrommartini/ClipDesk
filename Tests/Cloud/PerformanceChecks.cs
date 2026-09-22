using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using ClipDesk.Services;

internal static class PerformanceChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        var clock = new TestClock();
        var transport = new Transport();
        using var client = new HttpClient(new RequestCooldownHandler(transport, clock));
        using var limited = await client.PostAsync("https://limited.test/write", null);
        check(limited.StatusCode == HttpStatusCode.TooManyRequests && transport.Calls == 1, "429 does not replay a mutation");
        var blocked = false;
        try { using var ignored = await client.GetAsync("https://limited.test/read"); }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests) { blocked = true; }
        check(blocked && transport.Calls == 1, "Retry-After suppresses further requests to the same service");
        using var other = await client.GetAsync("https://other.test/read");
        check(other.IsSuccessStatusCode && transport.Calls == 2, "A rate-limited translator does not block a different service");
        clock.Now += TimeSpan.FromSeconds(59);
        try { using var ignored = await client.GetAsync("https://limited.test/read"); } catch (HttpRequestException) { }
        check(transport.Calls == 2, "Retry-After is honored throughout the requested interval");
        clock.Now += TimeSpan.FromSeconds(3);
        using var resumed = await client.GetAsync("https://limited.test/read");
        check(resumed.IsSuccessStatusCode && transport.Calls == 3, "Requests resume after the server cooldown");
        transport.Next = HttpStatusCode.ServiceUnavailable;
        using var unavailable = await client.GetAsync("https://other.test/read");
        try { using var ignored = await client.GetAsync("https://other.test/read"); } catch (HttpRequestException) { }
        check(transport.Calls == 4, "503 without Retry-After applies bounded exponential backoff");
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-09-14T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Transport : HttpMessageHandler
    {
        public int Calls;
        public HttpStatusCode Next = HttpStatusCode.TooManyRequests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var response = new HttpResponseMessage(Next);
            if (Next == HttpStatusCode.TooManyRequests) response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
            Next = HttpStatusCode.OK;
            return Task.FromResult(response);
        }
    }
}
