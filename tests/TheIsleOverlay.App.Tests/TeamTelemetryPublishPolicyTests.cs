namespace TheIsleOverlay.App.Tests;

public sealed class TeamTelemetryPublishPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ChangedTelemetryPublishesImmediately()
    {
        Assert.True(TeamTelemetryPublishPolicy.ShouldPublish(
            hasSnapshot: true,
            version: 4,
            publishedVersion: 3,
            receivedAt: Now,
            lastPublishedAt: Now,
            now: Now));
    }

    [Fact]
    public void UnchangedFreshTelemetryRefreshesEveryFiveSeconds()
    {
        Assert.False(TeamTelemetryPublishPolicy.ShouldPublish(
            true, 4, 4, Now - TimeSpan.FromSeconds(2), Now - TimeSpan.FromSeconds(4), Now));
        Assert.True(TeamTelemetryPublishPolicy.ShouldPublish(
            true, 4, 4, Now - TimeSpan.FromSeconds(2), Now - TimeSpan.FromSeconds(5), Now));
    }

    [Fact]
    public void SilentOrClearedSourceIsNotRefreshedForever()
    {
        Assert.False(TeamTelemetryPublishPolicy.ShouldPublish(
            true, 4, 4, Now - TimeSpan.FromSeconds(11), Now - TimeSpan.FromSeconds(6), Now));
        Assert.False(TeamTelemetryPublishPolicy.ShouldPublish(
            false, 4, 4, Now, Now - TimeSpan.FromSeconds(6), Now));
    }
}
