namespace TheIsleOverlay.App;

internal static class MapRenderInvalidationPolicy
{
    public static bool ShouldPositionMap(
        bool mapPositionChanged,
        bool heatmapChanged,
        bool remoteMarkersChanged,
        bool markerVisibilityChanged) =>
        mapPositionChanged
        || heatmapChanged
        || remoteMarkersChanged
        || markerVisibilityChanged;
}
