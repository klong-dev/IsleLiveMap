using TheIsleOverlay.Core;

namespace TheIsleOverlay.Tests;

public sealed class CreatureMarkerLabelFormatterTests
{
    [Theory]
    [InlineData("T-Rex", 2_300, "T-Rex 2.3T")]
    [InlineData("Trice", 200, "Trice 200K")]
    [InlineData("Stego", 6_000, "Stego 6T")]
    [InlineData("Fish", 12.44, "Fish 12.4K")]
    public void Format_UsesRequestedCompactKilogramAndTonNotation(
        string species,
        double massKg,
        string expected)
    {
        Assert.Equal(expected, CreatureMarkerLabelFormatter.Format(species, massKg));
    }

    [Fact]
    public void Format_DoesNotInventMissingMass()
    {
        Assert.Equal("Cerato", CreatureMarkerLabelFormatter.Format("Cerato", null));
    }
}
