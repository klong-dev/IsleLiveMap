using TheIsleOverlay.IslePilot;
using System.Net;
using System.Text;

namespace TheIsleOverlay.Tests;

public sealed class IslePilotOverlayAuthServiceTests
{
    [Fact]
    public void LoginUri_IsFixedToTheIslePilotOverlayEndpoint()
    {
        Assert.Equal(
            new Uri("https://islepilot.eu/api/overlay/auth/steam"),
            IslePilotOverlayAuthService.LoginUri);
    }

    [Theory]
    [InlineData("isle-overlay://callback?sid=76561198000000000&token=header.payload.signature")]
    [InlineData("isle-overlay://auth/?token=abc%2B123%2Fxyz&sid=76561198000000000")]
    public void TryParseCallback_AcceptsTheReadOnlyOverlayToken(string callback)
    {
        var parsed = IslePilotOverlayAuthService.TryParseCallback(callback, out var result);

        Assert.True(parsed);
        Assert.NotNull(result);
        Assert.Equal("76561198000000000", result.SteamId);
        Assert.False(string.IsNullOrWhiteSpace(result.OverlayToken));
    }

    [Theory]
    [InlineData("https://islepilot.eu/?sid=76561198000000000&token=secret")]
    [InlineData("isle-overlay://callback?sid=not-a-steam-id&token=secret")]
    [InlineData("isle-overlay://callback?sid=76561198000000000")]
    [InlineData("isle-overlay://callback?sid=76561198000000000&token=bad%0D%0Avalue")]
    [InlineData("isle-overlay://callback?sid=76561198000000000&sid=76561198000000001&token=secret")]
    public void TryParseCallback_RejectsUntrustedOrAmbiguousInput(string callback)
    {
        Assert.False(IslePilotOverlayAuthService.TryParseCallback(callback, out var result));
        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsValidForAnAuthenticatedMeResponse()
    {
        using var httpClient = new HttpClient(new StubHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"hasData\":true}", Encoding.UTF8, "application/json")
        }));

        var state = await IslePilotOverlayAuthService.ValidateAsync(
            httpClient,
            Credentials());

        Assert.Equal(IslePilotOverlayAuthValidationState.Valid, state);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ValidateAsync_ReturnsInvalidForRejectedCredentials(HttpStatusCode statusCode)
    {
        using var httpClient = new HttpClient(new StubHandler(new HttpResponseMessage(statusCode)));

        var state = await IslePilotOverlayAuthService.ValidateAsync(
            httpClient,
            Credentials());

        Assert.Equal(IslePilotOverlayAuthValidationState.Invalid, state);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsUnavailableForARecoverableNetworkFailure()
    {
        using var httpClient = new HttpClient(new StubHandler(new HttpRequestException("offline")));

        var state = await IslePilotOverlayAuthService.ValidateAsync(
            httpClient,
            Credentials());

        Assert.Equal(IslePilotOverlayAuthValidationState.Unavailable, state);
    }

    private static IslePilotOverlayAuthResult Credentials() => new(
        "76561198000000000",
        "overlay-token");

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage? _response;
        private readonly Exception? _exception;

        public StubHandler(HttpResponseMessage response) => _response = response;

        public StubHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            _exception is null
                ? Task.FromResult(_response!)
                : Task.FromException<HttpResponseMessage>(_exception);
    }
}
