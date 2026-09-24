using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using TheIsleOverlay.Core;
using TheIsleOverlay.Origin;

namespace TheIsleOverlay.Tests;

public sealed class OriginRealtimeTests
{
    private static OriginCommandResult Health(double hp = 70) => new("completed", JsonSerializer.SerializeToElement(new
    { success = true, species = "Rex", hp, maxHp = 100, stamina = 40, maxStamina = 50, growth = 0.5 }), null);

    [Fact]
    public async Task SlowPrimeAndOtherServerNeverBlockHealth()
    {
        var fake = new FakeClient
        {
            Health = async (server, ct) => server.Id == OriginServerId.Main ? Health() : await Wait(ct),
            Prime = (_, ct) => Wait(ct)
        };
        await using var session = new OriginStatsSession(fake);
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var reader = session.WatchAsync(ct.Token).GetAsyncEnumerator(ct.Token);
        Assert.True(await reader.MoveNextAsync());
        var stopwatch = Stopwatch.StartNew();
        var first = await Until(reader, s => s.Player?.ExactVitals?.Health == 70);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
        Assert.True(first.Player!.Prime!.IsSynchronizing);
        Assert.Equal(80, first.Player.StaminaPercent);
        var second = await Until(reader, s => s.UpdatedAt > first.UpdatedAt);
        Assert.InRange((second.UpdatedAt!.Value - first.UpdatedAt!.Value).TotalSeconds, 2.3, 3.5);
        Assert.Equal(1, fake.MaximumConcurrentHealthPerServer);
        Assert.Equal(1, fake.PrimeCalls);
    }

    [Fact]
    public async Task StaleFallbackExpiresWithoutRefreshingItsOwnTimestamp()
    {
        var calls = 0;
        var fake = new FakeClient { Health = (_, _) => Task.FromResult(Interlocked.Increment(ref calls) == 1
            ? Health() : new OriginCommandResult("pending", null, "slow")) };
        await using var session = new OriginStatsSession(fake, OriginServer.All[0]);
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var reader = session.WatchAsync(ct.Token).GetAsyncEnumerator(ct.Token);
        var first = await Until(reader, s => s.Player is not null);
        var stale = await Until(reader, s => s.LiveDataStale && s.Player is not null);
        Assert.Equal(first.UpdatedAt, stale.UpdatedAt);
        var expired = await Until(reader, s => s.LiveDataStale && s.Player is null);
        Assert.False(expired.PlayerOnline);
        Assert.Equal(1, fake.MaximumConcurrentHealthPerServer);
    }

    [Fact]
    public async Task ActiveServerSwitchDiscardsOldPrimeAndStats()
    {
        var fake = new FakeClient();
        var mainCalls = 0;
        fake.Health = (server, _) => Task.FromResult(server.Id == OriginServerId.Voice ? Health(90)
            : Interlocked.Increment(ref mainCalls) == 1 ? Health(10)
            : new OriginCommandResult("completed", JsonSerializer.SerializeToElement(new { success = false }), "no dino"));
        await using var session = new OriginStatsSession(fake, OriginServer.All[0]);
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await using var reader = session.WatchAsync(ct.Token).GetAsyncEnumerator(ct.Token);
        await Until(reader, s => s.Player?.ExactVitals?.Health == 10);
        var switched = await Until(reader, s => s.Player?.ExactVitals?.Health == 90);
        Assert.Equal(OriginServer.All[1].DisplayName, switched.Player!.Server);
    }

    [Fact]
    public async Task AuthenticationFailureIsPublishedThenSessionStops()
    {
        var fake = new FakeClient { Health = (_, _) => throw new OriginAuthenticationException("expired") };
        await using var session = new OriginStatsSession(fake, OriginServer.All[0]);
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var reader = session.WatchAsync(ct.Token).GetAsyncEnumerator(ct.Token);
        var failed = await Until(reader, s => s.SessionState == TelemetrySessionState.AuthenticationRequired);
        Assert.Null(failed.Player);
        Assert.False(await reader.MoveNextAsync());
    }

    [Fact]
    public async Task PendingCommandResumesSameIdAfterTwelvePolls()
    {
        var posts = 0; var gets = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.Method == HttpMethod.Post) { posts++; return Json("""{"id":"pending-1"}"""); }
            gets++;
            return Json(gets <= 12 ? """{"status":"queued"}""" : """{"status":"completed","result":{"species":"Rex"}}""");
        }));
        using var client = new OriginStatsClient("session=fixture", http);
        var pending = await client.ExecuteHealthAsync(OriginServer.All[0]);
        Assert.Equal("pending", pending.Status); Assert.Equal(12, gets);
        var completed = await client.ExecuteHealthAsync(OriginServer.All[0]);
        Assert.True(completed.IsCompletedSuccessfully); Assert.Equal(1, posts); Assert.Equal(13, gets);
    }

    [Fact]
    public async Task RateLimitDoesNotQueueAnotherCommand()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(_ =>
        {
            calls++; var response = new HttpResponseMessage((HttpStatusCode)429);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return response;
        }));
        using var client = new OriginStatsClient("session=fixture", http);
        await client.ExecuteHealthAsync(OriginServer.All[0]);
        await client.ExecuteHealthAsync(OriginServer.All[0]);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task OldQueuedHealthResultCannotResetFreshness()
    {
        var fake = new FakeClient { Health = (_, _) => Task.FromResult(Health() with { RequestedAt = DateTimeOffset.UtcNow.AddSeconds(-30) }) };
        await using var session = new OriginStatsSession(fake, OriginServer.All[0]);
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var reader = session.WatchAsync(ct.Token).GetAsyncEnumerator(ct.Token);
        var snapshot = await Until(reader, s => s.LiveDataStale);
        Assert.Null(snapshot.Player);
    }

    [Fact]
    public async Task DeletedCommandDoesNotLeaveLaneStuckForever()
    {
        var posts = 0;
        using var http = new HttpClient(new Handler(request => request.Method == HttpMethod.Post
            ? Json("{\"id\":\"cmd-" + ++posts + "\"}")
            : posts == 1 ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : Json("""{"status":"completed","result":{"species":"Rex"}}""")));
        using var client = new OriginStatsClient("session=fixture", http);
        Assert.Equal("failed", (await client.ExecuteHealthAsync(OriginServer.All[0])).Status);
        Assert.True((await client.ExecuteHealthAsync(OriginServer.All[0])).IsCompletedSuccessfully);
        Assert.Equal(2, posts);
    }

    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { var response = respond(request); response.RequestMessage = request; return Task.FromResult(response); }
    }
    private static async Task<OriginCommandResult> Wait(CancellationToken ct)
    { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Health(); }
    private static async Task<TelemetrySnapshot> Until(IAsyncEnumerator<TelemetrySnapshot> reader, Func<TelemetrySnapshot, bool> predicate)
    {
        while (await reader.MoveNextAsync()) if (predicate(reader.Current)) return reader.Current;
        throw new InvalidOperationException("Session ended before expected state.");
    }
    private sealed class FakeClient : IOriginStatsClient
    {
        private readonly ConcurrentDictionary<string, int> _active = new();
        public int MaximumConcurrentHealthPerServer;
        public int PrimeCalls;
        public OriginServer? PreferredServer => null;
        public Func<OriginServer, CancellationToken, Task<OriginCommandResult>> Health = (_, _) => Task.FromResult(OriginRealtimeTests.Health());
        public Func<OriginServer, CancellationToken, Task<OriginCommandResult>> Prime = (_, _) => Task.FromResult(new OriginCommandResult("failed", null, "timeout"));
        public async Task<OriginCommandResult> ExecuteHealthAsync(OriginServer server, CancellationToken cancellationToken = default)
        {
            var n = _active.AddOrUpdate(server.ApiId, 1, (_, previous) => previous + 1);
            MaximumConcurrentHealthPerServer = Math.Max(MaximumConcurrentHealthPerServer, n);
            try { return await Health(server, cancellationToken); }
            finally { _active.AddOrUpdate(server.ApiId, 0, (_, previous) => previous - 1); }
        }
        public Task<OriginCommandResult> ExecutePrimeAsync(OriginServer server, CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref PrimeCalls); return Prime(server, cancellationToken); }
        public void Dispose() { }
    }
}
