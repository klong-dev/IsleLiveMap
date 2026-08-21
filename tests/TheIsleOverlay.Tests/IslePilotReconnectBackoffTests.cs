using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.Tests;

public sealed class IslePilotReconnectBackoffTests
{
    [Fact]
    public void NextDelay_FollowsCappedSequenceAtMidpointJitter()
    {
        var backoff = new IslePilotReconnectBackoff(() => 0.5);

        var seconds = Enumerable.Range(0, 7)
            .Select(_ => backoff.NextDelay().TotalSeconds)
            .ToArray();

        Assert.Equal([1d, 2d, 4d, 8d, 15d, 15d, 15d], seconds);
    }

    [Fact]
    public void NextDelay_AppliesTwentyPercentJitterBounds()
    {
        var low = new IslePilotReconnectBackoff(() => 0);
        var high = new IslePilotReconnectBackoff(() => 1);

        Assert.Equal(0.8, low.NextDelay().TotalSeconds, precision: 8);
        Assert.Equal(1.2, high.NextDelay().TotalSeconds, precision: 8);
    }

    [Fact]
    public void Reset_ReturnsSequenceToOneSecond()
    {
        var backoff = new IslePilotReconnectBackoff(() => 0.5);
        _ = backoff.NextDelay();
        _ = backoff.NextDelay();
        _ = backoff.NextDelay();

        backoff.Reset();

        Assert.Equal(1, backoff.NextDelay().TotalSeconds);
    }
}
