using TheIsleOverlay.LocalTelemetry;
namespace TheIsleOverlay.Tests;

public sealed class InboundStatsRestartCacheTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-09-29T07:40:00Z");
    private static VitalsFieldEvidence E(string name, double value, int seconds = 0) =>
        new("pid:start|server:7777|port", 42, name, value, At.AddSeconds(seconds), 1, 400, 1391, "measured-1391-1491");
    private static InboundStatsRestartCache.RestartSnapshot Snapshot => new(1, "pid:start|server:7777|port", 42, At,
        [E("HungerCandidate", 900), E("ThirstCandidate", 840), E("MaxHealthCandidate", 1000),
         E("MaxStaminaCandidate", 400), E("MaxHungerCandidate", 1100), E("HealthCandidate", 1000), E("GrowthCandidate", .8)]);
    [Fact]
    public void SameScopeWithFreshSurvival_RestoresOnlyMaximaWithOriginalTimestamps()
    {
        var fields = InboundStatsRestartCache.SelectRestorable(Snapshot, Snapshot.Scope, 42, At.AddSeconds(10),
            [E("HungerCandidate", 899, 10), E("ThirstCandidate", 839, 10)]);
        Assert.Equal(3, fields.Count);
        Assert.All(fields, f => { Assert.StartsWith("Max", f.Field); Assert.Equal(At, f.ObservedAt); });
    }
    [Theory]
    [InlineData("other:start|server:7777|port", 42UL, 10)]
    [InlineData("pid:start|other:7777|port", 42UL, 10)]
    [InlineData("pid:start|server:7777|other", 42UL, 10)]
    [InlineData("pid:start|server:7777|port", 43UL, 10)]
    [InlineData("pid:start|server:7777|port", 42UL, 61)]
    public void ScopeActorOrRestartWindowMismatch_IsRejected(string scope, ulong owner, int seconds)
    {
        Assert.Empty(InboundStatsRestartCache.SelectRestorable(Snapshot, scope, owner, At.AddSeconds(seconds),
            [E("HungerCandidate", 899, seconds), E("ThirstCandidate", 839, seconds)]));
    }
    [Fact]
    public void PresenceOnlyOrDiscontinuousLife_DoesNotRestore()
    {
        Assert.Empty(InboundStatsRestartCache.SelectRestorable(Snapshot, Snapshot.Scope, 42, At.AddSeconds(10), []));
        Assert.Empty(InboundStatsRestartCache.SelectRestorable(Snapshot, Snapshot.Scope, 42, At.AddSeconds(10),
            [E("HungerCandidate", 30, 10), E("ThirstCandidate", 1000, 10)]));
    }
    [Fact]
    public void FileRoundTrip_DoesNotRestoreUntilFreshSameOwnerEvidence()
    {
        var directory = Path.Combine(Path.GetTempPath(), "isle-cache-test-" + Guid.NewGuid().ToString("N"));
        const string scope = "39208:639000000000000000|173.225.107.226:7777|58291";
        try
        {
            var cache = new InboundStatsRestartCache(directory);
            cache.Save(scope, 42, At, Snapshot.Fields.Select(e => e with { Flow = scope }).ToArray());
            var restarted = new InboundStatsRestartCache(directory);
            Assert.Empty(restarted.Restore(scope, 42, At.AddSeconds(5), []));
            var restored = restarted.Restore(scope, 42, At.AddSeconds(5),
                [E("HungerCandidate", 899, 5) with { Flow = scope }, E("ThirstCandidate", 839, 5) with { Flow = scope }]);
            Assert.Equal(3, restored.Count);
            Assert.All(restored, e => Assert.Equal(At, e.ObservedAt));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
    [Fact]
    public void FreshMaxWinsOverCachedMax()
    {
        var fields = InboundStatsRestartCache.SelectRestorable(Snapshot, Snapshot.Scope, 42, At.AddSeconds(10),
            [E("HungerCandidate", 899, 10), E("ThirstCandidate", 839, 10), E("MaxHealthCandidate", 1200, 10)]);
        Assert.DoesNotContain(fields, f => f.Field == "MaxHealthCandidate");
    }
}
