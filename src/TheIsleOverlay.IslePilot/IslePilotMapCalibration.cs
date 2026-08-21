using TheIsleOverlay.Core;

namespace TheIsleOverlay.IslePilot;

public sealed class IslePilotMapCalibration
{
    private readonly IslePilotMapCalibrationPointDto _a;
    private readonly IslePilotMapCalibrationPointDto _b;

    public IslePilotMapCalibration(IslePilotMapCalibrationDto calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);

        _a = calibration.A ?? throw new InvalidDataException("Map calibration point A is missing.");
        _b = calibration.B ?? throw new InvalidDataException("Map calibration point B is missing.");

        if (!IsFinite(_a) || !IsFinite(_b) ||
            Math.Abs(_b.WorldX - _a.WorldX) <= double.Epsilon ||
            Math.Abs(_b.WorldY - _a.WorldY) <= double.Epsilon)
        {
            throw new InvalidDataException("Map calibration is degenerate.");
        }
    }

    public MapPoint Project(double x, double y)
    {
        var tx = (x - _a.WorldX) / (_b.WorldX - _a.WorldX);
        var ty = (y - _a.WorldY) / (_b.WorldY - _a.WorldY);

        var u = _a.U + tx * (_b.U - _a.U);
        var v = _a.V + ty * (_b.V - _a.V);
        return new MapPoint(u, v);
    }

    public double ProjectHeading(double x, double y, double yawDegrees)
    {
        var yawRadians = yawDegrees * Math.PI / 180d;
        var current = Project(x, y);
        var ahead = Project(
            x + 1000d * Math.Cos(yawRadians),
            y + 1000d * Math.Sin(yawRadians));

        var deltaLeft = ahead.Left - current.Left;
        var deltaTop = ahead.Top - current.Top;

        // DirectionNeedle points upward at 0 degrees and rotates clockwise.
        return MapHeading.Normalize(Math.Atan2(deltaLeft, -deltaTop) * 180d / Math.PI);
    }

    private static bool IsFinite(IslePilotMapCalibrationPointDto point) =>
        double.IsFinite(point.WorldX) && double.IsFinite(point.WorldY) &&
        double.IsFinite(point.U) && double.IsFinite(point.V);
}
