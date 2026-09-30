using System.Runtime.CompilerServices;
using System.Threading.Channels;
using TheIsleOverlay.Core;
using TheIsleOverlay.LocalTelemetry;
namespace TheIsleOverlay.Tests;

public sealed class OriginFusionSessionTests
{
    [Fact]
    public async Task ApiBaselineAndInboundUpdateReachOverlaySessionWithoutReplacingOriginPrime()
    {
        var api = new Api(); var inbound = new Inbound();
        await using var session = new LocalPositionTelemetrySession(api, inbound, "ORIGIN 5X", enableLocalVitals: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var watch = session.WatchAsync(timeout.Token).GetAsyncEnumerator();
        Assert.True(await watch.MoveNextAsync());
        var requestAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        api.Frames.Writer.TryWrite(new()
        {
            Source = "ORIGIN x5", Success = true, ServerOnline = true, PlayerOnline = true,
            ProviderStatsRequestedAt = requestAt, ProviderStatsScope = "origin/main/1",
            Player = new() { Class = "Rex", Prime = new() { Done = 2 }, ExactVitalsSource = "OriginDashboard",
                ExactVitals = new() { Health = 100, MaxHealth = 100, Stamina = 100, MaxStamina = 100,
                    Hunger = 100, MaxHunger = 100, Thirst = 840, MaxThirst = 1000 } }
        });
        while (watch.Current.Player?.ExactVitals?.MaxHealth != 100) Assert.True(await watch.MoveNextAsync());
        inbound.Frames.Writer.TryWrite(LocalMovementObservation.VitalsOnly(new(DateTimeOffset.UtcNow,
            new() { Health = 30 }, 42), "server:7777"));
        while (watch.Current.Player?.ExactVitals?.Health != 30) Assert.True(await watch.MoveNextAsync());
        Assert.Equal("OriginInbound", watch.Current.Player!.ExactVitalsSource);
        Assert.Equal(100, watch.Current.Player.ExactVitals!.MaxHealth);
        Assert.Equal(30, watch.Current.Player.HealthPercent);
        Assert.Equal(2, watch.Current.Player.Prime!.Done);
        Assert.Equal(requestAt, watch.Current.Player.StatsFieldTimes!["MaxHealth"]);
    }
    private sealed class Api : ITelemetrySession
    {
        public Channel<TelemetrySnapshot> Frames { get; } = Channel.CreateUnbounded<TelemetrySnapshot>();
        public async IAsyncEnumerable<TelemetrySnapshot> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        { await foreach(var frame in Frames.Reader.ReadAllAsync(cancellationToken)) yield return frame; }
        public ValueTask DisposeAsync() { Frames.Writer.TryComplete(); return ValueTask.CompletedTask; }
    }
    private sealed class Inbound : ILocalMovementSource
    {
        public Channel<LocalMovementObservation> Frames { get; } = Channel.CreateUnbounded<LocalMovementObservation>();
        public async IAsyncEnumerable<LocalMovementObservation> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        { await foreach(var frame in Frames.Reader.ReadAllAsync(cancellationToken)) yield return frame; }
        public ValueTask DisposeAsync() { Frames.Writer.TryComplete(); return ValueTask.CompletedTask; }
    }
}
