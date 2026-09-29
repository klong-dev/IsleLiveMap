using TheIsleOverlay.Core;
using TheIsleOverlay.LocalTelemetry;
using TheIsleOverlay.IslePilot;
namespace TheIsleOverlay.Tests;

public sealed class IslePilotInboundFallbackTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-29T10:00:00Z");
    private static PrimeTelemetry Prime => new() { Done = 1, Required = 2,
        Quests = [new() { Name = "Travel", Done = true }, new() { Name = "Eat", Done = false }] };
    private static TelemetrySnapshot Provider() => new()
    {
        Source = "IslePilot", Success = true, ServerOnline = true, PlayerOnline = true,
        SessionState = TelemetrySessionState.Live, UpdatedAt = Now,
        Player = new() { Class = "Triceratops", ServerEndpoint = "server:7777",
            ExactVitalsSource = "IslePilotOverlayV2", ExactVitals = new() { Health = 80, MaxHealth = 100, Growth = .73 },
            Prime = Prime, Nutrition = new() { Carb = 2 } }
    };
    private static LocalMovementObservation Local() => LocalMovementObservation.VitalsOnly(
        new(Now, new ExactVitals { Health = 10, MaxHealth = 100 }, 42), "server:7777");
    private static TelemetrySnapshot Merge(TelemetrySnapshot? provider, LocalMovementObservation? local = null, DateTimeOffset? now = null)
        => LocalPositionSnapshotMerger.Merge(provider, local ?? Local(), now ?? Now,
            allowLocalVitals: true, enableIslePilotFallback: true);

    [Fact]
    public void HealthyProviderWinsAndPrimeAndGrowthAreUntouched()
    {
        var provider = Provider(); var merged = Merge(provider);
        Assert.Same(provider.Player!.ExactVitals, merged.Player!.ExactVitals);
        Assert.Same(provider.Player.Prime, merged.Player.Prime);
        Assert.Equal(.73, merged.Player.ExactVitals!.Growth);
        Assert.False(merged.Player.InboundStatsFallback);
    }
    [Theory]
    [InlineData(TelemetrySessionState.Reconnecting)]
    [InlineData(TelemetrySessionState.Stale)]
    [InlineData(TelemetrySessionState.AuthenticationRequired)]
    public void OutageUsesInboundWithoutErasingPrime_RecoveryRestoresProvider(TelemetrySessionState state)
    {
        var provider = Provider();
        var fallback = Merge(provider with { SessionState = state, LiveDataStale = true });
        Assert.Equal(10, fallback.Player!.HealthPercent);
        Assert.Equal("LocalIris", fallback.Player.ExactVitalsSource);
        Assert.Same(provider.Player!.Prime, fallback.Player.Prime);
        Assert.Equal(2, fallback.Player.Prime!.Quests.Count);
        Assert.True(fallback.Player.PrimeDataStale);
        Assert.True(fallback.Player.InboundStatsFallback);
        Assert.Equal(state == TelemetrySessionState.AuthenticationRequired, fallback.Player.ProviderAuthenticationRequired);
        var recovered = Merge(provider with { Player = provider.Player with { Prime = Prime with { Done = 2 } } });
        Assert.Equal(80, recovered.Player!.ExactVitals!.Health);
        Assert.Equal(2, recovered.Player.Prime!.Done);
        Assert.False(recovered.Player.PrimeDataStale);
        Assert.False(recovered.Player.InboundStatsFallback);
    }
    [Fact]
    public void FreshPrimeWithoutStats_DoesNotBlockInbound_OrBecomeStale()
    {
        var provider = Provider(); provider = provider with { Player = provider.Player! with { ExactVitals = new() { Growth = .73 } } };
        var merged = Merge(provider);
        Assert.Equal(10, merged.Player!.HealthPercent);
        Assert.Same(provider.Player.Prime, merged.Player.Prime);
        Assert.False(merged.Player.PrimeDataStale);
    }
    [Fact]
    public void OtherServerCannotCarrySpeciesOrPrimeIntoFallback()
    {
        var provider = Provider(); provider = provider with { Player = provider.Player! with { ServerEndpoint = "other:7777" } };
        var merged = Merge(provider);
        Assert.Equal(10, merged.Player!.HealthPercent);
        Assert.Null(merged.Player.Prime); Assert.Null(merged.Player.Class);
    }
    [Fact]
    public void UnsupportedServerUsesRetainedInboundPairsWithoutInventingPrime()
    {
        var local = LocalMovementObservation.VitalsOnly(new(Now, new(), 42)
        { ExperimentalEvidence = [new("flow",42,"HealthCandidate",45,Now.AddSeconds(-8),1,1,1391,"full"),
            new("flow",42,"MaxHealthCandidate",100,Now.AddSeconds(-8),1,1,1391,"full")] }, "server:7777");
        var merged = Merge(Provider() with { SessionState = TelemetrySessionState.UnsupportedServer, PlayerOnline = false }, local);
        Assert.Equal(45d, merged.Player!.InboundStatsLastKnown!.Health);
        Assert.Null(merged.Player.Prime);
    }
    [Fact]
    public void SilentProviderCannotStayPrimaryBecauseGpsKeepsUpdating()
    {
        var provider = Provider() with { UpdatedAt = Now.AddSeconds(-30) };
        var merged = Merge(provider);
        Assert.Equal(10, merged.Player!.HealthPercent);
        Assert.True(merged.Player.PrimeDataStale);
    }
    [Fact]
    public void PrimeRefreshDoesNotRenewStats_AndLiveStatsDoNotRenewPrime()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(new() { HasData = true, Online = true, Health = 80, MaxHealth = 100,
            Prime = new() { Done = 1 } }, Now);
        reducer.ApplyMe(new() { Prime = new() { Done = 2 } }, Now.AddSeconds(20));
        var snapshot = reducer.BuildSnapshot(Now.AddSeconds(20));
        Assert.Equal(Now, snapshot.ProviderStatsObservedAt);
        Assert.Equal(Now.AddSeconds(20), snapshot.ProviderPrimeObservedAt);
        reducer.ApplyLive(new() { HasDino = true, Health = 70 }, Now.AddSeconds(60));
        snapshot = reducer.BuildSnapshot(Now.AddSeconds(60));
        Assert.Equal(Now.AddSeconds(20), snapshot.ProviderPrimeObservedAt);
        var merged = Merge(snapshot, now: Now.AddSeconds(60));
        Assert.True(merged.Player!.PrimeDataStale);
        Assert.Equal(70, merged.Player.ExactVitals!.Health);
    }
    [Fact]
    public void PrimeCountersOnlyUpdateKeepsQuests_ExplicitEmptyClears()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(new() { HasData = true, Online = true, Prime = new() { Done = 1, Required = 2,
            Quests = [new() { Name = "Travel", Done = true }, new() { Name = "Eat", Done = false }] } }, Now);
        reducer.ApplyMe(new() { Prime = new() { Done = 2 } }, Now.AddSeconds(1));
        var prime = reducer.BuildSnapshot(Now.AddSeconds(1)).Player!.Prime!;
        Assert.Equal(2, prime.Done); Assert.Equal(2, prime.Required); Assert.Equal(2, prime.Quests.Count);
        reducer.ApplyMe(new() { Prime = new() { Quests = [] } }, Now.AddSeconds(2));
        Assert.Empty(reducer.BuildSnapshot(Now.AddSeconds(2)).Player!.Prime!.Quests);
    }
    [Fact]
    public void PartialMeAndLiveDoNotDropPrimeButRespawnClearsOldQuests()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(new() { HasData = true, Online = true, Server = "server", Species = "Triceratops",
            Prime = new() { Done = 1, Required = 2 }, Health = 80, MaxHealth = 100 }, Now);
        reducer.ApplyMe(new() { Health = 70 }, Now.AddSeconds(1));
        reducer.ApplyLive(new() { HasDino = true, Stamina = 40 }, Now.AddSeconds(2));
        Assert.Equal(1, reducer.BuildSnapshot(Now.AddSeconds(2)).Player!.Prime!.Done);
        reducer.ApplyLive(new() { HasDino = false }, Now.AddSeconds(3));
        reducer.ApplyLive(new() { HasDino = true, Health = 5 }, Now.AddSeconds(4));
        Assert.Null(reducer.BuildSnapshot(Now.AddSeconds(4)).Player!.Prime);
    }
}
