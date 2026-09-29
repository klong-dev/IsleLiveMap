using TheIsleOverlay.Core;
using TheIsleOverlay.IslePilot;
using TheIsleOverlay.LocalTelemetry;

namespace TheIsleOverlay.Tests;

public sealed class IslePilotStatsIsolationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-26T00:00:00Z");
    private static IslePilotOverlayMeDto Baseline() => new()
    {
        SteamId = "player-a", Online = true, HasData = true, Server = "server-a",
        Species = "Triceratops", Health = 80, MaxHealth = 100,
        Nutrition = new() { Protein = 2 }, Prime = new() { Done = 2 }
    };

    [Theory]
    [InlineData("server-b", "Triceratops")]
    [InlineData("server-a", "Tyrannosaurus")]
    public void IdentityTransitionDoesNotInheritOldLiveStatsOrMap(string server, string species)
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyMap(new() { Markers = [new() { Self = true, X = 100, Y = 200 }] }, Now);
        reducer.ApplyLive(new() { HasDino = true, Health = 12, Position = new() { X = 300, Y = 400 } }, Now);
        reducer.ApplyMe(new() { SteamId = "player-a", Online = true, HasData = true, Server = server, Species = species, Health = 900 }, Now.AddSeconds(1));
        var snapshot = reducer.BuildSnapshot(Now.AddSeconds(1));
        Assert.Equal(900, snapshot.Player?.ExactVitals?.Health);
        Assert.Null(snapshot.Player?.ExactVitals?.MaxHealth);
        Assert.Null(snapshot.Player?.Nutrition);
        Assert.Null(snapshot.Player?.Prime);
        Assert.Null(snapshot.Player?.Location);
        Assert.Null(snapshot.Map);
    }

    [Fact]
    public void DespawnThenPartialRespawnDoesNotRevivePreviousDinoStats()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyLive(new() { HasDino = true, Health = 12 }, Now);
        reducer.ApplyLive(new() { HasDino = false }, Now.AddSeconds(1));
        reducer.ApplyLive(new() { HasDino = true, Health = 5 }, Now.AddSeconds(2));
        var player = reducer.BuildSnapshot(Now.AddSeconds(2)).Player;
        Assert.Equal(5, player?.ExactVitals?.Health);
        Assert.Null(player?.ExactVitals?.MaxHealth);
        Assert.Null(player?.Class);
        Assert.Null(player?.Prime);
    }

    [Fact]
    public void LiveFrameForAnotherAccountIsIgnored()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyLive(new() { SteamId = "player-b", HasDino = true, Health = 999 }, Now);
        var player = reducer.BuildSnapshot(Now).Player;
        Assert.Equal("player-a", player?.SteamId);
        Assert.Equal(80, player?.ExactVitals?.Health);
    }

    [Fact]
    public void AgentSpeciesCannotRelabelIslePilotStats()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        var merged = LocalPositionSnapshotMerger.Merge(reducer.BuildSnapshot(Now), null, Now,
            remotePlayers: [], verifiedLocalSpeciesId: "tyrannosaurus");
        Assert.Equal("Triceratops", merged.Player?.Class);
        Assert.Equal(80, merged.Player?.ExactVitals?.Health);
    }

    [Fact]
    public void MapRequestStartedBeforeServerChangeCannotRestoreOldMap()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        var generation = reducer.IdentityGeneration;
        reducer.ApplyMe(Baseline() with { Server = "server-b" }, Now.AddSeconds(1));
        var oldMap = new IslePilotOverlayMapDto { Markers = [new() { Self = true, X = 100, Y = 200 }] };
        reducer.ApplyMap(oldMap, Now.AddSeconds(2), generation);
        Assert.Null(reducer.BuildSnapshot(Now.AddSeconds(2)).Map);
        reducer.ApplyMap(oldMap, Now.AddSeconds(3), reducer.IdentityGeneration);
        Assert.NotNull(reducer.BuildSnapshot(Now.AddSeconds(3)).Map);
    }

    [Fact]
    public void OlderIdentityCannotRollBackTheCurrentServer()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline() with { Server = "server-b" }, Now.AddSeconds(1));
        reducer.ApplyMe(Baseline(), Now);
        Assert.Equal("server-b", reducer.BuildSnapshot(Now.AddSeconds(1)).Player?.Server);
    }
}
