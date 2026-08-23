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
        var left = (location.X / WorldScale + XOffset) / MapWidth;
        var top = (location.Y / WorldScale + YOffset) / MapHeight;
        return new MapPoint(Math.Clamp(left, 0d, 1d), Math.Clamp(top, 0d, 1d));
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
