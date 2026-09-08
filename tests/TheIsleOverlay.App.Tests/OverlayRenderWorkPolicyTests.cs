using TheIsleOverlay.Core;

namespace TheIsleOverlay.App.Tests;

public sealed class OverlayRenderWorkPolicyTests
{
    [Theory]
    [InlineData(null, 90d, true)]
    [InlineData(90d, 90d, false)]
    [InlineData(0d, 360d, false)]
    [InlineData(359.999d, 0d, false)]
    [InlineData(359d, 1d, true)]
    [InlineData(45d, 46d, true)]
    [InlineData(45d, double.NaN, false)]
    public void Heading_OnlySchedulesAnimationForNewTarget(double? previous, double next, bool expected) =>
        Assert.Equal(expected, OverlayRenderWorkPolicy.HeadingChanged(previous, next));

    [Fact]
    public void MissionList_TwelveHundredGpsFramesDoNotRebuildUnchangedItems()
    {
        var cache = new MissionListRenderCache();
        var rebuilds = 0;
        for (var frame = 0; frame < 1_200; frame++)
        {
            // Equivalent content may arrive in newly deserialized objects.
            if (cache.Update([new() { Name = "Explore", Done = false }]))
                rebuilds++;
        }
        Assert.Equal(1, rebuilds);
        Assert.True(cache.Update([new() { Name = "Explore", Done = true }]));
        Assert.True(cache.Update([]));
        Assert.False(cache.Update([]));
        cache.Reset();
        Assert.True(cache.Update([new() { Name = "Explore", Done = false }]));
    }
}
