using TheIsleOverlay.Core;

namespace TheIsleOverlay.Tests;

public sealed class MapHeadingTests
{
    [Theory]
    [InlineData(0, 90)]
    [InlineData(90, 180)]
    [InlineData(180, 270)]
    [InlineData(270, 0)]
    [InlineData(-90, 0)]
    public void FromUnrealYaw_ConvertsEastBasedYawToNorthBasedMapAngle(double yaw, double expected) =>
        Assert.Equal(expected, MapHeading.FromUnrealYaw(yaw), precision: 8);
}
