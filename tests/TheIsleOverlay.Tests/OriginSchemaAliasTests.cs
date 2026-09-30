using System.Text.Json;
using TheIsleOverlay.Origin;
using TheIsleOverlay.LocalTelemetry;
namespace TheIsleOverlay.Tests;

public sealed class OriginSchemaAliasTests
{
    // stam/maxStam keys were observed in 84 successful real responses.
    // Numeric stamina values are synthetic: prior logs omitted those values.
    [Fact]
    public async Task ObservedStamSchemaReachesCompleteFusionBaseline()
    {
        await using var session = new OriginStatsSession(new AliasClient(), OriginServer.All[1]);
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await using var reader = session.WatchAsync(ct.Token).GetAsyncEnumerator();
        while (await reader.MoveNextAsync() && reader.Current.Player is null) { }
        Assert.Equal(40, reader.Current.Player!.ExactVitals!.Stamina);
        Assert.Equal(50, reader.Current.Player.ExactVitals.MaxStamina);
        var fusion = new OriginInboundStatsFusion();
        fusion.ObserveProvider(reader.Current, DateTimeOffset.UtcNow);
        var merged = fusion.Build(DateTimeOffset.UtcNow);
        Assert.Equal(50, merged.Player!.ExactVitals!.MaxStamina);
        Assert.Equal("Origin", merged.Player.StatsFieldSources!["MaxStamina"]);
        Assert.Equal(.3204, merged.Player.ExactVitals.Growth);
    }
    private sealed class AliasClient : IOriginStatsClient
    {
        public OriginServer? PreferredServer => OriginServer.All[1];
        public Task<OriginCommandResult> ExecuteHealthAsync(OriginServer s, CancellationToken ct = default)
            => Task.FromResult(new OriginCommandResult("completed", JsonSerializer.SerializeToElement(new
            { success = true, species = "Rex", hp = 110, maxHp = 110, stam = 40, maxStam = 50,
                hunger = 38, maxHunger = 55, thirst = 934, maxThirst = 1000, growth = .3204 }), null));
        public Task<OriginCommandResult> ExecutePrimeAsync(OriginServer s, CancellationToken ct = default)
            => Task.FromResult(new OriginCommandResult("failed", null, "not needed"));
        public void Dispose() { }
    }
}
