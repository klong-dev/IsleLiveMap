using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.Tests;

public sealed class IslePilotVoiceLoginNavigationPolicyTests
{
    [Theory]
    [InlineData("https://voip.islepilot.eu/api/auth/steam?client=app")]
    [InlineData("https://steamcommunity.com/openid/login")]
    [InlineData("https://store.steampowered.com/login")]
    [InlineData("isle-voip://auth?sid=76561198000000000&token=secret")]
    public void IsAllowed_AllowsOnlyTheSteamVoiceLoginChain(string url)
    {
        Assert.True(IslePilotVoiceLoginNavigationPolicy.IsAllowed(url));
    }

    [Theory]
    [InlineData("http://voip.islepilot.eu/api/auth/steam")]
    [InlineData("https://islepilot.eu.evil.example/")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("javascript:alert(1)")]
    [InlineData("isle-overlay://callback?sid=76561198000000000&token=secret")]
    public void IsAllowed_RejectsNavigationOutsideTheLoginChain(string url)
    {
        Assert.False(IslePilotVoiceLoginNavigationPolicy.IsAllowed(url));
    }
}
