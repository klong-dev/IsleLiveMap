using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.Tests;

public sealed class IslePilotOverlayLoginNavigationPolicyTests
{
    [Theory]
    [InlineData("https://islepilot.eu/api/overlay/auth/steam")]
    [InlineData("https://steamcommunity.com/openid/login")]
    [InlineData("https://store.steampowered.com/login")]
    [InlineData("isle-overlay://callback?sid=76561198000000000&token=secret")]
    public void IsAllowed_AllowsOnlyTheSteamLoginChain(string url)
    {
        Assert.True(IslePilotOverlayLoginNavigationPolicy.IsAllowed(url));
    }

    [Theory]
    [InlineData("http://islepilot.eu/api/overlay/auth/steam")]
    [InlineData("https://islepilot.eu.example.com/phishing")]
    [InlineData("https://steamcommunity.com.example.com/phishing")]
    [InlineData("file:///C:/Windows/System32/drivers/etc/hosts")]
    [InlineData("javascript:alert(1)")]
    public void IsAllowed_RejectsNavigationOutsideTheLoginChain(string url)
    {
        Assert.False(IslePilotOverlayLoginNavigationPolicy.IsAllowed(url));
    }
}
