using System.Net;
using System.Text;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.Tests;

public sealed class IslePilotVoiceAuthServiceTests
{
    [Fact]
    public void LoginUri_IsFixedToTheSteamAppEndpoint()
    {
        Assert.Equal(
            new Uri("https://voip.islepilot.eu/api/auth/steam?client=app"),
            IslePilotVoiceAuthService.LoginUri);
    }

    [Theory]
    [InlineData("isle-voip://auth?sid=76561198000000000&token=voice-token")]
    [InlineData("isle-voip://callback?token=abc%2B123%2Fxyz&sid=76561198000000000")]
    public void TryParseCallback_AcceptsSteamId64AndVoiceToken(string callback)
    {
        Assert.True(IslePilotVoiceAuthService.TryParseCallback(callback, out var result));
        Assert.NotNull(result);
        Assert.Equal("76561198000000000", result.SteamId64);
        Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
    }

    [Theory]
    [InlineData("https://voip.islepilot.eu/?sid=76561198000000000&token=secret")]
    [InlineData("isle-voip://auth?sid=invalid&token=secret")]
    [InlineData("isle-voip://auth?sid=76561198000000000")]
    [InlineData("isle-voip://auth?sid=76561198000000000&token=bad%0D%0Avalue")]
    public void TryParseCallback_RejectsUntrustedInput(string callback)
    {
        Assert.False(IslePilotVoiceAuthService.TryParseCallback(callback, out var result));
        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_RequiresTicketForTheSameSteamId64()
    {
        using var validClient = Client(
            HttpStatusCode.OK,
            "{\"ticket\":\"short-lived\",\"steamId64\":\"76561198000000000\"}");
        using var wrongAccountClient = Client(
            HttpStatusCode.OK,
            "{\"ticket\":\"short-lived\",\"steamId64\":\"76561198000000001\"}");

        Assert.Equal(
            IslePilotVoiceAuthValidationState.Valid,
            await IslePilotVoiceAuthService.ValidateAsync(validClient, Credentials()));
        Assert.Equal(
            IslePilotVoiceAuthValidationState.Invalid,
            await IslePilotVoiceAuthService.ValidateAsync(wrongAccountClient, Credentials()));
    }

    [Fact]
    public async Task ValidateAsync_DistinguishesRejectedAndUnavailableService()
    {
        using var rejectedClient = Client(HttpStatusCode.Unauthorized, string.Empty);
        using var unavailableClient = Client(HttpStatusCode.ServiceUnavailable, string.Empty);

        Assert.Equal(
            IslePilotVoiceAuthValidationState.Invalid,
            await IslePilotVoiceAuthService.ValidateAsync(rejectedClient, Credentials()));
        Assert.Equal(
            IslePilotVoiceAuthValidationState.Unavailable,
            await IslePilotVoiceAuthService.ValidateAsync(unavailableClient, Credentials()));
    }

    private static IslePilotVoiceAuthResult Credentials() => new(
        "76561198000000000",
        "voice-token");

    private static HttpClient Client(HttpStatusCode status, string body) => new(
        new StubHandler(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        }));

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }
}
