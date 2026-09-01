using TheIsleOverlay.App;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App.Tests;

public sealed class PlayerHeatmapResolverTests
{
    [Fact]
    public void Resolve_FailsClosedWhenProviderDidNotEnableHeatmap()
    {
        var map = new MapTelemetry
        {
            PlayerHeatmapCells = [Cell(0.4, 0.4, 1)]
        };

        Assert.Empty(PlayerHeatmapResolver.Resolve(map).Points);
    }

    [Fact]
    public void Resolve_UsesOnlyExplicitProviderCellsAndOfficialRadius()
    {
        var map = new MapTelemetry
        {
            PlayerHeatmapEnabled = true,
            PlayerHeatmapRadius = 0.03,
            PlayerHeatmapCells =
            [
                Cell(0.4, 0.5, 0.75),
                Cell(-1, 0.5, 1)
            ]
        };

        var result = PlayerHeatmapResolver.Resolve(map);
        var point = Assert.Single(result.Points);

        Assert.Equal(new MapPoint(0.4, 0.5), point.Point);
        Assert.Equal(0.75, point.Intensity);
        Assert.Equal(0.03, result.Radius);
    }

    private static MapHeatCellTelemetry Cell(
        double left,
        double top,
        double intensity) => new()
        {
            Location = new MapPoint(left, top),
            Intensity = intensity
        };
}
