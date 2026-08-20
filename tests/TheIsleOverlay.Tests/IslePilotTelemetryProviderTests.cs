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

    [Theory]
    [InlineData("https://dinovietnampremium.islepilot.eu/", "dinovietnampremium", "DinoVietNam Premium")]
    [InlineData("https://hoho.islepilot.eu/", "hoho", "HoHo")]
    public async Task GetSnapshotAsync_UsesConfiguredHostSlugAndDisplayName(
        string baseUri,
        string serverSlug,
        string displayName)
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
                BaseUri = new Uri(baseUri),
                ServerSlug = serverSlug,
                DisplayName = displayName,
                PlayerCookie = "test-cookie"
            });

        var snapshot = await provider.GetSnapshotAsync();

        Assert.Equal(displayName, snapshot.Source);
        Assert.Equal(new Uri(baseUri).Host, handler.LastHost);
        Assert.Contains($"/api/p/{serverSlug}/map/markers", handler.RequestPaths);
    }

    [Fact]
    public async Task GetSnapshotAsync_KeepsMarkersWhenPlayerPageIsUnavailable()
    {
        const string markers = """
            {"ok":true,"markers":[{"steamId":"1","label":"You","x":1234,"y":-5678,"yaw":90,"self":true}]}
            """;
        var handler = new FailingStatsHandler(markers);
        var provider = new IslePilotTelemetryProvider(
            new HttpClient(handler),
            new IslePilotOptions { PlayerCookie = "test-cookie" });

        var first = await provider.GetSnapshotAsync();
        var second = await provider.GetSnapshotAsync();

        Assert.True(first.Success);
        Assert.True(first.PlayerOnline);
        Assert.Equal(1234, first.Player?.Location?.X);
        Assert.Equal(180, first.Player?.ExactMapHeadingDegrees);
        Assert.Null(first.Player?.ExactVitals?.Health);
        Assert.Equal(2, handler.MarkerRequests);
        Assert.Equal(1, handler.PlayerPageRequests);
        Assert.True(second.Success);
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

    private sealed class FailingStatsHandler(string markers) : HttpMessageHandler
    {
        private int _markerRequests;
        private int _playerPageRequests;

        public int MarkerRequests => _markerRequests;
        public int PlayerPageRequests => _playerPageRequests;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/map/markers", StringComparison.Ordinal) == true)
            {
                Interlocked.Increment(ref _markerRequests);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(markers, Encoding.UTF8, "application/json")
                });
            }

            Interlocked.Increment(ref _playerPageRequests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
