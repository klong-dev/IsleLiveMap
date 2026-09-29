using TheIsleOverlay.Core;
using TheIsleOverlay.LocalTelemetry;
namespace TheIsleOverlay.Tests;

public sealed class InboundStatsReplacementTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-29T03:19:00Z");
    [Fact]
    public void Live1391Packet_ReachesExistingMapStatsThroughAccumulatorAndMerger()
    {
        var bytes = Convert.FromBase64String("AABouiXU/////9uAAAgniAEAAABCNiCIKwgBAIDKN4YEIGcDAhReDD/9Lbapi1BsUxehD7U3RB9qb4hSsX8LpWL/FtqKuxC0FXchaCvuQtBW3IWgBJVRQQkqo4ISVEYFJaiMCkpQGRWUoDIqKEFlVFCCyqiguhuEPHU3CHlim7oKxTZ1FYpt6ioU29RVaLgQt9BwIW6hVOzfQqnYv4Vim7oKxTZ1FYpt6iIU29RFKLapi1BsUxeh2KauQrFNXYVim7oKxTZ1FQICHQChIOeBmPkHuAFCCICAQxiZcErUEj01iFPkN6I5AuqwXi+qAlBg");
        var accumulator = new InboundStatsAccumulator();
        Assert.True(accumulator.TryTrack(bytes, Now, "game-session/server/port", out var observation));
        var result = LocalPositionSnapshotMerger.Merge(Provider(), LocalMovementObservation.VitalsOnly(observation),
            Now, allowLocalVitals: true, replaceIslePilotStats: true);
        Assert.Equal(263880UL, result.Player?.InboundStatsOwnerHandle);
        Assert.Equal(1.612650230526924, result.Player!.GrowthPercent!.Value, 6);
        Assert.Equal(87.32586669921875, result.Player.ExactVitals!.Health);
        Assert.Null(result.Player.ExactVitals.Thirst);
        var expired = LocalPositionSnapshotMerger.Merge(Provider(), LocalMovementObservation.VitalsOnly(observation),
            Now.AddSeconds(4), allowLocalVitals: true, replaceIslePilotStats: true);
        Assert.Null(expired.Player?.ExactVitals);
    }
    private static TelemetrySnapshot Provider(string source = "IslePilotOverlayV2") => new()
    {
        Source = "ISLEPILOT", SessionState = TelemetrySessionState.Live,
        Success = true, ServerOnline = true, PlayerOnline = true,
        Player = new PlayerTelemetry
        {
            Name = "player", Class = "old-species", ExactVitalsSource = source,
            ExactVitals = new ExactVitals { Health = 99, MaxHealth = 100, Growth = .9 },
            HealthPercent = 99, GrowthPercent = 90
        }
    };
    private static LocalMovementObservation Local(DateTimeOffset at) => LocalMovementObservation.VitalsOnly(
        new LocalDinosaurVitalsObservation(at, new ExactVitals { Health = 20, MaxHealth = 100 }, 42), "server:7777");

    [Fact]
    public void InMapReplacement_TakesInboundAndDoesNotBorrowProviderGrowthOrSpecies()
    {
        var result = LocalPositionSnapshotMerger.Merge(Provider(), Local(Now), Now,
            allowLocalVitals: true, replaceIslePilotStats: true);
        Assert.Equal(20, result.Player?.HealthPercent);
        Assert.Equal("LocalIris", result.Player?.ExactVitalsSource);
        Assert.Null(result.Player?.GrowthPercent);
        Assert.Null(result.Player?.ExactVitals?.Growth);
        Assert.Null(result.Player?.Class);
        Assert.Equal("player", result.Player?.Name);
    }
    [Fact]
    public void ExpiredInbound_DoesNotFallBackToIslePilotStats()
    {
        var result = LocalPositionSnapshotMerger.Merge(Provider(), Local(Now.AddSeconds(-10)), Now,
            allowLocalVitals: true, replaceIslePilotStats: true);
        Assert.Null(result.Player?.ExactVitals);
        Assert.Null(result.Player?.GrowthPercent);
        Assert.Null(result.Player?.HealthPercent);
    }
    [Fact]
    public void DefaultAndOtherProviders_KeepExistingPolicy()
    {
        var unchanged = LocalPositionSnapshotMerger.Merge(Provider(), Local(Now), Now, allowLocalVitals: true);
        Assert.Equal(99, unchanged.Player?.HealthPercent);
        var other = LocalPositionSnapshotMerger.Merge(Provider("GachaOfficialWebSocket"), Local(Now), Now,
            allowLocalVitals: true, replaceIslePilotStats: true);
        Assert.Equal(99, other.Player?.HealthPercent);
    }
    [Fact]
    public void TrialRawHealth_RendersInExistingPanelWithoutInventingPercentage()
    {
        var evidence = new VitalsFieldEvidence("session", 42, "HealthCandidate", 95, Now, 1, 435, 296, "tail-A-A-B");
        var local = LocalMovementObservation.VitalsOnly(new LocalDinosaurVitalsObservation(Now, new ExactVitals(), 42)
        { ExperimentalEvidence = [evidence] });
        var result = LocalPositionSnapshotMerger.Merge(Provider(), local, Now, allowLocalVitals: true, replaceIslePilotStats: true);
        Assert.Equal(95d, result.Player?.ExactVitals?.Health);
        Assert.Null(result.Player?.HealthPercent);
        Assert.True(result.Player?.InboundStatsExperimental);
        Assert.Equal(42UL, result.Player?.InboundStatsOwnerHandle);
    }
    [Fact]
    public void NewStamina_DoesNotRefreshOldHealthOrGrowth()
    {
        var local = LocalMovementObservation.VitalsOnly(new LocalDinosaurVitalsObservation(Now, new ExactVitals(), 42)
        { ExperimentalEvidence = [
            new("session", 42, "HealthCandidate", 95, Now.AddSeconds(-5), 1, 435, 296, "tail-A-A-B"),
            new("session", 42, "GrowthCandidate", .8, Now.AddSeconds(-5), 1, 800, 1482, "measured-1482"),
            new("session", 42, "StaminaCandidate", 400, Now, 2, 435, 296, "tail-A-B-A-C")] });
        var result = LocalPositionSnapshotMerger.Merge(Provider(), local, Now, allowLocalVitals: true, replaceIslePilotStats: true);
        Assert.Null(result.Player?.ExactVitals?.Health);
        Assert.Null(result.Player?.ExactVitals?.Growth);
        Assert.Equal(400d, result.Player?.ExactVitals?.Stamina);
    }
    [Fact]
    public void AuthenticationRemainsRequired()
    {
        var result = LocalPositionSnapshotMerger.Merge(Provider() with { SessionState = TelemetrySessionState.AuthenticationRequired },
            Local(Now), Now, allowLocalVitals: true, replaceIslePilotStats: true);
        Assert.Equal(TelemetrySessionState.AuthenticationRequired, result.SessionState);
        Assert.Null(result.Player?.ExactVitals);
    }
}
