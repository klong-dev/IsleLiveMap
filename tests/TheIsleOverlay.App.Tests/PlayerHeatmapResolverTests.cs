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

    [Fact]
    public void RenderData_ContentEqualsMatchesEquivalentIndependentCollections()
    {
        var first = new PlayerHeatmapRenderData(
            [new PlayerHeatPoint(new MapPoint(0.2, 0.3), 0.75)],
            0.03);
        var second = new PlayerHeatmapRenderData(
            [new PlayerHeatPoint(new MapPoint(0.2, 0.3), 0.75)],
            0.03);

        Assert.True(first.ContentEquals(second));
        Assert.True(PlayerHeatmapRenderData.Empty.ContentEquals(PlayerHeatmapRenderData.Empty));
    }

    [Theory]
    [InlineData(0.21, 0.3, 0.75, 0.03)]
    [InlineData(0.2, 0.31, 0.75, 0.03)]
    [InlineData(0.2, 0.3, 0.76, 0.03)]
    [InlineData(0.2, 0.3, 0.75, 0.04)]
    public void RenderData_ContentEqualsDetectsChangedGeometryOrIntensity(
        double left,
        double top,
        double intensity,
        double radius)
    {
        var baseline = new PlayerHeatmapRenderData(
            [new PlayerHeatPoint(new MapPoint(0.2, 0.3), 0.75)],
            0.03);
        var changed = new PlayerHeatmapRenderData(
            [new PlayerHeatPoint(new MapPoint(left, top), intensity)],
            radius);

        Assert.False(baseline.ContentEquals(changed));
    }

    [Fact]
    public void RenderData_ContentEqualsDetectsChangedPointCount()
    {
        var baseline = new PlayerHeatmapRenderData(
            [new PlayerHeatPoint(new MapPoint(0.2, 0.3), 0.75)],
            0.03);
        var changed = new PlayerHeatmapRenderData(
            [
                new PlayerHeatPoint(new MapPoint(0.2, 0.3), 0.75),
                new PlayerHeatPoint(new MapPoint(0.4, 0.5), 0.5)
            ],
            0.03);

        Assert.False(baseline.ContentEquals(changed));
    }

    [Fact]
    public void Resolve_CanonicalizesEqualIntensityCellsBeforeComparison()
    {
        var first = PlayerHeatmapResolver.Resolve(new MapTelemetry
        {
            PlayerHeatmapEnabled = true,
            PlayerHeatmapCells =
            [
                Cell(0.4, 0.5, 0.75),
                Cell(0.2, 0.3, 0.75)
            ]
        });
        var second = PlayerHeatmapResolver.Resolve(new MapTelemetry
        {
            PlayerHeatmapEnabled = true,
            PlayerHeatmapCells =
            [
                Cell(0.2, 0.3, 0.75),
                Cell(0.4, 0.5, 0.75)
            ]
        });

        Assert.True(first.ContentEquals(second));
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
