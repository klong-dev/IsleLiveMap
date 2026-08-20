using System.Net;
using System.Text;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.Tests;

public sealed class IslePilotTelemetryProviderTests
{
    [Fact]
    public async Task GetSnapshotAsync_CombinesMarkerYawWithCachedPlayerPageStats()
    {
        const string markers = """
            {"ok":true,"markers":[{"steamId":"1","label":"You","x":1000,"y":-2000,"yaw":0,"self":true}]}
            """;
        const string playerPage = """
            <h1>Pteranodon</h1><span>Online</span>
            <span>Growth</span><span>40%</span>
            <span>Health</span><span>8 / 10</span>
            <span>Hunger</span><span>2 / 4</span>
            <span>Thirst</span><span>600 / 1000</span>
            """;
        var handler = new IslePilotHandler(markers, playerPage);
        var cookie = "eyJhbGciOiJub25lIn0.eyJwZXJzb25hTmFtZSI6IlRlc3QgUGxheWVyIn0.signature";
        var provider = new IslePilotTelemetryProvider(
            new HttpClient(handler),
            new IslePilotOptions { PlayerCookie = cookie, StatsRefreshInterval = TimeSpan.FromMinutes(1) });

        var first = await provider.GetSnapshotAsync();
        var second = await provider.GetSnapshotAsync();

        Assert.Equal("DinoVietnam", first.Source);
        Assert.True(first.PlayerOnline);
        Assert.Equal("Test Player", first.Player?.Name);
        Assert.Equal("Pteranodon", first.Player?.Class);
        Assert.Equal(8, first.Player?.ExactVitals?.Health);
        Assert.Null(first.Player?.ExactVitals?.Stamina);
        Assert.Equal(1000, first.Player?.Location?.X);
        Assert.Equal(90, first.Player?.ExactMapHeadingDegrees);
        Assert.Equal(2, handler.MarkerRequests);
        Assert.Equal(1, handler.PlayerPageRequests);
        Assert.Equal("islepilot_player=" + cookie, handler.LastCookie);
        Assert.Equal(first.Player?.ExactVitals?.Health, second.Player?.ExactVitals?.Health);
    }

    [Fact]
    public async Task GetSnapshotAsync_DoesNotWaitForSlowStatsRefreshAfterFirstSnapshot()
    {
        const string markers = """
            {"ok":true,"markers":[{"steamId":"1","label":"You","x":1000,"y":-2000,"yaw":45,"self":true}]}
            """;
        const string playerPage = """
            <h1>Pteranodon</h1><span>Online</span>
            <span>Health</span><span>8 / 10</span>
            """;
        var handler = new DelayedStatsHandler(markers, playerPage);
        using var client = new HttpClient(handler);
        var provider = new IslePilotTelemetryProvider(
            client,
            new IslePilotOptions
            {
                PlayerCookie = "test-cookie",
                StatsRefreshInterval = TimeSpan.Zero
            });

        await provider.GetSnapshotAsync();

        try
        {
            var secondSnapshotTask = provider.GetSnapshotAsync();
            await handler.SecondStatsRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var second = await secondSnapshotTask.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(135, second.Player?.ExactMapHeadingDegrees);
            Assert.Equal(2, handler.MarkerRequests);
        }
        finally
        {
            handler.ReleaseSecondStatsRequest.TrySetResult(true);
        }
    }

    [Fact]
    public async Task GetSnapshotAsync_UsesPremiumHostSlugAndDisplayNameFromOptions()
    {
        const string markers = """
            {"ok":true,"markers":[]}
            """;
        const string playerPage = "<h1>No active dinosaur</h1>";
        var handler = new IslePilotHandler(markers, playerPage);
        var provider = new IslePilotTelemetryProvider(
            new HttpClient(handler),
            new IslePilotOptions
            {
                BaseUri = new Uri("https://dinovietnampremium.islepilot.eu/"),
                ServerSlug = "dinovietnampremium",
                DisplayName = "DinoVietNam Premium",
                PlayerCookie = "test-cookie"
            });

        var snapshot = await provider.GetSnapshotAsync();

        Assert.Equal("DinoVietNam Premium", snapshot.Source);
        Assert.Equal("dinovietnampremium.islepilot.eu", handler.LastHost);
        Assert.Contains("/api/p/dinovietnampremium/map/markers", handler.RequestPaths);
    }

    private sealed class IslePilotHandler(string markers, string page) : HttpMessageHandler
    {
        private int _markerRequests;
        private int _playerPageRequests;

        public int MarkerRequests => _markerRequests;
        public int PlayerPageRequests => _playerPageRequests;
        public string? LastCookie { get; private set; }
        public string? LastHost { get; private set; }
        public List<string> RequestPaths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastCookie = request.Headers.TryGetValues("Cookie", out var values) ? values.Single() : null;
            LastHost = request.RequestUri?.Host;
            if (request.RequestUri is not null)
            {
                RequestPaths.Add(request.RequestUri.AbsolutePath);
            }
            if (request.RequestUri?.AbsolutePath.EndsWith("/map/markers", StringComparison.Ordinal) == true)
            {
                Interlocked.Increment(ref _markerRequests);
                return Task.FromResult(Response(markers, "application/json"));
            }

            Interlocked.Increment(ref _playerPageRequests);
            return Task.FromResult(Response(page, "text/html"));
        }

        private static HttpResponseMessage Response(string content, string mediaType) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, mediaType)
        };
    }

    private sealed class DelayedStatsHandler(string markers, string page) : HttpMessageHandler
    {
        private int _markerRequests;
        private int _statsRequests;

        public int MarkerRequests => _markerRequests;
        public TaskCompletionSource<bool> SecondStatsRequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReleaseSecondStatsRequest { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/map/markers", StringComparison.Ordinal) == true)
            {
                Interlocked.Increment(ref _markerRequests);
                return Response(markers, "application/json");
            }

            if (Interlocked.Increment(ref _statsRequests) == 2)
            {
                SecondStatsRequestStarted.TrySetResult(true);
                await ReleaseSecondStatsRequest.Task.WaitAsync(cancellationToken);
            }

            return Response(page, "text/html");
        }

        private static HttpResponseMessage Response(string content, string mediaType) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, mediaType)
        };
    }
}
