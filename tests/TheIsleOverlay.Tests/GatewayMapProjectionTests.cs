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

    [Theory]
    [InlineData(-9_000_000, -9_000_000, 0, 0)]
    [InlineData(9_000_000, 9_000_000, 1, 1)]
    public void Project_ClampsCoordinatesToMapBounds(double x, double y, double expectedLeft, double expectedTop)
    {
        var point = GatewayMapProjection.Project(new WorldLocation { X = x, Y = y });

        Assert.Equal(expectedLeft, point.Left);
        Assert.Equal(expectedTop, point.Top);
    }
}
