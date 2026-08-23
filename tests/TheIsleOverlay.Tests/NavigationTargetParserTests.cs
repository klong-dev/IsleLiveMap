using TheIsleOverlay.Core;

namespace TheIsleOverlay.Tests;

public sealed class NavigationTargetParserTests
{
    [Theory]
    [InlineData("-3,007.455, -4,606.069,  44,728.061", -4606.069, -3007.455, 44728.061)]
    [InlineData("-3007.455, -4606.069, 44728.061", -4606.069, -3007.455, 44728.061)]
    [InlineData("Lat: -3,007.455 Long: -4,606.069 Alt: 44,728.061", -4606.069, -3007.455, 44728.061)]
    [InlineData("X=-3,007.455 Y=-4,606.069 Z=44,728.061", -3007.455, -4606.069, 44728.061)]
    [InlineData("100, 200, 300", 200, 100, 300)]
    public void TryParse_AcceptsTheIsleCoordinateFormats(
        string input,
        double x,
        double y,
        double z)
    {
        var parsed = NavigationTargetParser.TryParse(input, out var target);

        Assert.True(parsed);
        Assert.Equal(x, target.X, precision: 6);
        Assert.Equal(y, target.Y, precision: 6);
        Assert.Equal(z, target.Z!.Value, precision: 6);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("100, 200")]
    [InlineData("100, 200, 300, 400")]
    [InlineData("x, y, z")]
    public void TryParse_RejectsIncompleteOrAmbiguousInput(string? input)
    {
        Assert.False(NavigationTargetParser.TryParse(input, out _));
    }
}
