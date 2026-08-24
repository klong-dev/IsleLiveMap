using TheIsleOverlay.Core;
using TheIsleOverlay.LocalTelemetry;

namespace TheIsleOverlay.Tests;

public sealed class LocalPositionSnapshotMergerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-24T00:00:00Z");

    [Fact]
    public void Merge_UsesLocalPositionWhenRemoteServerIsUnsupported()
    {
        var remote = new TelemetrySnapshot
        {
            Source = "ISLEPILOT",
            SessionState = TelemetrySessionState.UnsupportedServer,
            StatusMessage = "Unsupported"
        };
        var local = Observation(123_456.78d, -234_567.89d, 20_000d, 157.37d);

        var merged = LocalPositionSnapshotMerger.Merge(remote, local, Now);

        Assert.True(merged.Success);
        Assert.True(merged.PlayerOnline);
        Assert.Equal(TelemetrySessionState.Live, merged.SessionState);
        Assert.Equal(123_456.78d, merged.Player?.Location?.X);
        Assert.Equal(-234_567.89d, merged.Player?.Location?.Y);
        Assert.Equal(247.37d, merged.Player!.ExactMapHeadingDegrees!.Value, precision: 6);
        Assert.Equal("171.232.64.234:7777", merged.Player?.Server);
    }

    [Fact]
    public void Merge_PreservesRemoteVitalsAndOverridesOnlyPositionFields()
    {
        var exactVitals = new ExactVitals { Health = 825, MaxHealth = 1_000 };
        var remote = new TelemetrySnapshot
        {
            Source = "ERA",
            Success = true,
            ServerOnline = true,
            PlayerOnline = true,
            Player = new PlayerTelemetry
            {
                Name = "Player",
                Class = "Pteranodon",
                Server = "ERA",
                ExactVitals = exactVitals,
                Location = new WorldLocation { X = 1, Y = 2, Z = 3 }
            },
            SessionState = TelemetrySessionState.Live
        };

        var merged = LocalPositionSnapshotMerger.Merge(
            remote,
            Observation(100, 200, 300, 45),
            Now);

        Assert.Same(exactVitals, merged.Player?.ExactVitals);
        Assert.Equal("Player", merged.Player?.Name);
        Assert.Equal("ERA", merged.Player?.Server);
        Assert.Equal(100, merged.Player?.Location?.X);
        Assert.Null(merged.Player?.MapLocation);
        Assert.Equal(135, merged.Player?.ExactMapHeadingDegrees);
    }

    [Fact]
    public void Merge_IgnoresExpiredLocalPosition()
    {
        var remote = new TelemetrySnapshot
        {
            Success = true,
            ServerOnline = true,
            PlayerOnline = true,
            Player = new PlayerTelemetry
            {
                Location = new WorldLocation { X = 10, Y = 20 }
            },
            SessionState = TelemetrySessionState.Live
        };

        var merged = LocalPositionSnapshotMerger.Merge(
            remote,
            Observation(100, 200, 300, 45) with
            {
                ObservedAt = Now - LocalPositionSnapshotMerger.LocalFreshness - TimeSpan.FromMilliseconds(1)
            },
            Now);

        Assert.Same(remote, merged);
    }

    private static LocalMovementObservation Observation(
        double x,
        double y,
        double z,
        double yaw) => new(
        Now,
        new UnrealMovementCandidate(
            x,
            y,
            z,
            yaw,
            1f,
            64,
            380,
            26),
        "171.232.64.234:7777");
}
