namespace TheIsleOverlay.App.Tests;

public sealed class MapRenderInvalidationPolicyTests
{
    [Fact]
    public void CameraHeadingOnly_DoesNotRepositionTheWholeMap()
    {
        Assert.False(MapRenderInvalidationPolicy.ShouldPositionMap(
            mapPositionChanged: false,
            heatmapChanged: false,
            remoteMarkersChanged: false,
            markerVisibilityChanged: false));
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void ChangedMapContent_RepositionsTheMap(
        bool mapPositionChanged,
        bool heatmapChanged,
        bool remoteMarkersChanged,
        bool markerVisibilityChanged)
    {
        Assert.True(MapRenderInvalidationPolicy.ShouldPositionMap(
            mapPositionChanged,
            heatmapChanged,
            remoteMarkersChanged,
            markerVisibilityChanged));
    }
}
