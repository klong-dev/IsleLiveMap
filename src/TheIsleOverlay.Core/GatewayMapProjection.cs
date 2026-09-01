namespace TheIsleOverlay.Core;

public readonly record struct MapPoint(double Left, double Top);

public static class GatewayMapProjection
{
    private const double WorldScale = 1000d;
    private const double XOffset = 505d;
    private const double YOffset = 607d;
    private const double MapWidth = 1112d;
    private const double MapHeight = 1116d;

    public static MapPoint Project(WorldLocation location)
    {
        var point = ProjectUnclamped(location);
        return new MapPoint(
            Math.Clamp(point.Left, 0d, 1d),
            Math.Clamp(point.Top, 0d, 1d));
    }

    public static MapPoint ProjectUnclamped(WorldLocation location)
    {
        var left = (location.X / WorldScale + XOffset) / MapWidth;
        var top = (location.Y / WorldScale + YOffset) / MapHeight;
        return new MapPoint(left, top);
    }

    public static WorldLocation Unproject(MapPoint point)
    {
        var left = double.IsFinite(point.Left) ? Math.Clamp(point.Left, 0d, 1d) : 0.5d;
        var top = double.IsFinite(point.Top) ? Math.Clamp(point.Top, 0d, 1d) : 0.5d;
        return new WorldLocation
        {
            X = (left * MapWidth - XOffset) * WorldScale,
            Y = (top * MapHeight - YOffset) * WorldScale
        };
    }

    public static MapPoint? ResolveForBundledTexture(
        WorldLocation? worldLocation,
        MapPoint? providerMapLocation)
    {
        // Normalized points supplied by a provider are calibrated against that
        // provider's own basemap. The overlay renders a bundled Gateway texture,
        // so world coordinates must win whenever they are available.
        if (worldLocation is { } world
            && double.IsFinite(world.X)
            && double.IsFinite(world.Y))
        {
            return Project(world);
        }

        if (providerMapLocation is not { } map
            || !double.IsFinite(map.Left)
            || !double.IsFinite(map.Top))
        {
            return null;
        }

        return new MapPoint(
            Math.Clamp(map.Left, 0d, 1d),
            Math.Clamp(map.Top, 0d, 1d));
    }
}
