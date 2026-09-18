namespace TheIsleOverlay.App.Tests;

public sealed class TeamOverlayFreshnessPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MarkerStaysVisibleThroughFifteenSecondsThenExpires()
    {
        Assert.True(TeamOverlayFreshnessPolicy.IsFresh(Now - TimeSpan.FromSeconds(12), Now));
        Assert.True(TeamOverlayFreshnessPolicy.IsFresh(Now - TimeSpan.FromSeconds(15), Now));
        Assert.False(TeamOverlayFreshnessPolicy.IsFresh(
            Now - TimeSpan.FromSeconds(15) - TimeSpan.FromMilliseconds(1),
            Now));
    }

    [Fact]
    public void MissingDefaultAndFutureTimestampsAreNotFresh()
    {
        Assert.False(TeamOverlayFreshnessPolicy.IsFresh(null, Now));
        Assert.False(TeamOverlayFreshnessPolicy.IsFresh(default(DateTimeOffset), Now));
        Assert.False(TeamOverlayFreshnessPolicy.IsFresh(Now + TimeSpan.FromMilliseconds(1), Now));
    }
}
