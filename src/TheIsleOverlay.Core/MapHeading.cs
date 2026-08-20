namespace TheIsleOverlay.Core;

public static class MapHeading
{
    public static double FromUnrealYaw(double yawDegrees) => Normalize(yawDegrees + 90d);

    public static double Normalize(double degrees)
    {
        var normalized = degrees % 360d;
        return normalized < 0d ? normalized + 360d : normalized;
    }
}
