using System.Net;
using System.Text;
using TheIsleOverlay.Pandora;

namespace TheIsleOverlay.Tests;

public sealed class PandoraTelemetryProviderTests
{
    [Fact]
    public async Task GetSnapshotAsync_PostsHostCookiesAndMapsPlayerTelemetry()
    {
        const string payload = """
            {
              "inGame": true,
              "updatedAt": "2026-08-22T03:00:00Z",
              "player": {
                "steamId": "76561198000000000",
                "name": "Pandora Player",
                "dino": "Omniraptor",
                "gender": "Female",
                "x": 51000,
                "y": -49000,
                "z": 1200,
                "yaw": 157.37,
                "growth": 0.4,
                "health": 0.8,
                "stamina": 0.7,
                "hunger": 0.6,
                "thirst": 0.5
              }
            }
            """;
        var handler = new RecordingHandler(payload);
        var provider = new PandoraTelemetryProvider(
            new HttpClient(handler),
            new PandoraOptions
            {
                SessionCookieHeader = "connect.sid=session-value; cf_clearance=clearance-value"
            });

        var result = await provider.GetSnapshotAsync();

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/api/map/mylocation", handler.RequestUri?.AbsolutePath);
        Assert.Equal(
            "connect.sid=session-value; cf_clearance=clearance-value",
            handler.Cookie);
        Assert.True(result.Success);
        Assert.True(result.ServerOnline);
        Assert.True(result.PlayerOnline);
        Assert.Equal("PANDORA", result.Source);
        Assert.Equal("Pandora Player", result.Player?.Name);
        Assert.Equal("Omniraptor", result.Player?.Class);
        Assert.Equal("Isla Pandora", result.Player?.Server);
        Assert.True(result.Player?.Female);
        Assert.Equal(40d, result.Player?.GrowthPercent);
        Assert.Equal(80d, result.Player?.HealthPercent);
        Assert.Equal(60d, result.Player?.HungerPercent);
        Assert.Equal(50d, result.Player?.ThirstPercent);
        Assert.Equal(0.5d, result.Player!.MapLocation!.Value.Left, precision: 6);
        Assert.Equal(0.5d, result.Player.MapLocation.Value.Top, precision: 6);
        Assert.Equal(247.37d, result.Player.ExactMapHeadingDegrees!.Value, precision: 6);
    }

    [Fact]
    public async Task GetSnapshotAsync_ReturnsOnlineSourceWhenPlayerIsNotInGame()
    {
        var provider = new PandoraTelemetryProvider(
            new HttpClient(new RecordingHandler("""{"inGame":false,"player":null}""")),
            new PandoraOptions { SessionCookieHeader = "connect.sid=session-value" });

        var result = await provider.GetSnapshotAsync();

        Assert.True(result.Success);
        Assert.True(result.ServerOnline);
        Assert.False(result.PlayerOnline);
        Assert.Null(result.Player);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GetSnapshotAsync_MapsRejectedSessionToAuthenticationError(HttpStatusCode status)
    {
        var provider = new PandoraTelemetryProvider(
            new HttpClient(new RecordingHandler("{}", status)),
            new PandoraOptions { SessionCookieHeader = "connect.sid=expired" });

        await Assert.ThrowsAsync<PandoraAuthenticationException>(() => provider.GetSnapshotAsync());
    }

    private sealed class RecordingHandler(
        string payload,
        HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Cookie { get; private set; }
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Cookie = request.Headers.TryGetValues("Cookie", out var values)
                ? values.Single()
                : null;
            Method = request.Method;
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }
}
