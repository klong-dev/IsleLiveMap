using System.Net;
using System.Text;
using TheIsleOverlay.EraGaming;

namespace TheIsleOverlay.Tests;

public sealed class EraGamingTelemetryProviderTests
{
    [Fact]
    public async Task GetSnapshotAsync_SendsSessionCookieAndParsesExactVitals()
    {
        const string payload = """
            {
              "success": true,
              "serverOnline": true,
              "playerOnline": true,
              "updatedAt": "2026-08-20T09:00:00Z",
              "player": {
                "name": "Test Player",
                "class": "BP_Dryosaurus_C",
                "growthPercent": 40,
                "healthPercent": 80,
                "staminaPercent": 70,
                "hungerPercent": 60,
                "thirstPercent": 50,
                "location": { "x": 51000, "y": -49000, "z": 1200 },
                "exactVitals": {
                  "health": 800,
                  "maxHealth": 1000,
                  "hunger": 1500,
                  "maxHunger": 2000,
                  "foodValue": 4250,
                  "maxFoodValue": 5000
                },
                "exactVitalsSource": "TestSource"
              }
            }
            """;
        var handler = new RecordingHandler(payload);
        var provider = new EraGamingTelemetryProvider(
            new HttpClient(handler),
            new EraGamingOptions { SessionCookie = "test-session" });

        var result = await provider.GetSnapshotAsync();

        Assert.True(result.PlayerOnline);
        Assert.Equal(40, result.Player?.GrowthPercent);
        Assert.Equal(60, result.Player?.HungerPercent);
        Assert.Equal(800, result.Player?.ExactVitals?.Health);
        Assert.Equal(1500, result.Player?.ExactVitals?.Hunger);
        Assert.Equal(2000, result.Player?.ExactVitals?.MaxHunger);
        Assert.Equal("era_session=test-session", handler.Cookie);
        Assert.Equal("/api/theisle/map", handler.RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task GetSnapshotAsync_MapsUnauthorizedToAuthenticationError()
    {
        var provider = new EraGamingTelemetryProvider(
            new HttpClient(new RecordingHandler("{}", HttpStatusCode.Unauthorized)),
            new EraGamingOptions { SessionCookie = "expired" });

        await Assert.ThrowsAsync<EraGamingAuthenticationException>(() => provider.GetSnapshotAsync());
    }

    private sealed class RecordingHandler(string payload, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Cookie { get; private set; }
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Cookie = request.Headers.TryGetValues("Cookie", out var values) ? values.Single() : null;
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }
}
