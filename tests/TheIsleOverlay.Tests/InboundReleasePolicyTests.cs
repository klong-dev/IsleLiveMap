using TheIsleOverlay.Core;
using TheIsleOverlay.LocalTelemetry;
using TheIsleOverlay.TeamRelay;
namespace TheIsleOverlay.Tests;
public sealed class InboundReleasePolicyTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("0", false)]
    [InlineData("1", true)]
    public void ReplacementRequiresExplicitOptIn(string? value, bool expected)
        => Assert.Equal(expected, LocalVitalsFeature.IsEnabledForReplacement(value));

    [Fact]
    public void TeamStatsUseSameCurrentAndRetainedMaxAsOverlay()
    {
        var update = TeamTelemetryMapper.Create(new TelemetrySnapshot
        {
            Success = true, ServerOnline = true, PlayerOnline = true,
            Player = new PlayerTelemetry
            {
                InboundStatsExperimental = true,
                ExactVitalsSource = LocalVitalsFeature.SourceName,
                ExactVitals = new ExactVitals { Health = 50, Hunger = 20, Thirst = 840 },
                InboundStatsLastKnown = new ExactVitals { MaxHealth = 100, MaxHunger = 40, MaxThirst = 1000 }
            }
        }, 1);
        Assert.Equal(50, update.HealthPercent);
        Assert.Equal(50, update.HungerPercent);
        Assert.Equal(84, update.ThirstPercent);
    }
}
