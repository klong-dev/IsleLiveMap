using TheIsleOverlay.Core;
using TheIsleOverlay.TeamRelay;

namespace TheIsleOverlay.Tests;

public sealed class TeamTelemetryMapperTests
{
    [Fact]
    public void Create_UsesExactVitalsAndCalibratedMapPoint()
    {
        var snapshot = new TelemetrySnapshot
        {
            Source = "IslePilot",
            Success = true,
            ServerOnline = true,
            PlayerOnline = true,
            Player = new PlayerTelemetry
            {
                Server = "DinoVietnam Premium",
                Class = "BP_Pteranodon_C",
                HealthPercent = 1,
                HungerPercent = 1,
                ThirstPercent = 1,
                ExactVitals = new ExactVitals
                {
                    Health = 8.2,
                    MaxHealth = 9.9,
                    Hunger = 2.2,
                    MaxHunger = 3.3,
                    Thirst = 530,
                    MaxThirst = 1000
                },
                Location = new WorldLocation { X = 77761.41, Y = -235882.81 },
                MapLocation = new MapPoint(0.42, 0.31),
                ExactMapHeadingDegrees = 157.37
            }
        };

        var result = TeamTelemetryMapper.Create(snapshot, 4, 270);

        Assert.Equal(4, result.Sequence);
        Assert.Equal("DinoVietnam Premium", result.ServerKey);
        Assert.Equal("gateway", result.MapId);
        Assert.Equal(82.82828282828282, result.HealthPercent!.Value, 8);
        Assert.Equal(66.66666666666666, result.HungerPercent!.Value, 8);
        Assert.Equal(53, result.ThirstPercent);
        Assert.Equal(77761.41, result.WorldX);
        Assert.Equal(-235882.81, result.WorldY);
        Assert.Equal(0.42, result.MapLeft);
        Assert.Equal(0.31, result.MapTop);
        Assert.Equal(157.37, result.HeadingDegrees);
    }

    [Fact]
    public void Create_UsesMovementHeadingAndNormalizedFallbackPercentages()
    {
        var snapshot = new TelemetrySnapshot
        {
            Source = "ERA",
            Success = true,
            ServerOnline = true,
            PlayerOnline = true,
            Player = new PlayerTelemetry
            {
                HealthPercent = 0.8,
                HungerPercent = 45,
                ThirstPercent = 0.25
            }
        };

        var result = TeamTelemetryMapper.Create(snapshot, 1, -20);

        Assert.Equal(80, result.HealthPercent);
        Assert.Equal(45, result.HungerPercent);
        Assert.Equal(25, result.ThirstPercent);
        Assert.Equal(340, result.HeadingDegrees);
    }

    [Fact]
    public void Create_ClearsGameplayFieldsWhenDinosaurIsUnavailable()
    {
        var snapshot = new TelemetrySnapshot
        {
            Source = "IslePilot",
            Success = true,
            ServerOnline = true,
            PlayerOnline = false
        };

        var result = TeamTelemetryMapper.Create(snapshot, 9, 90);

        Assert.Equal(9, result.Sequence);
        Assert.Equal("IslePilot", result.Source);
        Assert.Null(result.ServerKey);
        Assert.Null(result.HealthPercent);
        Assert.Null(result.WorldX);
        Assert.Null(result.MapLeft);
        Assert.Null(result.HeadingDegrees);
    }

    [Fact]
    public void Create_DropsCoordinatePairWhenOneValueIsNotFinite()
    {
        var snapshot = new TelemetrySnapshot
        {
            Success = true,
            ServerOnline = true,
            PlayerOnline = true,
            Player = new PlayerTelemetry
            {
                Location = new WorldLocation { X = 12, Y = double.NaN },
                MapLocation = new MapPoint(0.4, double.PositiveInfinity)
            }
        };

        var result = TeamTelemetryMapper.Create(snapshot, 2);

        Assert.Null(result.WorldX);
        Assert.Null(result.WorldY);
        Assert.Null(result.MapLeft);
        Assert.Null(result.MapTop);
    }

    [Fact]
    public void DefaultEndpoint_UsesProductionRelayDomain()
    {
        Assert.Equal("https://isle-relay.klong.dev/", TeamRelayClient.DefaultBaseUri.AbsoluteUri);
    }
}
