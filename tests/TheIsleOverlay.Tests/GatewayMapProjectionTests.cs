using TheIsleOverlay.Core;

namespace TheIsleOverlay.Tests;

public sealed class GatewayMapProjectionTests
{
    [Fact]
    public void Project_MapsKnownWorldCenterToImageCenter()
    {
        var location = new WorldLocation { X = 51_000, Y = -49_000, Z = 0 };

        var point = GatewayMapProjection.Project(location);

        Assert.Equal(0.5d, point.Left, precision: 8);
        Assert.Equal(0.5d, point.Top, precision: 8);
    }

    [Fact]
    public void Unproject_RoundTripsAProjectedGatewayCoordinate()
    {
        var expected = new WorldLocation { X = -329_555.776, Y = 112_985.442 };

        var restored = GatewayMapProjection.Unproject(GatewayMapProjection.Project(expected));

        Assert.Equal(expected.X, restored.X, precision: 6);
        Assert.Equal(expected.Y, restored.Y, precision: 6);
    }

    [Theory]
    [InlineData(-9_000_000, -9_000_000, 0, 0)]
    [InlineData(9_000_000, 9_000_000, 1, 1)]
    public void Project_ClampsCoordinatesToMapBounds(double x, double y, double expectedLeft, double expectedTop)
    {
        var point = GatewayMapProjection.Project(new WorldLocation { X = x, Y = y });

        Assert.Equal(expectedLeft, point.Left);
        Assert.Equal(expectedTop, point.Top);
    }

    [Fact]
    public void ProjectUnclamped_PreservesOutOfBoundsCoordinatesForGeometryValidation()
    {
        var point = GatewayMapProjection.ProjectUnclamped(
            new WorldLocation { X = 9_000_000, Y = -9_000_000 });

        Assert.True(point.Left > 1d);
        Assert.True(point.Top < 0d);
    }

    [Fact]
    public void ResolveForBundledTexture_PrefersWorldCoordinateOverProviderCalibration()
    {
        var world = new WorldLocation { X = -329_555.776, Y = 112_985.442 };
        var providerPoint = new MapPoint(0.164, 0.641);

        var point = GatewayMapProjection.ResolveForBundledTexture(world, providerPoint);

        var expected = GatewayMapProjection.Project(world);
        Assert.Equal(expected, point);
        Assert.NotEqual(providerPoint, point);
    }

    [Fact]
    public void ResolveForBundledTexture_UsesProviderPointOnlyWithoutWorldCoordinate()
    {
        var point = GatewayMapProjection.ResolveForBundledTexture(
            null,
            new MapPoint(1.1, -0.1));

        Assert.Equal(new MapPoint(1, 0), point);
    }

    [Fact]
    public void ResolveForBundledTexture_RejectsNonFiniteCoordinates()
    {
        var point = GatewayMapProjection.ResolveForBundledTexture(
            new WorldLocation { X = double.NaN, Y = 10 },
            new MapPoint(double.PositiveInfinity, 0.5));

        Assert.Null(point);
    }
}
