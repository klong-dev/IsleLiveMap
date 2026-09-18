using TheIsleOverlay.TeamRelay;

namespace TheIsleOverlay.Tests;

public sealed class ServerIdentityNormalizerTests
{
    [Theory]
    [InlineData(" 115.72.226.156 : 7777 ", "115.72.226.156:7777")]
    [InlineData("UDP://PLAY.EXAMPLE.COM:7788", "play.example.com:7788")]
    [InlineData("play.example.com.", "play.example.com:7777")]
    [InlineData("[2001:db8::1]:7777", "[2001:db8::1]:7777")]
    [InlineData("2001:db8::1", "[2001:db8::1]:7777")]
    public void NormalizeEndpoint_CanonicalizesAddressDnsPortAndWhitespace(
        string value,
        string expected)
    {
        Assert.Equal(expected, ServerIdentityNormalizer.NormalizeEndpoint(value));
    }

    [Fact]
    public void AreSame_PrefersEndpointThenFallsBackToNormalizedServerName()
    {
        Assert.True(ServerIdentityNormalizer.AreSame(
            "115.72.226.156:7777",
            "Display name A",
            "udp://115.72.226.156:7777",
            "Display name B"));
        Assert.True(ServerIdentityNormalizer.AreSame(
            null,
            "  Origin   Voice Chat  ",
            null,
            "origin voice chat"));
        Assert.False(ServerIdentityNormalizer.AreSame(
            "115.72.226.156:7777",
            "Same display name",
            "115.72.226.157:7777",
            "Same display name"));
    }

    [Fact]
    public void AreSame_AcceptsLegacyServerKeyWhenCallerPromotesItToEndpoint()
    {
        const string legacyServerKey = "115.72.226.156:7777";

        Assert.True(ServerIdentityNormalizer.AreSame(
            "udp://115.72.226.156:7777",
            "Display name changed",
            legacyServerKey,
            "Another display name"));
    }

    [Fact]
    public void Compare_IsUnknownUntilServerIdentityCanBeProven()
    {
        Assert.Equal(
            ServerIdentityNormalizer.MatchResult.Unknown,
            ServerIdentityNormalizer.Compare(
                null,
                null,
                "115.72.226.156:7777",
                "Gateway"));
        Assert.Equal(
            ServerIdentityNormalizer.MatchResult.Different,
            ServerIdentityNormalizer.Compare(
                "115.72.226.157:7777",
                "Gateway",
                "115.72.226.156:7777",
                "Gateway"));
    }
}
