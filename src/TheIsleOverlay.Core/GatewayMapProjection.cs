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
}
