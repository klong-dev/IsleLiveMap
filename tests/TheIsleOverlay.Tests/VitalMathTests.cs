using TheIsleOverlay.Core;

namespace TheIsleOverlay.Tests;

public sealed class VitalMathTests
{
    [Fact]
    public void Percent_UsesExactCurrentAndMaximumFirst() =>
        Assert.Equal(25d, VitalMath.Percent(50, 200, 99));

    [Theory]
    [InlineData(0.75, 75)]
    [InlineData(42, 42)]
    [InlineData(900, 100)]
    [InlineData(-10, 0)]
    public void Percent_NormalizesAndClampsFallback(double fallback, double expected) =>
        Assert.Equal(expected, VitalMath.Percent(null, null, fallback));
}
