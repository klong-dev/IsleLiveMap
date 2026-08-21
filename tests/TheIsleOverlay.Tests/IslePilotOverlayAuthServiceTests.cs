using TheIsleOverlay.IslePilot;

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
}
