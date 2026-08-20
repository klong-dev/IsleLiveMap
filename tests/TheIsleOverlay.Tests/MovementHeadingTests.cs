using TheIsleOverlay.Core;

namespace TheIsleOverlay.Tests;

public sealed class MovementHeadingTests
{
    [Theory]
    [InlineData(0, -1000, 0)]
    [InlineData(1000, 0, 90)]
    [InlineData(0, 1000, 180)]
    [InlineData(-1000, 0, 270)]
    public void TryCalculate_MapsWorldMovementToClockwiseMapHeading(double x, double y, double expected)
    {
        var previous = new WorldLocation { X = 0, Y = 0 };
        var current = new WorldLocation { X = x, Y = y };

        var valid = MovementHeading.TryCalculate(previous, current, out var heading);

        Assert.True(valid);
        Assert.Equal(expected, heading, precision: 8);
    }

    [Fact]
    public void TryCalculate_IgnoresStationaryNoise()
    {
        var previous = new WorldLocation { X = 10_000, Y = 10_000 };
        var current = new WorldLocation { X = 10_020, Y = 10_010 };

        Assert.False(MovementHeading.TryCalculate(previous, current, out _));
    }

    [Fact]
    public void TryCalculate_IgnoresTeleportSizedDelta()
    {
        var previous = new WorldLocation { X = 0, Y = 0 };
        var current = new WorldLocation { X = 500_000, Y = 500_000 };

        Assert.False(MovementHeading.TryCalculate(previous, current, out _));
    }

    [Fact]
    public void Smooth_TakesShortestPathAcrossNorth()
    {
        var heading = MovementHeading.Smooth(350, 10, 0.5);

        Assert.Equal(0, heading, precision: 8);
    }
}
