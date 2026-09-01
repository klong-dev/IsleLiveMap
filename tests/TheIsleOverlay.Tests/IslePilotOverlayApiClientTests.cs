using System.Net;
using System.Text;
using TheIsleOverlay.Core;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.Tests;

public sealed class IslePilotOverlayApiClientTests
{
    private const string Token = "super-secret-overlay-token";

    [Fact]
    public async Task GetMeAsync_UsesFixedServiceHostAndRequiredBearerHeaders()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, """
            {
              "hasData": true,
              "steamId": "76561198000000000",
              "personaName": "Player",
              "species": "Utahraptor"
            }
            """);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var me = await client.GetMeAsync();

        Assert.Equal("Utahraptor", me.Species);
        Assert.Equal(new Uri("https://islepilot.eu/api/overlay/me"), handler.RequestUri);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal(Token, handler.AuthorizationParameter);
        Assert.Equal("2", handler.OverlayVersion);
        Assert.Equal("application/json", handler.Accept);
    }

    [Fact]
    public async Task GetMapAsync_UsesOverlayMapEndpoint()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, """
            {
              "allowed": true,
              "calibration": {
                "a": { "worldX": 0, "worldY": 0, "u": 0, "v": 0 },
                "b": { "worldX": 100, "worldY": -100, "u": 1, "v": 1 }
              },
              "markers": []
            }
            """);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var map = await client.GetMapAsync();

        Assert.True(map.Allowed);
        Assert.Equal(new Uri("https://islepilot.eu/api/overlay/map"), handler.RequestUri);
    }

    [Fact]
    public async Task GetHeatmapAsync_UsesTenantEndpointCookieAndExplicitCells()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, """
            {"ok":true,"cells":[{"u":0.25,"v":0.75,"intensity":0.8}],"radius":30}
            """);
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, "signed-cookie");

        var heatmap = await client.GetHeatmapAsync("Dino Vietnam Premium 2");

        Assert.True(heatmap?.Ok);
        Assert.Equal(
            new Uri("https://dinovietnampremium.islepilot.eu/api/p/dinovietnampremium/map/heatmap"),
            handler.RequestUri);
        Assert.Equal("islepilot_player=signed-cookie", handler.Cookie);
        Assert.Equal(30, heatmap?.Radius);
        Assert.Single(heatmap?.Cells ?? []);
    }

    [Fact]
    public async Task GetHeatmapAsync_WithoutPlayerCookie_FailsClosedWithoutNetworkCall()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var heatmap = await client.GetHeatmapAsync("DinoVietnam");

        Assert.Null(heatmap);
        Assert.Null(handler.RequestUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task AuthenticationFailure_RequiresLoginWithoutLeakingToken(HttpStatusCode statusCode)
    {
        using var handler = new RecordingHandler(statusCode, "unauthorized");
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var exception = await Assert.ThrowsAnyAsync<TelemetryAuthenticationException>(
            () => client.GetMeAsync());

        Assert.DoesNotContain(Token, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsHeaderInjectionInToken()
    {
        using var httpClient = new HttpClient(new RecordingHandler(HttpStatusCode.OK, "{}"));
        var options = new IslePilotOverlayOptions { OverlayToken = "token\r\nX-Evil: true" };

        Assert.Throws<ArgumentException>(() => new IslePilotOverlayApiClient(httpClient, options));
    }

    private static IslePilotOverlayApiClient CreateClient(
        HttpClient httpClient,
        string? playerCookie = null) => new(
        httpClient,
        new IslePilotOverlayOptions
        {
            OverlayToken = Token,
            PlayerCookie = playerCookie
        });

    private sealed class RecordingHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string? OverlayVersion { get; private set; }
        public string? Accept { get; private set; }
        public string? Cookie { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Method = request.Method;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            OverlayVersion = request.Headers.TryGetValues("X-Overlay-Version", out var versions)
                ? versions.Single()
                : null;
            Accept = request.Headers.Accept.SingleOrDefault()?.MediaType;
            Cookie = request.Headers.TryGetValues("Cookie", out var cookies)
                ? cookies.Single()
                : null;

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }
}
