using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

internal static class PlayerHeatmapResolver
{
    private const int MaximumHeatPoints = 512;
    private const double DefaultRadius = 0.03d;

    public static PlayerHeatmapRenderData Resolve(MapTelemetry? map)
    {
        if (map?.PlayerHeatmapEnabled != true
            || map.PlayerHeatmapCells is not { Count: > 0 } cells)
        {
            return PlayerHeatmapRenderData.Empty;
        }

        var points = cells
            .Where(cell => double.IsFinite(cell.Location.Left)
                           && double.IsFinite(cell.Location.Top)
                           && double.IsFinite(cell.Intensity)
                           && cell.Location.Left is >= 0d and <= 1d
                           && cell.Location.Top is >= 0d and <= 1d)
            .OrderByDescending(cell => cell.Intensity)
            .ThenBy(cell => cell.Location.Left)
            .ThenBy(cell => cell.Location.Top)
            .Take(MaximumHeatPoints)
            .Select(cell => new PlayerHeatPoint(
                cell.Location,
                Math.Clamp(cell.Intensity, 0d, 1d)))
            .ToArray();
        if (points.Length == 0)
        {
            return PlayerHeatmapRenderData.Empty;
        }

        var radius = map.PlayerHeatmapRadius is { } value
                     && double.IsFinite(value)
                     && value is >= 0.001d and <= 0.25d
            ? value
            : DefaultRadius;
        return new PlayerHeatmapRenderData(points, radius);
    }
}

internal readonly record struct PlayerHeatPoint(
    MapPoint Point,
    double Intensity);

internal readonly record struct PlayerHeatmapRenderData(
    IReadOnlyList<PlayerHeatPoint> Points,
    double Radius)
{
    public static PlayerHeatmapRenderData Empty { get; } = new([], 0d);

    public bool ContentEquals(PlayerHeatmapRenderData other)
    {
        if (!Radius.Equals(other.Radius) || Points.Count != other.Points.Count)
        {
            return false;
        }

        for (var index = 0; index < Points.Count; index++)
        {
            if (Points[index] != other.Points[index])
            {
                return false;
            }
        }

        return true;
    }
}
