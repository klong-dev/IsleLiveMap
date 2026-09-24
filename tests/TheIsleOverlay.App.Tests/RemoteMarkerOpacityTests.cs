using TheIsleOverlay.App;

namespace TheIsleOverlay.App.Tests;

public sealed class RemoteMarkerOpacityTests
{
    [Theory]
    [InlineData(true, false, 0.60)]
    [InlineData(true, true, 0.60)]
    [InlineData(false, true, 0.82)]
    [InlineData(false, false, 1.0)]
    public void StaleMarkersAreReadableButDistinctFromLive(
        bool stale, bool provisional, double expected)
    {
        Assert.Equal(expected, MainWindow.RemoteMarkerOpacity(stale, provisional));
    }
}
