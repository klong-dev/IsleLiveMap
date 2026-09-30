using TheIsleOverlay.Core;
using TheIsleOverlay.LocalTelemetry;
namespace TheIsleOverlay.Tests;

public sealed class OriginInboundStatsFusionTests
{
    private static readonly DateTimeOffset T = DateTimeOffset.Parse("2026-09-29T10:00:00Z");
    private static TelemetrySnapshot Api(int seconds = 0, double health = 100, string scope = "main/1") => new()
    {
        Source = "ORIGIN x5", PlayerOnline = true, Success = true, ServerOnline = true,
        ProviderStatsRequestedAt = T.AddSeconds(seconds), ProviderStatsScope = scope, SessionState = TelemetrySessionState.Live,
        Player = new() { Class = "Rex", Server = "Main Origin", ExactVitalsSource = "OriginDashboard",
            ExactVitals = new() { Health = health, MaxHealth = 100, Stamina = 90, MaxStamina = 100,
                Hunger = 80, MaxHunger = 100, Thirst = 70, MaxThirst = 1000, Growth = .5 },
            Prime = new() { Done = 1, Required = 3 } }
    };
    private static LocalMovementObservation Local(int seconds, double health, ulong owner = 42, string endpoint = "game:7777", string flow = "game-session") =>
        LocalMovementObservation.VitalsOnly(new(T.AddSeconds(seconds), new(), owner)
        { ExperimentalEvidence = [new(flow, owner, "HealthCandidate", health, T.AddSeconds(seconds), seconds, 200, 356, "fixture")] }, endpoint);

    [Fact]
    public void CompleteResponseForNewProviderScopeSeedsImmediately()
    {
        var f = new OriginInboundStatsFusion(); f.ObserveProvider(Api(), T);
        f.ObserveProvider(Api(20, 30, "voice/2"), T.AddSeconds(25));
        var p = f.Build(T.AddSeconds(25)).Player!;
        Assert.Equal(30, p.ExactVitals!.Health);
        Assert.Equal(100, p.ExactVitals.MaxHealth);
        Assert.Equal(T.AddSeconds(20), p.StatsFieldTimes!["Health"]);
    }
    [Fact]
    public void FirstCompleteBaselineAfterExplicitNoDinoDoesNotWaitAnotherPoll()
    {
        var f = new OriginInboundStatsFusion(); f.ObserveProvider(Api(), T);
        f.ObserveProvider(new() { ProviderStatsScope = "none/2", ProviderStatsReset = true }, T.AddSeconds(10));
        f.ObserveProvider(Api(20, 25, "voice/3"), T.AddSeconds(25));
        Assert.Equal(25, f.Build(T.AddSeconds(25)).Player!.ExactVitals!.Health);
    }
    [Fact]
    public void PresenceDoesNotRefreshProtocolMaximumOrOverrideObservedApiMaximum()
    {
        var f = new OriginInboundStatsFusion();
        var snapshot = Api();
        f.ObserveProvider(snapshot with { Player = snapshot.Player! with
        { ExactVitals = snapshot.Player.ExactVitals! with { MaxThirst = 1200 } } }, T);
        var at = T.AddSeconds(2);
        var local = LocalMovementObservation.VitalsOnly(new(at, new(), 42)
        { ExperimentalEvidence = [new("flow", 42, "ThirstCandidate", 840, at, 1, 1, 395, "survival")] }, "game:7777");
        f.ObserveLocal(local, at);
        f.ObserveLocal(local with { DinosaurVitals = local.DinosaurVitals!.Value with { ObservedAt = T.AddSeconds(10) } }, T.AddSeconds(10));
        var p = f.Build(T.AddSeconds(10)).Player!;
        Assert.Equal(1200, p.ExactVitals!.MaxThirst);
        Assert.Equal(70, p.ThirstPercent);
        Assert.Equal(T, p.StatsFieldTimes!["MaxThirst"]);
        Assert.Equal(at, p.StatsFieldTimes["Thirst"]);
    }
    [Fact]
    public void PauseGrowthForMinutesRetainsMaxAndPrimeWhileInboundChangesCurrent()
    {
        var f = new OriginInboundStatsFusion(); f.ObserveProvider(Api(), T);
        f.ObserveLocal(Local(300, 63), T.AddSeconds(300));
        var p = f.Build(T.AddSeconds(600)).Player!;
        Assert.Equal(63, p.HealthPercent); Assert.Equal(100, p.ExactVitals!.MaxHealth);
        Assert.Equal(T, p.StatsFieldTimes!["MaxHealth"]);
        Assert.Equal(.5, p.ExactVitals.Growth); Assert.Equal(1, p.Prime!.Done);
    }
    [Fact]
    public void LateApiMaximumOverridesOnlyProtocolDefault_NotNewerMeasuredMaximum()
    {
        var f = new OriginInboundStatsFusion(); var at = T.AddSeconds(5);
        f.ObserveLocal(LocalMovementObservation.VitalsOnly(new(at, new(), 42)
        { ExperimentalEvidence = [new("flow", 42, "ThirstCandidate", 840, at, 1, 1, 395, "survival")] }, "game:7777"), at);
        var api = Api(1); f.ObserveProvider(api with { Player = api.Player! with
            { ExactVitals = api.Player.ExactVitals! with { MaxThirst = 1200 } } }, T.AddSeconds(9));
        Assert.Equal(1200, f.Build(T.AddSeconds(9)).Player!.ExactVitals!.MaxThirst);
        Assert.Equal(840, f.Build(T.AddSeconds(9)).Player!.ExactVitals!.Thirst);
    }
    [Fact]
    public void TicksAndPrimeRepublishDoNotMakeHoursOldStateLive()
    {
        var f = new OriginInboundStatsFusion(); f.ObserveProvider(Api(), T);
        Assert.False(f.Build(T.AddSeconds(20)).LiveDataStale);
        f.ObserveProvider(Api(), T.AddHours(5));
        var old = f.Build(T.AddHours(5));
        Assert.True(old.LiveDataStale);
        Assert.Equal(TelemetrySessionState.Stale, old.SessionState);
        Assert.Equal(100, old.Player!.ExactVitals!.MaxHealth);
        Assert.Equal(T, old.Player.StatsFieldTimes!["MaxHealth"]);
        f.ObserveLocal(Local(18001, 63), T.AddSeconds(18001));
        Assert.False(f.Build(T.AddSeconds(18001)).LiveDataStale);
    }
    [Fact]
    public void EmptyDiscoveryDoesNotDelayFirstCompleteBaseline()
    {
        var f = new OriginInboundStatsFusion();
        f.ObserveProvider(new() { Source = "ORIGIN x5", ProviderStatsScope = "none/0" }, T);
        f.ObserveProvider(Api(1), T.AddSeconds(5));
        Assert.Equal(100, f.Build(T.AddSeconds(5)).Player!.ExactVitals!.MaxHealth);
    }
    [Fact]
    public void InboundCurrentUsesApiMaxEvenAfterTwentySecondsAndApiFailure()
    {
        var f = new OriginInboundStatsFusion();
        f.ObserveProvider(Api(), T); f.ObserveLocal(Local(30, 45), T.AddSeconds(30));
        f.ObserveProvider(Api() with { LiveDataStale = true, SessionState = TelemetrySessionState.Stale }, T.AddSeconds(40));
        var p = f.Build(T.AddSeconds(100)).Player!;
        Assert.Equal(45, p.ExactVitals!.Health); Assert.Equal(100, p.ExactVitals.MaxHealth);
        Assert.Equal(45, p.HealthPercent); Assert.Equal(1, p.Prime!.Done); Assert.True(p.PrimeDataStale);
        Assert.Equal(T, p.StatsFieldTimes!["MaxHealth"]); Assert.Equal(T.AddSeconds(30), p.StatsFieldTimes["Health"]);
    }
    [Fact]
    public void SlowApiCannotOverwriteInboundAfterRequestStart_ButFillsMissingMax()
    {
        var f = new OriginInboundStatsFusion();
        f.ObserveLocal(Local(5, 40), T.AddSeconds(5));
        f.ObserveProvider(Api(1, 99), T.AddSeconds(9));
        var p = f.Build(T.AddSeconds(9)).Player!;
        Assert.Equal(40, p.ExactVitals!.Health); Assert.Equal(100, p.ExactVitals.MaxHealth);
        Assert.Equal("Inbound", p.StatsFieldSources!["Health"]); Assert.Equal("Origin", p.StatsFieldSources["MaxHealth"]);
        f.ObserveProvider(Api(20, 80), T.AddSeconds(22));
        Assert.Equal(80, f.Build(T.AddSeconds(22)).Player!.ExactVitals!.Health);
    }
    [Fact]
    public void ActorChangeRejectsInflightAndCachedApiUntilNewRequest()
    {
        var f = new OriginInboundStatsFusion();
        f.ObserveLocal(Local(0, 95), T); f.ObserveProvider(Api(1), T.AddSeconds(2));
        f.ObserveLocal(Local(10, 5, 43), T.AddSeconds(10).AddMilliseconds(50));
        f.ObserveProvider(Api(5), T.AddSeconds(12));
        var p = f.Build(T.AddSeconds(12)).Player!;
        Assert.Equal(5, p.ExactVitals!.Health); Assert.Null(p.ExactVitals.MaxHealth); Assert.Null(p.Prime);
        f.ObserveProvider(Api(20, 6), T.AddSeconds(21));
        Assert.Equal(100, f.Build(T.AddSeconds(21)).Player!.ExactVitals!.MaxHealth);
    }
    [Theory]
    [InlineData("other:7777", "game-session")]
    [InlineData("game:7777", "new-game-session")]
    public void EndpointOrGameGenerationChangeClearsBaseline(string endpoint, string flow)
    {
        var f = new OriginInboundStatsFusion(); f.ObserveLocal(Local(0, 95), T); f.ObserveProvider(Api(1), T.AddSeconds(2));
        f.ObserveLocal(Local(10, 10, endpoint: endpoint, flow: flow), T.AddSeconds(10));
        Assert.Null(f.Build(T.AddSeconds(10)).Player!.ExactVitals!.MaxHealth);
    }
    [Fact]
    public void PartialApiDoesNotSeedUntilFourPairsAreComplete_ThenPartialUpdatesKeepMissingFields()
    {
        var f = new OriginInboundStatsFusion(); var full = Api();
        f.ObserveProvider(full with { Player = full.Player! with { ExactVitals = new() { Health = 50, MaxHealth = 100 } } }, T);
        Assert.Null(f.Build(T).Player);
        f.ObserveProvider(full, T);
        var delta = Api(20); f.ObserveProvider(delta with { Player = delta.Player! with { ExactVitals = new() { MaxHealth = 120 } } }, T.AddSeconds(22));
        var p = f.Build(T.AddSeconds(22)).Player!;
        Assert.Equal(100, p.ExactVitals!.Health); Assert.Equal(120, p.ExactVitals.MaxHealth);
        Assert.Equal(100, p.ExactVitals.MaxHunger);
    }
    [Fact]
    public void LateOldGenerationCannotClearNewState_ZeroCurrentIsAccepted()
    {
        var f = new OriginInboundStatsFusion();
        f.ObserveLocal(Local(0, 90), T); f.ObserveProvider(Api(1), T.AddSeconds(2));
        f.ObserveLocal(Local(10, 0, 43), T.AddSeconds(10));
        f.ObserveProvider(Api(3, scope: "main/old"), T.AddSeconds(12));
        Assert.Equal(0, f.Build(T.AddSeconds(12)).Player!.ExactVitals!.Health);
        Assert.Null(f.Build(T.AddSeconds(12)).Player!.ExactVitals!.MaxHealth);
        f.ObserveProvider(Api(20), T.AddSeconds(21));
        Assert.NotNull(f.Build(T.AddSeconds(21)).Player!.ExactVitals!.MaxHealth);
    }
    [Fact]
    public void PrimeOnlyRepublishDoesNotRefreshStats()
    {
        var f = new OriginInboundStatsFusion(); f.ObserveProvider(Api(), T);
        var same = Api(); f.ObserveProvider(same with { Player = same.Player! with { Prime = new() { Done = 3 } } }, T.AddSeconds(12));
        var p = f.Build(T.AddSeconds(12)).Player!;
        Assert.Equal(3, p.Prime!.Done); Assert.Equal(T, p.StatsFieldTimes!["Health"]);
    }
    [Fact]
    public void ExplicitNoDinoClearsButAuthenticationDoesNotMasqueradeAsDeath()
    {
        var f = new OriginInboundStatsFusion(); f.ObserveProvider(Api(), T);
        f.ObserveProvider(new() { SessionState = TelemetrySessionState.AuthenticationRequired }, T.AddSeconds(1));
        Assert.Equal(100, f.Build(T.AddSeconds(1)).Player!.ExactVitals!.Health);
        f.ObserveProvider(new() { ProviderStatsScope = "main/2", ProviderStatsReset = true }, T.AddSeconds(2));
        Assert.Null(f.Build(T.AddSeconds(2)).Player);
    }
}
